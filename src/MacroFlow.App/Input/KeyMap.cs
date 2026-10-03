using System.Text;

namespace MacroFlow.App.Input;

internal static class KeyMap
{
    private static readonly Dictionary<string, int> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SPACE"] = 0x20, ["ESPACIO"] = 0x20, ["TAB"] = 0x09,
        ["ENTER"] = 0x0D, ["RETURN"] = 0x0D, ["INTRO"] = 0x0D,
        ["ESC"] = 0x1B, ["ESCAPE"] = 0x1B,
        ["SHIFT"] = 0x10, ["MAYUS"] = 0x10, ["MAYÚS"] = 0x10,
        ["CTRL"] = 0x11, ["CONTROL"] = 0x11, ["ALT"] = 0x12,
        ["LSHIFT"] = 0xA0, ["RSHIFT"] = 0xA1,
        ["LCTRL"] = 0xA2, ["RCTRL"] = 0xA3,
        ["LALT"] = 0xA4, ["RALT"] = 0xA5, ["ALTGR"] = 0xA5,
        ["BACKSPACE"] = 0x08, ["RETROCESO"] = 0x08,
        ["DELETE"] = 0x2E, ["SUPR"] = 0x2E, ["INSERT"] = 0x2D,
        ["HOME"] = 0x24, ["INICIO"] = 0x24, ["END"] = 0x23, ["FIN"] = 0x23,
        ["PAGEUP"] = 0x21, ["PAGEDOWN"] = 0x22,
        ["UP"] = 0x26, ["ARRIBA"] = 0x26, ["DOWN"] = 0x28, ["ABAJO"] = 0x28,
        ["LEFT"] = 0x25, ["IZQUIERDA"] = 0x25, ["RIGHT"] = 0x27, ["DERECHA"] = 0x27,
        ["CAPSLOCK"] = 0x14, ["BLOQMAYUS"] = 0x14, ["NUMLOCK"] = 0x90,
        ["PRINTSCREEN"] = 0x2C, ["PAUSE"] = 0x13,
        ["OEM1"] = 0xBA, ["OEMPLUS"] = 0xBB, ["OEMCOMMA"] = 0xBC,
        ["OEMMINUS"] = 0xBD, ["OEMPERIOD"] = 0xBE, ["OEM2"] = 0xBF,
        ["OEM3"] = 0xC0, ["TILDE"] = 0xC0, ["OEM4"] = 0xDB,
        ["OEM5"] = 0xDC, ["OEM6"] = 0xDD, ["OEM7"] = 0xDE, ["OEM102"] = 0xE2
    };

    private static readonly Dictionary<int, string> Tokens = new()
    {
        [0x08] = "BACKSPACE", [0x09] = "TAB", [0x0D] = "ENTER", [0x1B] = "ESCAPE",
        [0x10] = "SHIFT", [0x11] = "CTRL", [0x12] = "ALT",
        [0x20] = "SPACE", [0x21] = "PAGEUP", [0x22] = "PAGEDOWN", [0x23] = "END",
        [0x24] = "HOME", [0x25] = "LEFT", [0x26] = "UP", [0x27] = "RIGHT", [0x28] = "DOWN",
        [0x2C] = "PRINTSCREEN", [0x2D] = "INSERT", [0x2E] = "DELETE",
        [0xA0] = "LSHIFT", [0xA1] = "RSHIFT", [0xA2] = "LCTRL", [0xA3] = "RCTRL",
        [0xA4] = "LALT", [0xA5] = "RALT"
    };

    private static readonly Dictionary<int, string> FriendlyNames = new()
    {
        [0x08] = "Retroceso", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pausa",
        [0x10] = "Shift", [0x11] = "Ctrl", [0x12] = "Alt",
        [0x14] = "Bloq Mayús", [0x1B] = "Escape", [0x20] = "Espacio",
        [0x21] = "Re Pág", [0x22] = "Av Pág", [0x23] = "Fin", [0x24] = "Inicio",
        [0x25] = "Flecha izquierda", [0x26] = "Flecha arriba", [0x27] = "Flecha derecha",
        [0x28] = "Flecha abajo", [0x2C] = "Impr Pant", [0x2D] = "Insertar", [0x2E] = "Suprimir",
        [0x90] = "Bloq Num", [0xA0] = "Shift izquierdo", [0xA1] = "Shift derecho",
        [0xA2] = "Ctrl izquierdo", [0xA3] = "Ctrl derecho",
        [0xA4] = "Alt izquierdo", [0xA5] = "AltGr / Alt derecho"
    };

    internal static bool TryParse(string? name, out int virtualKey)
    {
        virtualKey = 0;
        var normalized = name?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalized)) return false;

        if (normalized.StartsWith("VK_", StringComparison.Ordinal) &&
            int.TryParse(normalized.AsSpan(3), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var raw) && raw is > 0 and <= 0xFF)
        {
            virtualKey = raw;
            return true;
        }

        if (normalized.Length == 1)
        {
            var character = normalized[0];
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = character;
                return true;
            }
        }

        if (normalized.StartsWith('F') && int.TryParse(normalized[1..], out var number) && number is >= 1 and <= 24)
        {
            virtualKey = 0x70 + number - 1;
            return true;
        }

        return Aliases.TryGetValue(normalized, out virtualKey);
    }

    internal static string ToToken(int virtualKey)
    {
        if (virtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
            return ((char)virtualKey).ToString();
        if (virtualKey is >= 0x70 and <= 0x87)
            return $"F{virtualKey - 0x70 + 1}";
        return Tokens.TryGetValue(virtualKey, out var token) ? token : $"VK_{virtualKey:X2}";
    }

    internal static string GetDisplayName(string? token)
    {
        if (!TryParse(token, out var virtualKey)) return token ?? string.Empty;
        if (FriendlyNames.TryGetValue(virtualKey, out var friendly)) return friendly;
        if (virtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
            return ((char)virtualKey).ToString();
        if (virtualKey is >= 0x70 and <= 0x87)
            return $"F{virtualKey - 0x70 + 1}";

        var scanCode = NativeMethods.MapVirtualKey((uint)virtualKey, 0);
        var extended = virtualKey is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28
            or 0x2D or 0x2E or 0x5B or 0x5C or 0x6F or 0x90 or 0xA3 or 0xA5;
        var lParam = (int)(scanCode << 16) | (extended ? 1 << 24 : 0);
        var buffer = new StringBuilder(64);
        return NativeMethods.GetKeyNameText(lParam, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : ToToken(virtualKey);
    }

    internal static bool Matches(int physicalVirtualKey, string? configuredToken)
    {
        if (!TryParse(configuredToken, out var configured)) return false;
        if (configured == physicalVirtualKey) return true;
        return configured switch
        {
            0x10 => physicalVirtualKey is 0xA0 or 0xA1,
            0x11 => physicalVirtualKey is 0xA2 or 0xA3,
            0x12 => physicalVirtualKey is 0xA4 or 0xA5,
            _ => false
        };
    }
}
