using System.Diagnostics;
using System.IO;
using MacroFlow.App.Input;

namespace MacroFlow.App.Services;

internal static class ForegroundProcessService
{
    internal static bool IsForeground(string? expectedProcessName)
    {
        if (string.IsNullOrWhiteSpace(expectedProcessName)) return true;
        var window = NativeMethods.GetForegroundWindow();
        if (window == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return false;

        try
        {
            var actual = Process.GetProcessById((int)processId).ProcessName;
            var expected = Path.GetFileNameWithoutExtension(expectedProcessName.Trim());
            return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
