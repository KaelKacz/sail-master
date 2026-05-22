using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SailMaster
{
    internal static class SailMasterProfiler
    {
        private static readonly Dictionary<string, TimingStats> timings = new Dictionary<string, TimingStats>();
        private static readonly double tickToMilliseconds = 1000.0 / Stopwatch.Frequency;
        private static float nextReportTime;

        public static Sample Scope(string name)
        {
            return new Sample(name);
        }

        public static void MaybeReport(float time)
        {
            if (SailMasterMain.timingProfilerLog == null || !SailMasterMain.timingProfilerLog.Value)
            {
                return;
            }

            if (time < nextReportTime)
            {
                return;
            }

            float interval = Math.Max(1f, SailMasterMain.timingProfilerIntervalSeconds?.Value ?? 5f);
            nextReportTime = time + interval;
            if (timings.Count == 0)
            {
                return;
            }

            SailMasterMain.Logger?.LogInfo($"SailMaster timing over ~{interval:F1}s:");
            foreach (var timing in timings)
            {
                TimingStats stats = timing.Value;
                if (stats.Count == 0) continue;

                double totalMs = stats.TotalTicks * tickToMilliseconds;
                double maxMs = stats.MaxTicks * tickToMilliseconds;
                double avgMs = totalMs / stats.Count;
                SailMasterMain.Logger?.LogInfo(
                    $"{timing.Key}: calls={stats.Count} total={totalMs:F3}ms avg={avgMs:F4}ms max={maxMs:F3}ms");
            }

            timings.Clear();
        }

        private static bool TimingLogActive()
        {
            return SailMasterMain.timingProfilerLog != null && SailMasterMain.timingProfilerLog.Value;
        }

        private static void Record(string name, long elapsedTicks)
        {
            if (!timings.TryGetValue(name, out TimingStats stats))
            {
                stats = new TimingStats();
            }

            stats.Count++;
            stats.TotalTicks += elapsedTicks;
            if (elapsedTicks > stats.MaxTicks)
            {
                stats.MaxTicks = elapsedTicks;
            }

            timings[name] = stats;
        }

        private struct TimingStats
        {
            public int Count;
            public long TotalTicks;
            public long MaxTicks;
        }

        public readonly struct Sample : IDisposable
        {
            private readonly string name;
            private readonly bool timingActive;
            private readonly long startTicks;

            public Sample(string name)
            {
                this.name = name;
                timingActive = TimingLogActive();
                startTicks = timingActive ? Stopwatch.GetTimestamp() : 0L;
            }

            public void Dispose()
            {
                if (timingActive)
                {
                    Record(name, Stopwatch.GetTimestamp() - startTicks);
                }
            }
        }
    }
}
