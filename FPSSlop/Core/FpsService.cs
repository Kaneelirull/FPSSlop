using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace FPSSlop.Core
{
    public sealed class FpsService : IDisposable
    {
        public string TargetProcessName { get; set; } = "";

        public IReadOnlyList<string> CurrentApps
        {
            get { lock (_appsLock) return _currentApps.ToList(); }
        }

        private readonly object _lock     = new();
        private readonly object _appsLock = new();
        private float _fps, _frameTimeMs, _low1, _low01;
        private readonly HashSet<string> _currentApps = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        private Process? _presentMon;
        private Thread?  _watchThread;
        private volatile bool _running;

        private readonly Dictionary<string, Queue<float>> _framesByApp = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _ftLock = new();

        private static readonly string LogBase = Path.Combine(Path.GetTempPath(), "FPSSlop");
        private static readonly string CsvPath = Path.Combine(LogBase, "presentmon.csv");

        public void Start()
        {
            _running = true;
            Directory.CreateDirectory(LogBase);

            File.WriteAllText(Path.Combine(LogBase, "startup.txt"),
                $"ProcessPath={Environment.ProcessPath}\n" +
                $"AppDir={AppDir()}\n" +
                $"PresentMonPath={PresentMonPath()}\n" +
                $"PresentMonExists={File.Exists(PresentMonPath())}\n" +
                $"IgnoredExists={File.Exists(IgnoredProcessesPath())}\n");

            StartPresentMon();

            _watchThread = new Thread(WatchLoop)
            {
                IsBackground = true,
                Name = "FPSSlop.FpsWatch"
            };
            _watchThread.Start();
        }

        private static string AppDir() =>
            Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory)
            ?? AppContext.BaseDirectory;

        private static string PresentMonPath() =>
            Path.Combine(AppDir(), "PresentMon.exe");

        private static string IgnoredProcessesPath() =>
            Path.Combine(AppDir(), "ignored-processes.txt");

        private static string BuildExcludeArgs()
        {
            try
            {
                string path = IgnoredProcessesPath();
                if (!File.Exists(path)) return "";
                return string.Join(" ", File.ReadAllLines(path)
                    .Select(l => l.Trim().Trim('"'))
                    .Where(l => l.Length > 0)
                    .Select(l => $"-exclude {l}"));
            }
            catch { return ""; }
        }

        private void StartPresentMon()
        {
            try
            {
                string pmPath = PresentMonPath();
                if (!File.Exists(pmPath)) return;

                // Terminate any existing session first
                try
                {
                    using var killer = Process.Start(new ProcessStartInfo(pmPath,
                        $"-terminate_existing -session_name FPSSlopPM")
                    { CreateNoWindow = true, UseShellExecute = false });
                    killer?.WaitForExit(2000);
                }
                catch { }

                try { if (File.Exists(CsvPath)) File.Delete(CsvPath); } catch { }

                string excludes = BuildExcludeArgs();
                string args = $"-stop_existing_session -no_top -output_stdout -session_name FPSSlopPM {excludes}";

                File.WriteAllText(Path.Combine(LogBase, "pm_args.txt"),
                    $"pmPath={pmPath}\nargs={args}\nappDir={AppDir()}\n");

                var psi = new ProcessStartInfo(pmPath, args)
                {
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true
                };

                _presentMon = Process.Start(psi);
                if (_presentMon == null) return;

                _presentMon.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        File.AppendAllText(Path.Combine(LogBase, "pm_err.txt"), e.Data + "\n");
                };
                _presentMon.BeginErrorReadLine();

                int lineCount = 0;
                _presentMon.OutputDataReceived += (_, e) =>
                {
                    if (lineCount++ < 10)
                        File.AppendAllText(Path.Combine(LogBase, "pm_out.txt"), (e.Data ?? "<null>") + "\n");
                    ParseLine(e.Data);
                };
                _presentMon.BeginOutputReadLine();

                _ = Task.Run(async () =>
                {
                    await _presentMon.WaitForExitAsync();
                    File.AppendAllText(Path.Combine(LogBase, "pm_err.txt"),
                        $"PresentMon exited with code {_presentMon.ExitCode}\n");
                });
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(LogBase, "pm_err.txt"), ex.ToString());
            }
        }

        private bool  _headerParsed = false;
        private int   _appIdx       = 0;
        private int   _msBetweenIdx = 9;

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

            lock (_appsLock) _currentApps.Add(appName);

            string target = TargetProcessName.Trim();
            if (!string.IsNullOrEmpty(target) && target != "Auto")
            {
                string targetNoExt = target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? target[..^4] : target;
                if (!appName.Equals(target,      StringComparison.OrdinalIgnoreCase) &&
                    !appName.Equals(targetNoExt, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            lock (_ftLock)
            {
                if (!_framesByApp.TryGetValue(appName, out var q))
                    _framesByApp[appName] = q = new Queue<float>();
                q.Enqueue(ms);
                while (q.Count > 500) q.Dequeue();
            }
        }

        private void WatchLoop()
        {
            var appsTimer = System.Diagnostics.Stopwatch.StartNew();

            while (_running && !_disposed)
            {
                if (appsTimer.ElapsedMilliseconds > 10_000)
                {
                    lock (_appsLock) _currentApps.Clear();
                    appsTimer.Restart();
                }

                string target = TargetProcessName.Trim();
                float[] fts = Array.Empty<float>();

                lock (_ftLock)
                {
                    if (!string.IsNullOrEmpty(target) && target != "Auto")
                    {
                        string targetNoExt = target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                            ? target[..^4] : target;
                        foreach (var kv in _framesByApp)
                        {
                            string keyNoExt = kv.Key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                                ? kv.Key[..^4] : kv.Key;
                            if (keyNoExt.Equals(targetNoExt, StringComparison.OrdinalIgnoreCase))
                            {
                                fts = kv.Value.ToArray();
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Auto: pick the app with the highest instantaneous FPS.
                        float[] best    = Array.Empty<float>();
                        float   bestFps = 0;

                        foreach (var kv in _framesByApp)
                        {
                            if (kv.Key.Equals("dwm.exe", StringComparison.OrdinalIgnoreCase)) continue;

                            var snap = kv.Value.ToArray();
                            if (snap.Length == 0) continue;

                            float snapFps = snap.Length / (snap.Sum() / 1000f);
                            if (snapFps > bestFps) { bestFps = snapFps; best = snap; }
                        }

                        fts = best;
                    }

                    foreach (var key in _framesByApp.Keys.ToList())
                        if (_framesByApp[key].Count == 0)
                            _framesByApp.Remove(key);
                }

                if (fts.Length > 0)
                {
                    float sum = 0;
                    int start = fts.Length - 1;
                    while (start > 0 && sum + fts[start] < 1000f)
                    {
                        sum += fts[start];
                        start--;
                    }
                    fts = fts[start..];
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

                if (_presentMon?.HasExited == true && _running)
                {
                    Thread.Sleep(2000);
                    _headerParsed = false;
                    StartPresentMon();
                }

                Thread.Sleep(200);
            }
        }

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