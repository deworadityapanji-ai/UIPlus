using System;
using System.Diagnostics;

namespace UIPlus;

// Measure the synchronous work on the UI thread, excluding time spent waiting for I/O.
internal readonly struct UiTiming : IDisposable
{
    private readonly string _step;
    private readonly long _start;

    internal UiTiming(string step)
    {
        _step = step;
        _start = Stopwatch.GetTimestamp();
    }

    public void Dispose()
    {
        double milliseconds = (Stopwatch.GetTimestamp() - _start) * 1000d / Stopwatch.Frequency;
        if (milliseconds > 15d) Plugin.Logger.LogInfo($"UI Plus timing: {_step} took {milliseconds:0.0} ms");
    }
}
