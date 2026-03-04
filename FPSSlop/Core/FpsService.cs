using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace FPSSlop.Core
{
    public sealed class FpsService : IDisposable
    {
        private const string NO_SELECTED_APP = "NONE";

        /// <summary>
        /// Process name to track. Empty / "Auto" = first non-ignored presenter.
        /// </summary>
        public string TargetProcessName { get; set; } = "";

        /// <summary>
        /// All process names seen in the current PresentMon session (refreshed every 10s).
        /// </summary>
        public IReadOnlyList<string> CurrentApps
        {
            get { lock (_appsLock) return _currentApps.ToList(); }
        }

        private readonly object _lock    = new();
        private readonly object _appsLock = new();
        private float _fps, _frameTimeMs, _low1, _low01;
        private readonly HashSet<string> _currentApps = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        private Process?  _presentMon;
        private Thread?   _watchThread;
        private volatile bool _running;

        // Rolling per-process frame time queues
        private readonly Dictionary<string, Queue<float>> _framesByApp = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _ftLock = new();

        public void Start()
        {
            _running = true;
            StartPresentMon();

            _watchThread = new Thread(WatchLoop)
            {
                IsBackground = true,
                Name = "FPSSlop.FpsWatch"
            };
            _watchThread.Start();
        }

        // ── PresentMon launch ─────────────────────────────────────────────────

        private static string PresentMonPath() =>
            Path.Combine(AppContext.BaseDirectory, "PresentMon.exe");

        private static string IgnoredProcessesPath() =>
            Path.Combine(AppContext.BaseDirectory, "ignored-processes.txt");

        private static string BuildExcludeArgs()
        {
            try
            {
                string path = IgnoredProcessesPath();
                if (!File.Exists(path)) return "";
                return string.Join(" ", File.ReadAllLines(path)
                    .Select(l => l.Trim().Trim('"'))
                    .Where(l => l.Length > 0)
                    .Select(l => $"--exclude {l}"));
            }
            catch { return ""; }
        }

        private void StartPresentMon()
        {
            try
            {
                string pmPath = PresentMonPath();
                if (!File.Exists(pmPath)) return;

                // Kill stale session
                try
                {
                    using var killer = Process.Start(new ProcessStartInfo(pmPath,
                        "--terminate_existing_session --no_console_stats --session_name FPSSlopPM")
                    { CreateNoWindow = true, UseShellExecute = false });
                    killer?.WaitForExit(1000);
                }
                catch { }

                string excludes = BuildExcludeArgs();
                var psi = new ProcessStartInfo(pmPath,
                    $"--stop_existing_session --no_console_stats --output_stdout --session_name FPSSlopPM {excludes}")
                {
                    UseShellExecute         = false,
                    RedirectStandardOutput  = true,
                    RedirectStandardError   = true,
                    CreateNoWindow          = true
                };

                _presentMon = Process.Start(psi);
                if (_presentMon == null) return;

                // Async stderr drain
                _presentMon.ErrorDataReceived += (_, e) => { };
                _presentMon.BeginErrorReadLine();

                // Async stdout → ParseLine
                _presentMon.OutputDataReceived += (_, e) => ParseLine(e.Data);
                _presentMon.BeginOutputReadLine();
            }
            catch { }
        }

        // ── CSV parsing ───────────────────────────────────────────────────────

        private bool   _headerParsed   = false;
        private int    _appIdx         = 0;
        private int    _msBetweenIdx   = 9; // default for v1.9.x

        private void ParseLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            if (!_headerParsed)
            {
                var cols = line.Split(',');
                for (int i = 0; i < cols.Length; i++)
                {
                    string c = cols[i].Trim();
                    if (c.Equals("Application",       StringComparison.OrdinalIgnoreCase)) _appIdx       = i;
                    if (c.Equals("msBetweenPresents", StringComparison.OrdinalIgnoreCase)) _msBetweenIdx = i;
                }
                _headerParsed = true;
                return;
            }

            var fields = line.Split(',');
            if (fields.Length <= Math.Max(_appIdx, _msBetweenIdx)) return;

            string appName = fields[_appIdx].Trim();
            if (string.IsNullOrEmpty(appName)) return;

            if (!float.TryParse(fields[_msBetweenIdx],
                NumberStyles.Float, CultureInfo.InvariantCulture, out float ms) || ms <= 0)
                return;

            // Track seen apps
            lock (_appsLock) _currentApps.Add(appName);

            // Filter: if a target is set, only accept that app
            string target = TargetProcessName.Trim();
            if (!string.IsNullOrEmpty(target) && target != "Auto")
            {
                if (!appName.Equals(target, StringComparison.OrdinalIgnoreCase) &&
                    !appName.Equals(target.Replace(".exe", ""), StringComparison.OrdinalIgnoreCase))
                    return;
            }
            // Auto: accept any app (excluded ones were already filtered by --exclude flags)

            lock (_ftLock)
            {
                if (!_framesByApp.TryGetValue(appName, out var q))
                    _framesByApp[appName] = q = new Queue<float>();
                q.Enqueue(ms);
                while (q.Count > 500) q.Dequeue();
            }
        }

        // ── Stats computation ─────────────────────────────────────────────────

        private void WatchLoop()
        {
            var appsRefreshTimer = System.Diagnostics.Stopwatch.StartNew();

            while (_running && !_disposed)
            {
                // Clear seen apps every 10s (same as CleanMeter)
                if (appsRefreshTimer.ElapsedMilliseconds > 10_000)
                {
                    lock (_appsLock) _currentApps.Clear();
                    appsRefreshTimer.Restart();
                }

                // Pick which app's frames to use
                string target = TargetProcessName.Trim();
                float[] fts = Array.Empty<float>();

                lock (_ftLock)
                {
                    if (!string.IsNullOrEmpty(target) && target != "Auto")
                    {
                        // Fixed target
                        if (_framesByApp.TryGetValue(target, out var q))
                            fts = TrimToOneSecond(q);
                    }
                    else
                    {
                        // Auto: use whichever app has the most recent frames
                        float[] best = Array.Empty<float>();
                        foreach (var kv in _framesByApp)
                        {
                            var trimmed = TrimToOneSecond(kv.Value);
                            if (trimmed.Length > best.Length) best = trimmed;
                        }
                        fts = best;
                    }

                    // Prune stale queues
                    foreach (var key in _framesByApp.Keys.ToList())
                    {
                        if (_framesByApp[key].Count == 0)
                            _framesByApp.Remove(key);
                    }
                }

                if (fts.Length >= 2)
                {
                    float totalMs = fts.Sum();
                    float fps     = totalMs > 0 ? fts.Length / (totalMs / 1000f) : 0;
                    float avgFt   = totalMs / fts.Length;

                    var sorted = fts.OrderByDescending(x => x).ToArray();
                    int idx1  = Math.Max(1, (int)(sorted.Length * 0.01));
                    int idx01 = Math.Max(1, (int)(sorted.Length * 0.001));
                    float low1  = 1000f / sorted.Take(idx1).Average();
                    float low01 = 1000f / sorted.Take(idx01).Average();

                    lock (_lock) { _fps = fps; _frameTimeMs = avgFt; _low1 = low1; _low01 = low01; }
                }
                else
                {
                    lock (_lock) { _fps = 0; _frameTimeMs = 0; _low1 = 0; _low01 = 0; }
                }

                // Restart PresentMon if it died
                if (_presentMon?.HasExited == true && _running)
                {
                    Thread.Sleep(2000);
                    _headerParsed = false;
                    StartPresentMon();
                }

                Thread.Sleep(200);
            }
        }

        private static float[] TrimToOneSecond(Queue<float> q)
        {
            while (q.Count > 1 && q.Sum() > 1000f)
                q.Dequeue();
            return q.ToArray();
        }

        // ── Public API ────────────────────────────────────────────────────────

        public (float fps, float frameTimeMs, float low1, float low01) GetStats()
        {
            lock (_lock) return (_fps, _frameTimeMs, _low1, _low01);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _running  = false;
            try { _presentMon?.Kill(); } catch { }
            _presentMon?.Dispose();
            _watchThread?.Join(1000);
        }
    }
}
