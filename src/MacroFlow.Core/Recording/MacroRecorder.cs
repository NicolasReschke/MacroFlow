using MacroFlow.Core.Models;

namespace MacroFlow.Core.Recording;

public sealed class MacroRecorder
{
    private readonly object _sync = new();
    private readonly List<RecordedInput> _events = [];
    private readonly HashSet<string> _keysDown = new(StringComparer.OrdinalIgnoreCase);
    private long _startedAtMs;

    public bool IsRecording { get; private set; }

    public void Start(long timestampMs)
    {
        lock (_sync)
        {
            _events.Clear();
            _keysDown.Clear();
            _startedAtMs = timestampMs;
            IsRecording = true;
        }
    }

    public void RecordKey(string token, bool isDown, long timestampMs)
    {
        lock (_sync)
        {
            if (!IsRecording || string.IsNullOrWhiteSpace(token)) return;
            if (isDown)
            {
                if (!_keysDown.Add(token)) return;
                _events.Add(new RecordedInput(RecordedInputKind.KeyDown, token, Relative(timestampMs)));
            }
            else
            {
                if (!_keysDown.Remove(token)) return;
                _events.Add(new RecordedInput(RecordedInputKind.KeyUp, token, Relative(timestampMs)));
            }
        }
    }

    public void RecordMouseClick(string button, long timestampMs)
    {
        lock (_sync)
        {
            if (!IsRecording || string.IsNullOrWhiteSpace(button)) return;
            _events.Add(new RecordedInput(RecordedInputKind.MouseClick, button, Relative(timestampMs)));
        }
    }

    public IReadOnlyList<MacroAction> Stop(long timestampMs, IEnumerable<string>? trailingActivationKeys = null)
    {
        lock (_sync)
        {
            if (!IsRecording) return [];
            IsRecording = false;

            var excluded = new HashSet<string>(trailingActivationKeys ?? [], StringComparer.OrdinalIgnoreCase);
            while (_events.Count > 0 && _events[^1] is { Kind: RecordedInputKind.KeyDown } last && excluded.Contains(last.Value))
            {
                _events.RemoveAt(_events.Count - 1);
                _keysDown.Remove(last.Value);
            }

            var stopTime = Relative(timestampMs);
            foreach (var key in _keysDown.ToArray())
                _events.Add(new RecordedInput(RecordedInputKind.KeyUp, key, stopTime));
            _keysDown.Clear();

            return BuildActions(_events);
        }
    }

    internal static IReadOnlyList<MacroAction> BuildActions(IEnumerable<RecordedInput> inputs)
    {
        var ordered = inputs.OrderBy(input => input.TimestampMs).ToList();
        if (ordered.Count == 0) return [];

        var actions = new List<MacroAction>();
        var previousTimestamp = ordered[0].TimestampMs;
        foreach (var input in ordered)
        {
            var delay = Math.Clamp(input.TimestampMs - previousTimestamp, 0, 60_000);
            if (delay >= 5)
                actions.Add(new MacroAction { Kind = MacroActionKind.Delay, DurationMs = (int)delay, Value = string.Empty });

            actions.Add(input.Kind switch
            {
                RecordedInputKind.KeyDown => new MacroAction { Kind = MacroActionKind.KeyDown, Value = input.Value, DurationMs = 0 },
                RecordedInputKind.KeyUp => new MacroAction { Kind = MacroActionKind.KeyUp, Value = input.Value, DurationMs = 0 },
                RecordedInputKind.MouseClick => new MacroAction { Kind = MacroActionKind.MouseClick, Value = input.Value, DurationMs = 0 },
                _ => throw new ArgumentOutOfRangeException()
            });
            previousTimestamp = input.TimestampMs;
        }

        CompactSimplePresses(actions);
        return actions;
    }

    private static void CompactSimplePresses(List<MacroAction> actions)
    {
        for (var index = 0; index + 2 < actions.Count; index++)
        {
            var down = actions[index];
            var delay = actions[index + 1];
            var up = actions[index + 2];
            if (down.Kind != MacroActionKind.KeyDown || delay.Kind != MacroActionKind.Delay ||
                up.Kind != MacroActionKind.KeyUp || !string.Equals(down.Value, up.Value, StringComparison.OrdinalIgnoreCase)) continue;

            actions[index] = new MacroAction
            {
                Kind = MacroActionKind.KeyPress,
                Value = down.Value,
                DurationMs = Math.Max(1, delay.DurationMs)
            };
            actions.RemoveRange(index + 1, 2);
        }
    }

    private long Relative(long timestampMs) => Math.Max(0, timestampMs - _startedAtMs);
}
