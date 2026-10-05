namespace MacroFlow.App.Input;

internal static class HotkeyGesture
{
    internal static bool TryParse(string? gesture, out string[] tokens)
    {
        tokens = (gesture ?? string.Empty).Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 0 && tokens.All(token => KeyMap.TryParse(token, out _));
    }

    internal static bool Matches(int triggerVirtualKey, IReadOnlySet<int> pressedKeys, string? gesture)
    {
        if (!TryParse(gesture, out var tokens) || !KeyMap.Matches(triggerVirtualKey, tokens[^1])) return false;
        return tokens[..^1].All(token => ModifierPressed(token, pressedKeys));
    }

    internal static string FromPressedKeys(int triggerVirtualKey, IReadOnlySet<int> pressedKeys)
    {
        var tokens = new List<string>();
        if (pressedKeys.Any(key => key is 0x11 or 0xA2 or 0xA3)) tokens.Add("CTRL");
        if (pressedKeys.Any(key => key is 0x10 or 0xA0 or 0xA1)) tokens.Add("SHIFT");
        if (pressedKeys.Any(key => key is 0x12 or 0xA4 or 0xA5)) tokens.Add("ALT");
        tokens.Add(KeyMap.ToToken(triggerVirtualKey));
        return string.Join('+', tokens.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    internal static string GetDisplayName(string gesture)
    {
        if (!TryParse(gesture, out var tokens)) return gesture;
        return string.Join(" + ", tokens.Select(KeyMap.GetDisplayName));
    }

    internal static IReadOnlyList<string> Tokens(string gesture) =>
        TryParse(gesture, out var tokens) ? tokens : [];

    internal static bool IsModifier(int virtualKey) => virtualKey is 0x10 or 0x11 or 0x12 or >= 0xA0 and <= 0xA5;

    private static bool ModifierPressed(string token, IReadOnlySet<int> pressedKeys)
    {
        if (!KeyMap.TryParse(token, out var key)) return false;
        return key switch
        {
            0x10 => pressedKeys.Any(value => value is 0x10 or 0xA0 or 0xA1),
            0x11 => pressedKeys.Any(value => value is 0x11 or 0xA2 or 0xA3),
            0x12 => pressedKeys.Any(value => value is 0x12 or 0xA4 or 0xA5),
            _ => pressedKeys.Contains(key)
        };
    }
}
