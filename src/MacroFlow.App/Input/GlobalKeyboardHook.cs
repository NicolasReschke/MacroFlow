using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MacroFlow.App.Input;

internal sealed record GlobalKeyEvent(int VirtualKey, bool IsDown, bool IsInjected);

internal sealed class GlobalKeyboardHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private IntPtr _hookHandle;

    internal GlobalKeyboardHook() => _callback = HookCallback;

    internal event EventHandler<GlobalKeyEvent>? KeyChanged;

    internal void Start()
    {
        if (_hookHandle != IntPtr.Zero) return;
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLowLevel, _callback, IntPtr.Zero, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo iniciar la escucha global del teclado.");
        }
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = wParam.ToInt32();
            var isDown = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
            var isUp = message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;
            if (isDown || isUp)
            {
                var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                var injected = (data.Flags & NativeMethods.LowLevelInjected) != 0 || data.ExtraInfo == NativeMethods.MacroFlowMarker;
                KeyChanged?.Invoke(this, new GlobalKeyEvent((int)data.VirtualKeyCode, isDown, injected));
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookHandle == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
    }
}
