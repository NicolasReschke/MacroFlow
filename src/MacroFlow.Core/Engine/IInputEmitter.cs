namespace MacroFlow.Core.Engine;

public interface IInputEmitter
{
    Task KeyDownAsync(string key, CancellationToken cancellationToken);
    Task KeyUpAsync(string key, CancellationToken cancellationToken);
    Task MouseClickAsync(string button, CancellationToken cancellationToken);
    Task ReleaseAllAsync();
}
