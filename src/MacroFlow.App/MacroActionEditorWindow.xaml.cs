using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MacroFlow.App.Input;
using MacroFlow.Core.Models;

namespace MacroFlow.App;

public partial class MacroActionEditorWindow : Window
{
    private bool _captureActive;
    private string _keyToken = "1";

    public MacroActionEditorWindow(MacroAction? action = null)
    {
        InitializeComponent();
        SetKey("1");

        if (action is null) return;
        HeadingText.Text = "Editar acción";
        SaveButton.Content = "Guardar cambios";
        SelectKind(action.Kind);
        DurationBox.Text = action.DurationMs.ToString();
        if (action.Kind is MacroActionKind.KeyPress or MacroActionKind.KeyDown or MacroActionKind.KeyUp)
            SetKey(action.Value);
        if (action.Kind == MacroActionKind.MouseClick) SelectMouse(action.Value);
    }

    public MacroAction? ResultAction { get; private set; }

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        _captureActive = !_captureActive;
        CaptureNotice.Visibility = _captureActive ? Visibility.Visible : Visibility.Collapsed;
        CaptureButton.Content = _captureActive ? "Cancelar captura" : "Capturar tecla";
        if (_captureActive) Focus();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_captureActive) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey <= 0) return;
        SetKey(KeyMap.ToToken(virtualKey));
        _captureActive = false;
        CaptureNotice.Visibility = Visibility.Collapsed;
        CaptureButton.Content = "Capturar tecla";
        e.Handled = true;
    }

    private void VirtualKeyboard_Click(object sender, RoutedEventArgs e)
    {
        _captureActive = false;
        CaptureNotice.Visibility = Visibility.Collapsed;
        CaptureButton.Content = "Capturar tecla";
        var keyboard = new VirtualKeyboardWindow("Elegí la tecla para esta acción.") { Owner = this };
        if (keyboard.ShowDialog() == true && keyboard.SelectedToken is { } token) SetKey(token);
    }

    private void KindCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KeyPanel is null) return;
        var kind = SelectedKind();
        var isKey = kind is MacroActionKind.KeyPress or MacroActionKind.KeyDown or MacroActionKind.KeyUp;
        KeyPanel.Visibility = isKey ? Visibility.Visible : Visibility.Collapsed;
        MousePanel.Visibility = kind == MacroActionKind.MouseClick ? Visibility.Visible : Visibility.Collapsed;
        NoValueText.Visibility = kind == MacroActionKind.Delay ? Visibility.Visible : Visibility.Collapsed;
        DurationLabel.Content = kind switch
        {
            MacroActionKind.KeyPress => "Tiempo presionada (ms)",
            MacroActionKind.Delay => "Tiempo de espera (ms)",
            _ => "Espera posterior (ms)"
        };
        HelpText.Text = kind switch
        {
            MacroActionKind.KeyPress => "La tecla se presiona y se suelta automáticamente.",
            MacroActionKind.KeyDown => "Mantiene la tecla hasta una acción posterior de soltar.",
            MacroActionKind.KeyUp => "Libera una tecla que dejaste mantenida.",
            MacroActionKind.Delay => "La secuencia espera este tiempo antes de continuar.",
            MacroActionKind.MouseClick => "Hace un clic y luego continúa.",
            _ => string.Empty
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var kind = SelectedKind();
        if (kind is null || !int.TryParse(DurationBox.Text, out var duration) || duration is < 0 or > 60_000)
        {
            System.Windows.MessageBox.Show(this, "Usá un tiempo entre 0 y 60000 milisegundos.", "MacroFlow",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var value = kind == MacroActionKind.MouseClick
            ? (MouseCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "left"
            : kind == MacroActionKind.Delay ? string.Empty : _keyToken;
        ResultAction = new MacroAction { Kind = kind.Value, Value = value, DurationMs = duration };
        DialogResult = true;
    }

    private MacroActionKind? SelectedKind()
    {
        if (KindCombo.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<MacroActionKind>(item.Tag?.ToString(), out var kind)) return kind;
        return null;
    }

    private void SelectKind(MacroActionKind kind)
    {
        KindCombo.SelectedItem = KindCombo.Items.OfType<ComboBoxItem>()
            .First(item => string.Equals(item.Tag?.ToString(), kind.ToString(), StringComparison.Ordinal));
    }

    private void SelectMouse(string value)
    {
        MouseCombo.SelectedItem = MouseCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            ?? MouseCombo.Items[0];
    }

    private void SetKey(string token)
    {
        _keyToken = token;
        KeyBox.Text = KeyMap.GetDisplayName(token);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
