using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace FPSSlop.Core
{
    /// <summary>
    /// Measures FPS by spawning PresentMon.exe (bundled) as a child process.
    /// PresentMon uses ETW to capture DXGI present events — the same method used
    /// by FrameView, CapFrameX, and other professional frame-time tools.
    /// It outputs one CSV row per frame with msBetweenPresents which we use to
    /// compute a rolling 1-second FPS average.
    /// </summary>
    public sealed class FpsService : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern nint GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);

        private readonly object _lock = new();
        private float _fps, _frameTimeMs, _low1, _low01;
        private bool _disposed;

        /// <summary>
        /// Process name to track (e.g. "cs2"). Empty = auto foreground.
        /// </summary>
        public string TargetProcessName { get; set; } = "";

        private Process? _presentMon;
        private Thread? _readerThread;
        private Thread? _watchThread;
        private volatile bool _running;

        // Rolling window of frame times (ms) from PresentMon
        private readonly Queue<float> _frameTimes = new();
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

        private string PresentMonPath()
        {
            // Alongside our own exe (works for both debug and publish)
            string dir = AppContext.BaseDirectory;
            return Path.Combine(dir, "PresentMon.exe");
        }

        private void StartPresentMon()
        {
            try
            {
                string pmPath = PresentMonPath();
                if (!File.Exists(pmPath)) return;

                // Kill any stale session first
                try
                {
                    var killer = Process.Start(new ProcessStartInfo(pmPath,
                        "-terminate_existing -session_name FPSSlopPM")
                    { CreateNoWindow = true, UseShellExecute = false });
                    killer?.WaitForExit(1000);
                    killer?.Dispose();
                }
                catch { }

                var psi = new ProcessStartInfo(pmPath,
                    "-output_stdout -no_top -stop_existing_session -session_name FPSSlopPM")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _presentMon = Process.Start(psi);
                if (_presentMon == null) return;

                // Log stderr to temp file for debugging
                var pm = _presentMon;
                Task.Run(() =>
                {
                    try
                    {
                        string err = pm.StandardError.ReadToEnd();
                        if (!string.IsNullOrWhiteSpace(err))
                            File.WriteAllText(Path.Combine(Path.GetTempPath(), "fpSSlop_pm_err.txt"), err);
                    }
                    catch { }
                });

                _readerThread = new Thread(() => ReadPresentMonOutput(_presentMon))
                {
                    IsBackground = true,
                    Name = "FPSSlop.FpsReader"
                };
                _readerThread.Start();
            }
            catch { }
        }

        private void ReadPresentMonOutput(Process pm)
        {
            // CSV header (v1.x): Application,ProcessID,SwapChainAddress,Runtime,
            //   SyncInterval,PresentFlags,AllowsTearing,PresentMode,
            //   Dropped,TimeInSeconds,msBetweenPresents,msInPresentAPI,
            //   msBetweenDisplayChange,msUntilRenderComplete,msUntilDisplayed
            // We only need msBetweenPresents (index 10)

            int msBetweenIdx = -1;
            bool headerParsed = false;

            try
            {
                while (!pm.StandardOutput.EndOfStream && _running)
                {
                    string? line = pm.StandardOutput.ReadLine();
                    if (line == null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (!headerParsed)
                    {
                        // Log header line for debugging
                        File.WriteAllText(Path.Combine(Path.GetTempPath(), "fpSSlop_pm_header.txt"), line);

                        // Find column index
                        var cols = line.Split(',');
                        for (int i = 0; i < cols.Length; i++)
                        {
                            if (cols[i].Trim().Equals("msBetweenPresents", StringComparison.OrdinalIgnoreCase))
                            { msBetweenIdx = i; break; }
                        }
                        headerParsed = true;
                        continue;
                    }

                    if (msBetweenIdx < 0) continue;

                    var fields = line.Split(',');
                    if (fields.Length <= msBetweenIdx) continue;

                    if (!float.TryParse(fields[msBetweenIdx],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float ms) || ms <= 0) continue;

                    // Filter by target process
                    if (fields.Length > 1)
                    {
                        string target = TargetProcessName.Trim();
                        if (string.IsNullOrEmpty(target))
                        {
                            // Auto: foreground window PID
                            if (uint.TryParse(fields[1].Trim(), out uint framePid))
                            {
                                GetWindowThreadProcessId(GetForegroundWindow(), out uint fgPid);
                                if (framePid != fgPid) continue;
                            }
                        }
                        else
                        {
                            // Fixed process name match (fields[0] = Application name)
                            string appName = fields[0].Trim();
                            if (!appName.Equals(target, StringComparison.OrdinalIgnoreCase) &&
                                !appName.Equals(target + ".exe", StringComparison.OrdinalIgnoreCase))
                                continue;
                        }
                    }

                    lock (_ftLock)
                    {
                        _frameTimes.Enqueue(ms);
                        // Keep only 1 second worth (rough upper bound: 500 frames)
                        while (_frameTimes.Count > 500) _frameTimes.Dequeue();
                    }
                }
            }
            catch { }
        }

        private void WatchLoop()
        {
            while (_running && !_disposed)
            {
                float[] fts;
                lock (_ftLock)
                {
                    // Keep only frames that sum to <= 1000ms (1 second window)
                    while (_frameTimes.Count > 1 && _frameTimes.Sum() > 1000f)
                        _frameTimes.Dequeue();
                    fts = _frameTimes.ToArray();
                }

                if (fts.Length >= 2)
                {
                    float totalMs = fts.Sum();
                    float fps = totalMs > 0 ? fts.Length / (totalMs / 1000f) : 0;
                    float avgFt = totalMs / fts.Length;

                    var sorted = fts.OrderByDescending(x => x).ToArray();
                    int idx1  = Math.Max(1, (int)(sorted.Length * 0.01));
                    int idx01 = Math.Max(1, (int)(sorted.Length * 0.001));
                    float low1  = 1000f / sorted.Take(idx1).Average();
                    float low01 = 1000f / sorted.Take(idx01).Average();

                    lock (_lock)
                    {
                        _fps         = fps;
                        _frameTimeMs = avgFt;
                        _low1        = low1;
                        _low01       = low01;
                    }
                }
                else
                {
                    lock (_lock) { _fps = 0; _frameTimeMs = 0; _low1 = 0; _low01 = 0; }
                }

                if (_presentMon?.HasExited == true && _running)
                {
                    Thread.Sleep(2000);
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
            _running = false;
            try { _presentMon?.Kill(); } catch { }
            _presentMon?.Dispose();
            _readerThread?.Join(1000);
            _watchThread?.Join(1000);
        }
    }
}
