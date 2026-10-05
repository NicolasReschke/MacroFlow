namespace MacroFlow.Core.Recording;

public enum RecordedInputKind
{
    KeyDown,
    KeyUp,
    MouseClick
}

public sealed record RecordedInput(RecordedInputKind Kind, string Value, long TimestampMs);
