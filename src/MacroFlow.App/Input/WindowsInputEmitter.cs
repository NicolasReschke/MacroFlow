using System.ComponentModel;
using System.Runtime.InteropServices;
using MacroFlow.Core.Engine;

namespace MacroFlow.App.Input;

internal sealed class WindowsInputEmitter : IInputEmitter
{
    private readonly object _sync = new();
    private readonly HashSet<ushort> _pressedKeys = [];

    public Task KeyDownAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var virtualKey = ParseKey(key);
        SendKeyboard(virtualKey, keyUp: false);
        lock (_sync) _pressedKeys.Add(virtualKey);
        return Task.CompletedTask;
    }

    public Task KeyUpAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var virtualKey = ParseKey(key);
        SendKeyboard(virtualKey, keyUp: true);
        lock (_sync) _pressedKeys.Remove(virtualKey);
        return Task.CompletedTask;
    }

    public Task MouseClickAsync(string button, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (down, up) = button.Trim().ToLowerInvariant() switch
        {
            "left" or "izquierdo" => (NativeMethods.MouseLeftDown, NativeMethods.MouseLeftUp),
            "right" or "derecho" => (NativeMethods.MouseRightDown, NativeMethods.MouseRightUp),
            "middle" or "medio" => (NativeMethods.MouseMiddleDown, NativeMethods.MouseMiddleUp),
            _ => throw new ArgumentException($"Botón de mouse no reconocido: {button}", nameof(button))
        };

        SendMouse(down);
        SendMouse(up);
        return Task.CompletedTask;
    }

    public Task ReleaseAllAsync()
    {
        ushort[] keys;
        lock (_sync)
        {
            keys = _pressedKeys.ToArray();
            _pressedKeys.Clear();
        }

        foreach (var key in keys) SendKeyboard(key, keyUp: true);
        return Task.CompletedTask;
    }

    private static ushort ParseKey(string key)
    {
        if (!KeyMap.TryParse(key, out var virtualKey))
            throw new ArgumentException($"Tecla no reconocida: {key}", nameof(key));
        return checked((ushort)virtualKey);
    }

    private static void SendKeyboard(ushort virtualKey, bool keyUp)
    {
        var inputs = new[]
        {
            new NativeMethods.INPUT
            {
                Type = NativeMethods.InputKeyboard,
                Data = new NativeMethods.InputUnion
                {
                    Keyboard = new NativeMethods.KEYBDINPUT
                    {
                        VirtualKey = virtualKey,
                        Flags = keyUp ? NativeMethods.KeyboardEventKeyUp : 0,
                        ExtraInfo = NativeMethods.MacroFlowMarker
                    }
                }
            }
        };
        Send(inputs);
    }

    private static void SendMouse(uint flags)
    {
        var inputs = new[]
        {
            new NativeMethods.INPUT
            {
                Type = NativeMethods.InputMouse,
                Data = new NativeMethods.InputUnion
                {
                    Mouse = new NativeMethods.MOUSEINPUT { Flags = flags, ExtraInfo = NativeMethods.MacroFlowMarker }
                }
            }
        };
        Send(inputs);
    }

    private static void Send(NativeMethods.INPUT[] inputs)
    {
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows no pudo enviar la entrada solicitada.");
    }
}
