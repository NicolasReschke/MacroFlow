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

    private readonly ObservableCollection<ProfileListItem> _profiles = [];
    private readonly ProfileStore _profileStore;
    private MacroProfile _selectedProfile = new();

    public DashboardWindow()
    {
        InitializeComponent();
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var profileDirectory = Path.Combine(documents, "MacroFlow", "Macros");
        _profileStore = new ProfileStore(profileDirectory);
        MigrateLegacyProfiles(Path.Combine(AppContext.BaseDirectory, "profiles"), profileDirectory);
        ProfilesList.ItemsSource = _profiles;
        Loaded += (_, _) => ReloadProfiles();
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

        ToggleKeyText.Text = KeyMap.GetDisplayName(profile.ToggleHotkey);
        EmergencyKeyText.Text = KeyMap.GetDisplayName(profile.EmergencyStopKey);
        ParryKeyText.Text = KeyMap.GetDisplayName(profile.HoldPauseKey);
        MorphKeyText.Text = KeyMap.GetDisplayName(profile.MorphToggleKey);
        RecorderKeyText.Text = HotkeyGesture.GetDisplayName(profile.RecorderHotkey);

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
        BuildSequenceChips(profile);
    }

    private void BuildKeyboard(HashSet<string> actionKeys, HashSet<string> controlKeys)
    {
        AddKeyboardRow(FunctionKeysRow, ["ESCAPE", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"], actionKeys, controlKeys);
        AddKeyboardRow(ExtraFunctionKeysRow, Enumerable.Range(13, 12).Select(number => $"F{number}"), actionKeys, controlKeys);
        AddKeyboardRow(NumberKeysRow, ["VK_C0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "VK_BD", "VK_BB", "BACKSPACE"], actionKeys, controlKeys);
        AddKeyboardRow(TopKeysRow, ["TAB", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "VK_DB", "VK_DD", "VK_DC"], actionKeys, controlKeys);
        AddKeyboardRow(HomeKeysRow, ["CAPSLOCK", "A", "S", "D", "F", "G", "H", "J", "K", "L", "VK_BA", "VK_DE", "ENTER"], actionKeys, controlKeys);
        AddKeyboardRow(BottomKeysRow, ["LSHIFT", "Z", "X", "C", "V", "B", "N", "M", "VK_BC", "VK_BE", "VK_BF", "RSHIFT"], actionKeys, controlKeys);
        AddKeyboardRow(ModifierKeysRow, ["LCTRL", "LALT", "SPACE", "RALT", "RCTRL", "LEFT", "UP", "DOWN", "RIGHT"], actionKeys, controlKeys);
    }

    private static void AddKeyboardRow(System.Windows.Controls.Panel panel, IEnumerable<string> tokens,
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
        }
    }

    private void BuildSequenceChips(MacroProfile profile)
    {
        SequenceKeysPanel.Children.Clear();
        var tokens = profile.Actions
            .Where(action => action.Kind is MacroActionKind.KeyPress or MacroActionKind.KeyDown or MacroActionKind.KeyUp)
            .Select(action => action.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (tokens.Length == 0)
        {
            SequenceKeysPanel.Children.Add(new TextBlock
            {
                Text = "Sin teclas asignadas",
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary")
            });
        }
        else
        {
            foreach (var token in tokens)
            {
                SequenceKeysPanel.Children.Add(new Border
                {
                    Background = (System.Windows.Media.Brush)FindResource("PrimaryDark"),
                    BorderBrush = (System.Windows.Media.Brush)FindResource("Primary"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 0, 6, 6),
                    Child = new TextBlock { Text = KeyMap.GetDisplayName(token), FontWeight = FontWeights.SemiBold }
                });
            }
        }

        var mouseInputs = profile.Actions.Where(action => action.Kind == MacroActionKind.MouseClick)
            .Select(action => action.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        MouseInputsText.Text = mouseInputs.Length == 0
            ? "Sin botones de mouse en la secuencia."
            : $"Mouse: {string.Join(", ", mouseInputs)}";
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
        Hide();
        try
        {
            editor.ShowDialog();
        }
        finally
        {
            Show();
            Activate();
            ReloadProfiles(profileName);
        }
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
}
