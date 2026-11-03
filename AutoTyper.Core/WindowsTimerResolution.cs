using System.Runtime.InteropServices;

namespace AutoTyper;

// Without this, Windows rounds short waits up to about 15 ms. Does nothing on other systems.
internal static class WindowsTimerResolution
{
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    static extern uint TimeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    static extern uint TimeEndPeriod(uint milliseconds);

    internal static void Begin()
    {
        if (OperatingSystem.IsWindows()) TimeBeginPeriod(1);
    }

    internal static void End()
    {
        if (OperatingSystem.IsWindows()) TimeEndPeriod(1);
    }
}
