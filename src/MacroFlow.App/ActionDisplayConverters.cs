using System.Globalization;
using System.Windows.Data;
using MacroFlow.App.Input;
using MacroFlow.Core.Models;

namespace MacroFlow.App;

public sealed class ActionKindLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        MacroActionKind.KeyPress => "Pulsar y soltar tecla",
        MacroActionKind.KeyDown => "Mantener tecla presionada",
        MacroActionKind.KeyUp => "Soltar tecla",
        MacroActionKind.Delay => "Esperar",
        MacroActionKind.MouseClick => "Clic del mouse",
        _ => "Acción desconocida"
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ActionValueLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not MacroAction action) return string.Empty;
        return action.Kind switch
        {
            MacroActionKind.Delay => "ESPERAR",
            MacroActionKind.MouseClick => action.Value.ToLowerInvariant() switch
            {
                "left" or "izquierdo" => "Botón izquierdo",
                "right" or "derecho" => "Botón derecho",
                "middle" or "medio" => "Botón central",
                _ => action.Value
            },
            _ => KeyMap.GetDisplayName(action.Value)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
