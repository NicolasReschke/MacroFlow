namespace MacroFlow.Core.Engine;

public sealed class PauseController
{
    private readonly object _sync = new();
    private readonly HashSet<string> _reasons = new(StringComparer.OrdinalIgnoreCase);
    private readonly AsyncManualResetEvent _resumeGate = new(initialState: true);

    public PauseController()
    {
        _resumeGate.Initialize();
    }

    public event EventHandler? StateChanged;

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                return _reasons.Count > 0;
            }
        }
    }

    public IReadOnlyList<string> Reasons
    {
        get
        {
            lock (_sync)
            {
                return _reasons.Order(StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }
    }

    public bool SetPaused(string reason, bool paused)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        bool changed;
        bool isPaused;

        lock (_sync)
        {
            changed = paused ? _reasons.Add(reason) : _reasons.Remove(reason);
            isPaused = _reasons.Count > 0;
        }

        if (!changed)
        {
            return false;
        }

        if (isPaused)
        {
            _resumeGate.Reset();
        }
        else
        {
            _resumeGate.Set();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Clear()
    {
        bool changed;
        lock (_sync)
        {
            changed = _reasons.Count > 0;
            _reasons.Clear();
        }

        _resumeGate.Set();
        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task WaitWhilePausedAsync(CancellationToken cancellationToken) =>
        _resumeGate.WaitAsync(cancellationToken);
}
