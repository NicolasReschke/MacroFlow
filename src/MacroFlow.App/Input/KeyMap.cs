namespace MacroFlow.App.Input;

internal static class KeyMap
{
    private static readonly Dictionary<string, int> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SPACE"] = 0x20, ["TAB"] = 0x09, ["ENTER"] = 0x0D, ["RETURN"] = 0x0D,
        ["ESC"] = 0x1B, ["ESCAPE"] = 0x1B, ["SHIFT"] = 0x10, ["CTRL"] = 0x11,
        ["CONTROL"] = 0x11, ["ALT"] = 0x12, ["BACKSPACE"] = 0x08, ["DELETE"] = 0x2E,
        ["INSERT"] = 0x2D, ["HOME"] = 0x24, ["END"] = 0x23, ["PAGEUP"] = 0x21,
        ["PAGEDOWN"] = 0x22, ["UP"] = 0x26, ["DOWN"] = 0x28, ["LEFT"] = 0x25,
        ["RIGHT"] = 0x27, ["OEM3"] = 0xC0, ["TILDE"] = 0xC0
    };

    internal static bool TryParse(string? name, out int virtualKey)
    {
        virtualKey = 0;
        var normalized = name?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalized)) return false;

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
}
