using System.Drawing;
using System.Windows;

namespace MacroFlow.App;

internal static class AppIcon
{
    internal static Icon LoadForTray()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/app-icon.ico", UriKind.Absolute))
            ?? throw new InvalidOperationException("No se encontró el icono integrado de MacroFlow.");
        using (resource.Stream)
        using (var icon = new Icon(resource.Stream))
            return (Icon)icon.Clone();
    }
}
