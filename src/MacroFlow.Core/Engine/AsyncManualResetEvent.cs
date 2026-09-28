namespace MacroFlow.Core.Engine;

internal sealed class AsyncManualResetEvent(bool initialState)
{
    private volatile TaskCompletionSource _source = CreateSource();

    public Task WaitAsync(CancellationToken cancellationToken) =>
        _source.Task.WaitAsync(cancellationToken);

    public void Set() => _source.TrySetResult();

    public void Reset()
    {
        while (true)
        {
            var source = _source;
            if (!source.Task.IsCompleted)
            {
                return;
            }

            var replacement = CreateSource();
            if (ReferenceEquals(Interlocked.CompareExchange(ref _source, replacement, source), source))
            {
                return;
            }
        }
    }

    private static TaskCompletionSource CreateSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Initialize()
    {
        if (initialState)
        {
            Set();
        }
    }
}
