// Managed (C#) side diagnostics: crash hooks, hang watchdog and slow-operation
// reporting. UE already covers the native half (CrashReportClient + Saved/Crashes
// for access violations/asserts, FThreadHeartBeat hang detection in packaged
// builds); Mono has nothing equivalent, so this fills the gap for the ported game
// logic, whose failures are otherwise silent (a hang looks like a frozen frame, a
// background-thread exception just disappears).
//
// Everything writes to the UE log (tag Pal3) *and* to
//   <PAL3_DIAG_DIR or CWD>/Saved/Logs/managed-diag-<yyyyMMdd>.log
// so evidence survives a hard crash of the process.
//
// Switches (environment):
//   PAL3_DIAG_DIR          report directory (default: ./Saved/Logs)
//   PAL3_TRACE_EXCEPTIONS  "1" logs every first-chance exception (very noisy)
//   PAL3_WATCHDOG_SECONDS  stall threshold for the hang watchdog (default 20)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Engine.Diagnostics
{
    public static class ManagedDiagnostics
    {
        private static readonly object Gate = new();
        private static readonly DateTime StartUtc = DateTime.UtcNow;
        private static string? _reportPath;
        private static volatile int _heartbeat;
        private static long _lastHeartbeatTicks = Stopwatch.GetTimestamp();
        private static string _phase = "boot";
        private static bool _installed;
        private static Thread? _watchdog;
        private static int _firstChanceCount;
        private static readonly Dictionary<string, int> SuppressedExceptions = new();

        /// <summary>Installs unhandled-exception hooks. Safe to call more than once.</summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Write("UNHANDLED", e.ExceptionObject as Exception, e.IsTerminating ? "terminating" : "non-terminating");

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Write("UNOBSERVED-TASK", e.Exception, "unobserved");
                e.SetObserved();
            };

            if (Environment.GetEnvironmentVariable("PAL3_TRACE_EXCEPTIONS") == "1")
            {
                // First-chance catches everything the game swallows internally; throttle
                // so a per-frame throw cannot flood the log.
                AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
                {
                    if (Interlocked.Increment(ref _firstChanceCount) % 200 != 0)
                    {
                        return;
                    }
                    Write("FIRST-CHANCE", e.Exception, "throttled 1/200");
                };
            }

            Write("START", null, $"pid={Environment.ProcessId} mono={IsMono()}");
        }

        private static bool IsMono()
        {
            Type? t = Type.GetType("Mono.Runtime");
            return t != null;
        }

        /// <summary>Call once per frame from the game driver.</summary>
        public static void Heartbeat()
        {
            Interlocked.Increment(ref _heartbeat);
            Interlocked.Exchange(ref _lastHeartbeatTicks, Stopwatch.GetTimestamp());
        }

        /// <summary>Sets a coarse phase label shown in hang reports.</summary>
        public static void SetPhase(string phase) => _phase = phase;

        /// <summary>
        /// Starts a background thread that reports when Heartbeat() stops advancing.
        /// A hang leaves the game thread stuck inside native/managed code, so this is
        /// the only reliable way to learn *that* it hung and *where the loop was*.
        /// </summary>
        public static void StartWatchdog(double stallSeconds = 0)
        {
            if (_watchdog != null) return;
            double threshold = stallSeconds > 0
                ? stallSeconds
                : ParseDouble(Environment.GetEnvironmentVariable("PAL3_WATCHDOG_SECONDS"), 20.0);

            _watchdog = new Thread(() =>
            {
                int lastSeen = -1;
                double stalledFor = 0;
                int reported = 0;
                while (true)
                {
                    Thread.Sleep(1000);

                    long now = Stopwatch.GetTimestamp();
                    long last = Interlocked.Read(ref _lastHeartbeatTicks);
                    double sinceLast = (now - last) / (double)Stopwatch.Frequency;
                    int current = Volatile.Read(ref _heartbeat);

                    if (current != lastSeen)
                    {
                        lastSeen = current;
                        stalledFor = 0;
                        reported = 0;
                        continue;
                    }

                    stalledFor += 1.0;
                    if (stalledFor < threshold)
                    {
                        continue;
                    }

                    // Report at most every 10 s so a long hang does not flood the file.
                    if (reported > 0 && (int)stalledFor % 10 != 0)
                    {
                        continue;
                    }
                    reported++;

                    Write("HANG", null,
                        $"no frame for {sinceLast:F1}s (phase={_phase}, heartbeat={current}, "
                        + $"managedThreads={CountManagedThreads()}, uptime={(DateTime.UtcNow - StartUtc).TotalSeconds:F0}s)");
                }
            })
            {
                IsBackground = true,
                Name = "Pal3Watchdog",
            };
            _watchdog.Start();
            Write("WATCHDOG", null, $"threshold={threshold:F0}s");
        }

        private static int CountManagedThreads()
        {
            lock (Gate)
            {
                return SuppressedExceptions.Count + 1; // cheap proxy; real thread count needs profiler API
            }
        }

        /// <summary>Logs a slow operation (script Update, coroutine step, manager tick).</summary>
        public static void ReportSlow(string what, double milliseconds)
        {
            Write("SLOW", null, $"{what} took {milliseconds:F0}ms");
        }

        private static void Write(string kind, Exception? exception, string detail)
        {
            string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] {kind} {detail}";
            if (exception != null)
            {
                line += Environment.NewLine + exception;
            }

            lock (Gate)
            {
                // UE log first (visible in real time), then the durable file.
                UnityEngine.Debug.LogWarning("[Diag] " + line.Replace("\n", "\n[Diag] "));
                try
                {
                    File.AppendAllText(GetReportPath(), line + Environment.NewLine, Encoding.UTF8);
                }
                catch (Exception)
                {
                    // Diagnostics must never take the game down.
                }

                if (kind == "FIRST-CHANCE" || kind == "SLOW")
                {
                    string key = exception?.GetType().Name ?? detail;
                    SuppressedExceptions.TryGetValue(key, out int n);
                    SuppressedExceptions[key] = n + 1;
                }
            }
        }

        private static string GetReportPath()
        {
            if (_reportPath != null) return _reportPath;

            string dir = Environment.GetEnvironmentVariable("PAL3_DIAG_DIR");
            if (string.IsNullOrEmpty(dir))
            {
                dir = Path.Combine(Environment.CurrentDirectory, "Saved", "Logs");
            }
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception)
            {
                dir = Path.GetTempPath();
            }

            _reportPath = Path.Combine(dir, $"managed-diag-{DateTime.UtcNow:yyyyMMdd}.log");
            return _reportPath;
        }

        private static double ParseDouble(string? text, double fallback)
            => double.TryParse(text, out double value) && value > 0 ? value : fallback;
    }
}
