namespace MacroFlow.Core.Models;

public sealed class MacroProfile
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "Throne and Liberty";
    public bool Repeat { get; set; } = true;
    public string ToggleHotkey { get; set; } = "F6";
    public string EmergencyStopKey { get; set; } = "F12";
    public string HoldPauseKey { get; set; } = "Q";
    public string MorphToggleKey { get; set; } = "M";
    public string RecorderHotkey { get; set; } = "CTRL+F8";
    public int ResumeDelayMs { get; set; } = 150;
    public bool RestrictToTargetProcess { get; set; }
    public string TargetProcessName { get; set; } = "TL";
    public List<MacroAction> Actions { get; set; } =
    [
        new() { Kind = MacroActionKind.KeyPress, Value = "1", DurationMs = 75 },
        new() { Kind = MacroActionKind.Delay, Value = string.Empty, DurationMs = 125 },
        new() { Kind = MacroActionKind.KeyPress, Value = "2", DurationMs = 75 }
    ];

    public MacroProfile Clone() => new()
    {
        FormatVersion = FormatVersion,
        Name = Name,
        Repeat = Repeat,
        ToggleHotkey = ToggleHotkey,
        EmergencyStopKey = EmergencyStopKey,
        HoldPauseKey = HoldPauseKey,
        MorphToggleKey = MorphToggleKey,
        RecorderHotkey = RecorderHotkey,
        ResumeDelayMs = ResumeDelayMs,
        RestrictToTargetProcess = RestrictToTargetProcess,
        TargetProcessName = TargetProcessName,
        Actions = Actions.Select(action => new MacroAction
        {
            Kind = action.Kind,
            Value = action.Value,
            DurationMs = action.DurationMs
        }).ToList()
    };
}
