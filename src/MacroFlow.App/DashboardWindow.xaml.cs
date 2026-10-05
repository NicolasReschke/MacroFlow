using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MacroFlow.App.Input;
using MacroFlow.Core.Models;
using MacroFlow.Core.Persistence;

namespace MacroFlow.App;

public partial class DashboardWindow : Window
{
    private sealed record ProfileListItem(string Name, string Subtitle, bool IsDefault = false);
    private enum CalloutSide { Top, Right, Bottom, Left }

    private readonly ObservableCollection<ProfileListItem> _profiles = [];
    private readonly ProfileStore _profileStore;
    private readonly Dictionary<string, Border> _keyVisuals = new(StringComparer.OrdinalIgnoreCase);
    private MacroProfile _selectedProfile = new();
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private MainWindow? _activeEditor;

    public DashboardWindow()
    {
        InitializeComponent();
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var profileDirectory = Path.Combine(documents, "MacroFlow", "Macros");
        _profileStore = new ProfileStore(profileDirectory);
        MigrateLegacyProfiles(Path.Combine(AppContext.BaseDirectory, "profiles"), profileDirectory);
        ProfilesList.ItemsSource = _profiles;
        Loaded += (_, _) =>
        {
            CreateTrayIcon();
            ReloadProfiles();
        };
        StateChanged += DashboardWindow_StateChanged;
    }

    private void ReloadProfiles(string? preferredName = null)
    {
        _profiles.Clear();
        var names = _profileStore.ListProfiles();
        if (names.Count == 0)
        {
            _profiles.Add(new ProfileListItem("Configuración predeterminada", "1 → espera → 2 · lista para editar", true));
        }
        else
        {
            foreach (var name in names)
                _profiles.Add(new ProfileListItem(name, "Perfil guardado · doble clic para abrir"));
        }

        ProfilesList.SelectedItem = _profiles.FirstOrDefault(item =>
            string.Equals(item.Name, preferredName, StringComparison.OrdinalIgnoreCase)) ?? _profiles.FirstOrDefault();
    }

