namespace MacroFlow.App;

public sealed class AddActionPlaceholder
{
    private AddActionPlaceholder()
    {
    }

    public static AddActionPlaceholder Instance { get; } = new();
    public bool IsAdd => true;
}
