using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MacroFlow.Core.Models;

public enum MacroActionKind
{
    KeyPress,
    KeyDown,
    KeyUp,
    Delay,
    MouseClick
}

public sealed class MacroAction : INotifyPropertyChanged
{
    private MacroActionKind _kind = MacroActionKind.KeyPress;
    private string _value = "1";
    private int _durationMs = 75;

    public MacroActionKind Kind
    {
        get => _kind;
        set => SetField(ref _kind, value);
    }

    public string Value
    {
        get => _value;
        set => SetField(ref _value, value?.Trim() ?? string.Empty);
    }

    public int DurationMs
    {
        get => _durationMs;
        set => SetField(ref _durationMs, Math.Clamp(value, 0, 60_000));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