    private async void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not ProfileListItem item) return;
        try
        {
            _selectedProfile = item.IsDefault ? new MacroProfile() : await _profileStore.LoadAsync(item.Name);
            RenderProfile(_selectedProfile, item.IsDefault);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"No se pudo leer el perfil: {exception.Message}", "MacroFlow",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RenderProfile(MacroProfile profile, bool isDefault)
    {
        var actionCount = profile.Actions.Count(action => action.Kind != MacroActionKind.Delay);
        var totalMs = profile.Actions.Sum(action => Math.Max(0, action.DurationMs));
        SelectedProfileName.Text = isDefault ? "Configuración predeterminada" : profile.Name;
        SelectedProfileSummary.Text = $"{actionCount} entradas · {totalMs:N0} ms por ciclo · proceso: {profile.TargetProcessName}";
        RepeatBadge.Text = profile.Repeat ? "↻ REPETICIÓN ACTIVA" : "→ UNA EJECUCIÓN";

        var actionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in profile.Actions.Where(action =>
                     action.Kind is MacroActionKind.KeyPress or MacroActionKind.KeyDown or MacroActionKind.KeyUp))
            AddExpandedToken(actionKeys, action.Value);

        var controlKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in new[]
                 {
                     profile.ToggleHotkey, profile.EmergencyStopKey, profile.HoldPauseKey, profile.MorphToggleKey
                 })
            AddExpandedToken(controlKeys, token);
        foreach (var token in HotkeyGesture.Tokens(profile.RecorderHotkey)) AddExpandedToken(controlKeys, token);

        BuildKeyboard(actionKeys, controlKeys);
        Dispatcher.BeginInvoke(new Action(() => RenderControlCallouts(profile)),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void BuildKeyboard(HashSet<string> actionKeys, HashSet<string> controlKeys)
    {
        _keyVisuals.Clear();
        AddKeyboardRow(FunctionKeysRow, ["ESCAPE", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"], actionKeys, controlKeys);
        AddKeyboardRow(NumberKeysRow, ["VK_C0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "VK_BD", "VK_BB", "BACKSPACE"], actionKeys, controlKeys);
        AddKeyboardRow(TopKeysRow, ["TAB", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "VK_DB", "VK_DD", "VK_DC"], actionKeys, controlKeys);
        AddKeyboardRow(HomeKeysRow, ["CAPSLOCK", "A", "S", "D", "F", "G", "H", "J", "K", "L", "VK_BA", "VK_DE", "ENTER"], actionKeys, controlKeys);
        AddKeyboardRow(BottomKeysRow, ["LSHIFT", "Z", "X", "C", "V", "B", "N", "M", "VK_BC", "VK_BE", "VK_BF", "RSHIFT"], actionKeys, controlKeys);
        AddKeyboardRow(ModifierKeysRow, ["LCTRL", "LALT", "SPACE", "RALT", "RCTRL", "LEFT", "UP", "DOWN", "RIGHT"], actionKeys, controlKeys);
    }

    private void AddKeyboardRow(System.Windows.Controls.Panel panel, IEnumerable<string> tokens,
        HashSet<string> actionKeys, HashSet<string> controlKeys)
    {
        panel.Children.Clear();
        foreach (var token in tokens)
        {
            var isAction = actionKeys.Contains(token);
            var isControl = controlKeys.Contains(token);
            var label = KeyMap.GetDisplayName(token);
            var width = token switch
            {
                "SPACE" => 250,
                "BACKSPACE" or "CAPSLOCK" or "ENTER" => 92,
                "LSHIFT" or "RSHIFT" => 112,
                "TAB" or "LCTRL" or "RCTRL" or "LALT" or "RALT" => 76,
                _ => 56
            };
            var border = new Border
            {
                Width = width,
                Height = 44,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(isAction || isControl ? 1.5 : 1),
                BorderBrush = new SolidColorBrush(isAction || isControl
                    ? System.Windows.Media.Color.FromRgb(0x42, 0xD6, 0xC8)
                    : System.Windows.Media.Color.FromRgb(0x32, 0x3D, 0x4C)),
                Background = new SolidColorBrush(isAction
                    ? System.Windows.Media.Color.FromRgb(0x11, 0x6D, 0x68)
                    : isControl ? System.Windows.Media.Color.FromRgb(0x17, 0x30, 0x34) : System.Windows.Media.Color.FromRgb(0x17, 0x1D, 0x27)),
                ToolTip = isAction ? "Usada por la secuencia" : isControl ? "Tecla de control" : null,
                Child = new TextBlock
                {
                    Text = label,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    FontSize = label.Length > 10 ? 9 : 11,
                    FontWeight = isAction || isControl ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = new SolidColorBrush(isAction || isControl
                        ? System.Windows.Media.Color.FromRgb(0xF4, 0xFF, 0xFD)
                        : System.Windows.Media.Color.FromRgb(0x9E, 0xAB, 0xBD)),
                    TextWrapping = TextWrapping.Wrap
                }
            };
            panel.Children.Add(border);
            _keyVisuals[token] = border;
        }
    }

    private void RenderControlCallouts(MacroProfile profile)
    {
        ControlCalloutCanvas.Children.Clear();
        if (ControlCalloutCanvas.ActualWidth <= 0 || ControlCalloutCanvas.ActualHeight <= 0) return;

        var recorderTokens = HotkeyGesture.Tokens(profile.RecorderHotkey);
        var callouts = new[]
        {
            (Token: profile.ToggleHotkey, Label: "INICIAR / DETENER", Value: KeyMap.GetDisplayName(profile.ToggleHotkey)),
            (Token: profile.EmergencyStopKey, Label: "PARADA DE EMERGENCIA", Value: KeyMap.GetDisplayName(profile.EmergencyStopKey)),
            (Token: profile.HoldPauseKey, Label: "PARRY", Value: KeyMap.GetDisplayName(profile.HoldPauseKey)),
            (Token: profile.MorphToggleKey, Label: "MORPH", Value: KeyMap.GetDisplayName(profile.MorphToggleKey)),
            (Token: recorderTokens.LastOrDefault() ?? profile.RecorderHotkey, Label: "GRABACIÓN", Value: HotkeyGesture.GetDisplayName(profile.RecorderHotkey))
        };

        var keyBounds = _keyVisuals.Values.Where(key => key.IsVisible && key.ActualWidth > 0).Select(key =>
        {
            var topLeft = key.TranslatePoint(new System.Windows.Point(), ControlCalloutCanvas);
            var bottomRight = key.TranslatePoint(new System.Windows.Point(key.ActualWidth, key.ActualHeight), ControlCalloutCanvas);
            return new System.Windows.Rect(topLeft, bottomRight);
        }).ToArray();
        if (keyBounds.Length == 0) return;
        var keyboardBounds = keyBounds.Aggregate(System.Windows.Rect.Union);

        var located = callouts.Select(callout =>
        {
            var visualToken = ResolveVisualToken(callout.Token);
            if (visualToken is null || !_keyVisuals.TryGetValue(visualToken, out var key))
                return (Callout: callout, Key: (Border?)null, Bounds: new System.Windows.Rect(), Side: CalloutSide.Top);
            var topLeft = key.TranslatePoint(new System.Windows.Point(), ControlCalloutCanvas);
            var bottomRight = key.TranslatePoint(new System.Windows.Point(key.ActualWidth, key.ActualHeight), ControlCalloutCanvas);
            var bounds = new System.Windows.Rect(topLeft, bottomRight);
            var side = bounds.Top + bounds.Height / 2 < keyboardBounds.Top + keyboardBounds.Height * 0.2
                ? CalloutSide.Top
                : bounds.Bottom > keyboardBounds.Bottom - keyboardBounds.Height * 0.27
                    ? CalloutSide.Bottom
                    : bounds.Left + bounds.Width / 2 < keyboardBounds.Left + keyboardBounds.Width / 2
                        ? CalloutSide.Left
                        : CalloutSide.Right;
            return (Callout: callout, Key: (Border?)key, Bounds: bounds, Side: side);
        }).Where(item => item.Key is not null).ToArray();

        if (located.Length == 0) return;
        var sideIndexes = new Dictionary<CalloutSide, int>();
        foreach (var item in located.OrderBy(item => item.Side is CalloutSide.Top or CalloutSide.Bottom
                     ? item.Bounds.Left : item.Bounds.Top))
        {
            sideIndexes.TryGetValue(item.Side, out var sideIndex);
            sideIndexes[item.Side] = sideIndex + 1;
            var centerX = item.Bounds.Left + item.Bounds.Width / 2;
            var centerY = item.Bounds.Top + item.Bounds.Height / 2;
            System.Windows.Point source;
            System.Windows.Point elbow;
            System.Windows.Point terminal;
            double labelX;
            double labelY;

            switch (item.Side)
            {
                case CalloutSide.Top:
                    source = new System.Windows.Point(centerX, item.Bounds.Top);
                    var topY = keyboardBounds.Top - 20 - sideIndex % 2 * 42;
                    var topToLeft = centerX < keyboardBounds.Left + keyboardBounds.Width / 2;
                    elbow = new System.Windows.Point(centerX, topY);
                    terminal = new System.Windows.Point(centerX + (topToLeft ? -34 : 34), topY);
                    labelX = Math.Clamp(topToLeft ? terminal.X - 150 : terminal.X + 7, 4, ControlCalloutCanvas.ActualWidth - 154);
                    labelY = Math.Max(0, topY - 38);
                    break;
                case CalloutSide.Bottom:
                    source = new System.Windows.Point(centerX, item.Bounds.Bottom);
                    var bottomY = keyboardBounds.Bottom + 20 + sideIndex % 2 * 42;
                    var bottomToLeft = centerX < keyboardBounds.Left + keyboardBounds.Width / 2;
                    elbow = new System.Windows.Point(centerX, bottomY);
                    terminal = new System.Windows.Point(centerX + (bottomToLeft ? -34 : 34), bottomY);
                    labelX = Math.Clamp(bottomToLeft ? terminal.X - 150 : terminal.X + 7, 4, ControlCalloutCanvas.ActualWidth - 154);
                    labelY = Math.Min(ControlCalloutCanvas.ActualHeight - 38, bottomY + 4);
                    break;
                case CalloutSide.Left:
                    source = new System.Windows.Point(item.Bounds.Left, centerY);
                    var leftX = keyboardBounds.Left - 24 - sideIndex % 2 * 30;
                    elbow = new System.Windows.Point(leftX, centerY);
                    terminal = new System.Windows.Point(Math.Max(8, leftX - 34), centerY);
                    labelX = Math.Max(4, terminal.X - 150);
                    labelY = Math.Clamp(centerY - 19, 0, ControlCalloutCanvas.ActualHeight - 40);
                    break;
                default:
                    source = new System.Windows.Point(item.Bounds.Right, centerY);
                    var rightX = keyboardBounds.Right + 24 + sideIndex % 2 * 30;
                    elbow = new System.Windows.Point(rightX, centerY);
                    terminal = new System.Windows.Point(Math.Min(ControlCalloutCanvas.ActualWidth - 154, rightX + 34), centerY);
                    labelX = Math.Min(ControlCalloutCanvas.ActualWidth - 150, terminal.X + 7);
                    labelY = Math.Clamp(centerY - 19, 0, ControlCalloutCanvas.ActualHeight - 40);
                    break;
            }

            var line = new System.Windows.Shapes.Polyline
            {
                Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x78, 0x76)),
                StrokeThickness = 1,
                Points = new PointCollection { source, elbow, terminal }
            };
            ControlCalloutCanvas.Children.Add(line);

            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = (System.Windows.Media.Brush)FindResource("Primary"),
                Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0xFF, 0xFC)),
                StrokeThickness = 1
            };
            Canvas.SetLeft(dot, source.X - 4);
            Canvas.SetTop(dot, source.Y - 4);
            ControlCalloutCanvas.Children.Add(dot);

            var label = new StackPanel { Width = 150 };
            label.Children.Add(new TextBlock
            {
                Text = item.Callout.Label,
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary"),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold
            });
            label.Children.Add(new TextBlock
            {
                Text = item.Callout.Value,
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary"),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Canvas.SetLeft(label, labelX);
            Canvas.SetTop(label, labelY);
            ControlCalloutCanvas.Children.Add(label);
        }
    }

    private static string? ResolveVisualToken(string? token)
    {
        if (!KeyMap.TryParse(token, out var virtualKey)) return null;
        return KeyMap.ToToken(virtualKey) switch
        {
            "SHIFT" => "LSHIFT",
            "CTRL" => "LCTRL",
            "ALT" => "LALT",
            var canonical => canonical
        };
    }

    private void ControlCalloutCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() => RenderControlCallouts(_selectedProfile)),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private static void AddExpandedToken(HashSet<string> target, string? token)
    {
        if (!KeyMap.TryParse(token, out var virtualKey)) return;
        var canonical = KeyMap.ToToken(virtualKey);
        switch (canonical)
        {
            case "SHIFT": target.Add("LSHIFT"); target.Add("RSHIFT"); break;
            case "CTRL": target.Add("LCTRL"); target.Add("RCTRL"); break;
            case "ALT": target.Add("LALT"); target.Add("RALT"); break;
            default: target.Add(canonical); break;
        }
    }

    private void OpenSelected_Click(object sender, RoutedEventArgs e) => OpenEditor();
    private void ProfilesList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenEditor();
    private void NewProfile_Click(object sender, RoutedEventArgs e) => OpenEditor(forceNew: true);
    private void Refresh_Click(object sender, RoutedEventArgs e) => ReloadProfiles((ProfilesList.SelectedItem as ProfileListItem)?.Name);

    private void OpenEditor(bool forceNew = false)
    {
        var selected = ProfilesList.SelectedItem as ProfileListItem;
        var profileName = forceNew || selected?.IsDefault != false ? null : selected.Name;
        var editor = new MainWindow(profileName) { Owner = this };
        _activeEditor = editor;
        Hide();
        try
        {
            editor.ShowDialog();
        }
        finally
        {
            _activeEditor = null;
            Show();
            Activate();
            ReloadProfiles(profileName);
        }
    }

    private void CreateTrayIcon()
    {
        if (_trayIcon is not null) return;
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Abrir MacroFlow", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add("Detener macro", null, (_, _) => Dispatcher.BeginInvoke(async () =>
        {
            if (_activeEditor is not null) await _activeEditor.StopFromTrayAsync();
        }));
        menu.Items.Add("Volver al inicio", null, (_, _) => Dispatcher.Invoke(ReturnToDashboard));
        menu.Items.Add("Salir", null, (_, _) => Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown()));

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = AppIcon.LoadForTray(),
            Text = "MacroFlow — inicio",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    private void DashboardWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized) return;
        Hide();
        ShowTrayBalloon();
    }

    private void ShowFromTray()
    {
        if (_activeEditor is not null)
        {
            _activeEditor.RestoreFromTray();
            return;
        }
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ReturnToDashboard()
    {
        if (_activeEditor is not null)
        {
            _activeEditor.Close();
            return;
        }
        ShowFromTray();
    }

    internal void ShowTrayBalloon() => _trayIcon?.ShowBalloonTip(
        1500, "MacroFlow", "MacroFlow sigue disponible en los iconos ocultos.",
        System.Windows.Forms.ToolTipIcon.Info);

    internal void SetTrayStatus(string status)
    {
        if (_trayIcon is not null) _trayIcon.Text = $"MacroFlow — {status}";
    }

    private static void MigrateLegacyProfiles(string legacyDirectory, string newDirectory)
    {
        if (!Directory.Exists(legacyDirectory) || Path.GetFullPath(legacyDirectory) == Path.GetFullPath(newDirectory)) return;
        Directory.CreateDirectory(newDirectory);
        foreach (var source in Directory.EnumerateFiles(legacyDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            var destination = Path.Combine(newDirectory, Path.GetFileName(source));
            if (!File.Exists(destination)) File.Copy(source, destination);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Icon?.Dispose();
            _trayIcon.Dispose();
        }
        base.OnClosed(e);
    }
}
