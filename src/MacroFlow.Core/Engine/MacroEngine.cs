using System.Diagnostics;
using MacroFlow.Core.Models;

namespace MacroFlow.Core.Engine;

public enum MacroEngineState
{
    Stopped,
    Running,
    Paused
}

public sealed class MacroEngine : IAsyncDisposable
{
    private readonly IInputEmitter _input;
    private readonly object _sync = new();
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;

    public MacroEngine(IInputEmitter input)
    {
        _input = input;
        Pause = new PauseController();
        Pause.StateChanged += (_, _) => UpdateState();
    }

    public PauseController Pause { get; }
    public MacroEngineState State { get; private set; } = MacroEngineState.Stopped;
    public event EventHandler? StateChanged;
    public event EventHandler<Exception>? Faulted;

    public bool Start(MacroProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var snapshot = profile.Clone();
        if (snapshot.Actions.Count == 0)
        {
            return false;
        }

        lock (_sync)
        {
            if (_runTask is { IsCompleted: false })
            {
                return false;
            }

            Pause.Clear();
            _runCancellation = new CancellationTokenSource();
            SetState(MacroEngineState.Running);
            _runTask = RunAsync(snapshot, _runCancellation.Token);
            return true;
        }
    }

    public async Task StopAsync()
    {
        Task? task;
        lock (_sync)
        {
            _runCancellation?.Cancel();
            task = _runTask;
        }

        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _input.ReleaseAllAsync().ConfigureAwait(false);
        Pause.Clear();
        SetState(MacroEngineState.Stopped);
    }

    public async Task SetPauseAsync(string reason, bool paused)
    {
        if (!Pause.SetPaused(reason, paused))
        {
            return;
        }

        if (paused)
        {
            await _input.ReleaseAllAsync().ConfigureAwait(false);
        }
    }

    public Task TogglePauseAsync(string reason) =>
        SetPauseAsync(reason, !Pause.Reasons.Contains(reason, StringComparer.OrdinalIgnoreCase));

    private async Task RunAsync(MacroProfile profile, CancellationToken cancellationToken)
    {
        try
        {
            do
            {
                foreach (var action in profile.Actions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Pause.WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
                    await ExecuteActionAsync(action, cancellationToken).ConfigureAwait(false);
                }
            }
            while (profile.Repeat && !cancellationToken.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Faulted?.Invoke(this, exception);
        }
        finally
        {
            await _input.ReleaseAllAsync().ConfigureAwait(false);
            Pause.Clear();
            SetState(MacroEngineState.Stopped);
        }
    }

    private async Task ExecuteActionAsync(MacroAction action, CancellationToken cancellationToken)
    {
        switch (action.Kind)
        {
            case MacroActionKind.KeyPress:
                await _input.KeyDownAsync(action.Value, cancellationToken).ConfigureAwait(false);
                await DelayActiveTimeAsync(Math.Max(1, action.DurationMs), cancellationToken).ConfigureAwait(false);
                await _input.KeyUpAsync(action.Value, cancellationToken).ConfigureAwait(false);
                break;
            case MacroActionKind.KeyDown:
                await _input.KeyDownAsync(action.Value, cancellationToken).ConfigureAwait(false);
                break;
            case MacroActionKind.KeyUp:
                await _input.KeyUpAsync(action.Value, cancellationToken).ConfigureAwait(false);
                break;
            case MacroActionKind.Delay:
                await DelayActiveTimeAsync(action.DurationMs, cancellationToken).ConfigureAwait(false);
                break;
            case MacroActionKind.MouseClick:
                await _input.MouseClickAsync(action.Value, cancellationToken).ConfigureAwait(false);
                if (action.DurationMs > 0)
                {
                    await DelayActiveTimeAsync(action.DurationMs, cancellationToken).ConfigureAwait(false);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action.Kind), action.Kind, "Acción desconocida.");
        }
    }

    private async Task DelayActiveTimeAsync(int milliseconds, CancellationToken cancellationToken)
    {
        var remaining = Math.Max(0, milliseconds);
        while (remaining > 0)
        {
            await Pause.WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
            var slice = Math.Min(remaining, 15);
            var stopwatch = Stopwatch.StartNew();
            await Task.Delay(slice, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            if (!Pause.IsPaused)
            {
                remaining -= Math.Max(1, (int)stopwatch.ElapsedMilliseconds);
            }
        }
    }

    private void UpdateState()
    {
        if (State == MacroEngineState.Stopped)
        {
            return;
        }

        SetState(Pause.IsPaused ? MacroEngineState.Paused : MacroEngineState.Running);
    }

    private void SetState(MacroEngineState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _runCancellation?.Dispose();
    }
}
