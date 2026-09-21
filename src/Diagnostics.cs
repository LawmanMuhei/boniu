using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace MiniView.WebView2App
{
    /// <summary>
    /// Ring buffer + watchdog used to diagnose UI-thread stalls. Mark() and Tick() run on the
    /// hot paths and must never touch the disk; only discrete events and the watchdog write.
    /// </summary>
    internal static class Diagnostics
    {
        private const int RingSize = 96;

        private static readonly object Gate = new object();
        private static readonly string[] Ring = new string[RingSize];
        private static readonly long[] RingTicks = new long[RingSize];

        private static string logPath;
        private static int ringIndex;
        private static long uiTicks;
        private static long lastSeenTicks;
        private static int stallPeriods;
        private static Timer watchdog;

        internal static string LogPath { get { return logPath; } }

        internal static void Start(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                logPath = Path.Combine(folder, "run.log");
                FileInfo info = new FileInfo(logPath);
                if (info.Exists && info.Length > 512 * 1024) info.Delete();
            }
            catch
            {
                logPath = null;
                return;
            }

            lastSeenTicks = Interlocked.Read(ref uiTicks);
            watchdog = new Timer(Guard, null, 2000, 2000);
            Log("==== session start pid=" + Process.GetCurrentProcess().Id
                + " build=" + BuildVersion() + " ====");
        }

        private static string BuildVersion()
        {
            try
            {
                return FileVersionInfo.GetVersionInfo(typeof(Diagnostics).Assembly.Location).FileVersion;
            }
            catch
            {
                return "unknown";
            }
        }

        internal static void Tick()
        {
            Interlocked.Increment(ref uiTicks);
        }

        internal static void Mark(string step)
        {
            int slot = Interlocked.Increment(ref ringIndex) & (RingSize - 1);
            Ring[slot] = step;
            RingTicks[slot] = Interlocked.Read(ref uiTicks);
        }

        internal static void Log(string message)
        {
            string target = logPath;
            if (target == null) return;
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(target, string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [t{1}] {2}{3}",
                        DateTime.Now, Thread.CurrentThread.ManagedThreadId, message, Environment.NewLine));
                }
            }
            catch
            {
            }
        }

        internal static void LogException(string context, Exception exception)
        {
            if (exception == null) { Log(context); return; }
            Log(context + ": " + exception.GetType().Name + ": " + exception.Message
                + Environment.NewLine + exception.StackTrace);
            AggregateException aggregate = exception as AggregateException;
            if (aggregate != null && aggregate.InnerException != null)
                LogException(context + " (inner)", aggregate.InnerException);
        }

        private static void Guard(object state)
        {
            long now = Interlocked.Read(ref uiTicks);
            if (now == 0) return;
            if (now == lastSeenTicks)
            {
                stallPeriods++;
                if (stallPeriods == 2 || stallPeriods % 10 == 0) DumpRing(stallPeriods * 2000, now);
            }
            else
            {
                if (stallPeriods >= 2) Log("UI resumed after ~" + (stallPeriods * 2000) + "ms");
                lastSeenTicks = now;
                stallPeriods = 0;
            }
        }

        private static void DumpRing(long stalledMs, long now)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("UI STALLED ~").Append(stalledMs).Append("ms uiTicks=").Append(now).AppendLine();
            int start = (ringIndex + 1) & (RingSize - 1);
            for (int i = 0; i < RingSize; i++)
            {
                int slot = (start + i) & (RingSize - 1);
                if (Ring[slot] == null) continue;
                builder.Append("  ").Append(RingTicks[slot]).Append("  ").AppendLine(Ring[slot]);
            }
            Log(builder.ToString());
        }
    }
}
