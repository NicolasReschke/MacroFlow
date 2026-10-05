using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MacroFlow.App.Input;

internal sealed record GlobalMouseEvent(string Button, bool IsDown, bool IsInjected);

internal sealed class GlobalMouseHook : IDisposable
{
    private readonly NativeMethods.LowLevelMouseProc _callback;
    private IntPtr _hookHandle;

    internal GlobalMouseHook() => _callback = HookCallback;
    internal event EventHandler<GlobalMouseEvent>? ButtonChanged;

    internal void Start()
    {
        if (_hookHandle != IntPtr.Zero) return;
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLowLevel, _callback, IntPtr.Zero, 0);
        if (_hookHandle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo iniciar la escucha global del mouse.");
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && TryReadButton(wParam.ToInt32(), lParam, out var button, out var isDown, out var injected))
            ButtonChanged?.Invoke(this, new GlobalMouseEvent(button, isDown, injected));
        return NativeMethods.CallNextHookEx(_hookHandle, code, wParam, lParam);
    }

    private static bool TryReadButton(int message, IntPtr dataPointer, out string button, out bool isDown, out bool injected)
    {
        button = string.Empty;
        isDown = false;
        var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(dataPointer);
        injected = (data.Flags & NativeMethods.LowLevelMouseInjected) != 0 || data.ExtraInfo == NativeMethods.MacroFlowMarker;
        switch (message)
        {
            case NativeMethods.WmLeftButtonDown: button = "left"; isDown = true; return true;
            case NativeMethods.WmLeftButtonUp: button = "left"; return true;
            case NativeMethods.WmRightButtonDown: button = "right"; isDown = true; return true;
            case NativeMethods.WmRightButtonUp: button = "right"; return true;
            case NativeMethods.WmMiddleButtonDown: button = "middle"; isDown = true; return true;
            case NativeMethods.WmMiddleButtonUp: button = "middle"; return true;
            case NativeMethods.WmXButtonDown:
            case NativeMethods.WmXButtonUp:
                button = ((data.MouseData >> 16) & 0xFFFF) == 1 ? "xbutton1" : "xbutton2";
                isDown = message == NativeMethods.WmXButtonDown;
                return true;
            default:
                return false;
        }
    }

    public void Dispose()
    {
        if (_hookHandle == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
    }
}
