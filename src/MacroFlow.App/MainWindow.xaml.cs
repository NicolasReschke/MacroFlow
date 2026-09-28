using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MacroFlow.App.Input;
using MacroFlow.App.Services;
using MacroFlow.Core.Engine;
using MacroFlow.Core.Models;
using MacroFlow.Core.Persistence;

namespace MacroFlow.App;

public partial class MainWindow : Window
{
    private const string HoldPauseReason = "Parry";
    private const string MorphPauseReason = "Morph";
    private const string TargetPauseReason = "Fuera del juego";

    private readonly ObservableCollection<MacroAction> _actions = [];
    private readonly WindowsInputEmitter _input = new();
    private readonly GlobalKeyboardHook _keyboardHook = new();
    private readonly MacroEngine _engine;
    private readonly ProfileStore _profileStore;
    private readonly HashSet<int> _physicalKeysDown = [];
    private readonly DispatcherTimer _targetTimer;
    private CancellationTokenSource? _resumeCancellation;
    private MacroProfile? _runningProfile;
    private bool _morphPaused;
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    public MainWindow()
    {
        InitializeComponent();
        _engine = new MacroEngine(_input);
        _profileStore = new ProfileStore(Path.Combine(AppContext.BaseDirectory, "profiles"));
        _targetTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, TargetTimer_Tick, Dispatcher);

        ActionsGrid.ItemsSource = _actions;
        ActionsGrid.AlternationCount = 10_000;
        LoadProfileIntoEditor(new MacroProfile());

        Loaded += Window_Loaded;
        StateChanged += Window_StateChanged;
        _keyboardHook.KeyChanged += KeyboardHook_KeyChanged;
        _engine.StateChanged += Engine_StateChanged;
        _engine.Faulted += Engine_Faulted;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _keyboardHook.Start();
            CreateTrayIcon();
            RefreshProfileList();
            SetInfo("Listo. Configurá las teclas y guardá un perfil antes de jugar.");
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized) return;
        Hide();
        _trayIcon?.ShowBalloonTip(1500, "MacroFlow", "MacroFlow sigue disponible en la bandeja.", System.Windows.Forms.ToolTipIcon.Info);
    }

    private void CreateTrayIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Abrir MacroFlow", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add("Detener macro", null, async (_, _) => await Dispatcher.InvokeAsync(StopMacroAsync));
        menu.Items.Add("Salir", null, (_, _) => Dispatcher.Invoke(Close));

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "MacroFlow — detenido",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void KeyboardHook_KeyChanged(object? sender, GlobalKeyEvent e)
    {
        if (e.IsInjected) return;
        Dispatcher.BeginInvoke(async () => await HandlePhysicalKeyAsync(e));
    }

    private async Task HandlePhysicalKeyAsync(GlobalKeyEvent e)
    {
        var isFirstDown = e.IsDown && _physicalKeysDown.Add(e.VirtualKey);
        if (!e.IsDown) _physicalKeysDown.Remove(e.VirtualKey);

        if (Matches(e.VirtualKey, EmergencyKeyBox.Text) && isFirstDown)
        {
            await StopMacroAsync();
            SetInfo("Parada de emergencia ejecutada.");
            return;
        }

        if (IsEditingText()) return;

        if (Matches(e.VirtualKey, ToggleKeyBox.Text) && isFirstDown)
        {
            if (_engine.State == MacroEngineState.Stopped)
            {
                if (IsTargetAllowed(BuildProfileFromEditor(showErrors: false))) StartMacro();
                else SetInfo("No se inició: el proceso configurado no está en primer plano.");
            }
            else
            {
                await StopMacroAsync();
            }
            return;
        }

        if (_engine.State == MacroEngineState.Stopped || _runningProfile is null) return;

        if (Matches(e.VirtualKey, _runningProfile.HoldPauseKey))
        {
            if (e.IsDown && IsTargetAllowed(_runningProfile))
            {
                _resumeCancellation?.Cancel();
                await _engine.SetPauseAsync(HoldPauseReason, true);
            }
            else if (!e.IsDown)
            {
                ScheduleHoldPauseRelease(_runningProfile.ResumeDelayMs);
            }
            return;
        }

        if (Matches(e.VirtualKey, _runningProfile.MorphToggleKey) && isFirstDown && IsTargetAllowed(_runningProfile))
        {
            _morphPaused = !_morphPaused;
            await _engine.SetPauseAsync(MorphPauseReason, _morphPaused);
            SetInfo(_morphPaused ? "Pausa de morph activada." : "Pausa de morph desactivada.");
        }
    }

    private void ScheduleHoldPauseRelease(int delayMs)
    {
        _resumeCancellation?.Cancel();
        _resumeCancellation?.Dispose();
        _resumeCancellation = new CancellationTokenSource();
        var token = _resumeCancellation.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Math.Max(0, delayMs), token);
                await _engine.SetPauseAsync(HoldPauseReason, false);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private async void TargetTimer_Tick(object? sender, EventArgs e)
    {
        if (_engine.State == MacroEngineState.Stopped || _runningProfile is null) return;
        var shouldPause = _runningProfile.RestrictToTargetProcess && !ForegroundProcessService.IsForeground(_runningProfile.TargetProcessName);
        await _engine.SetPauseAsync(TargetPauseReason, shouldPause);
    }

    private void Start_Click(object sender, RoutedEventArgs e) => StartMacro();

    private void StartMacro()
    {
        var profile = BuildProfileFromEditor(showErrors: true);
        if (profile is null) return;
        if (!IsTargetAllowed(profile))
        {
            SetInfo("No se inició: el proceso configurado no está en primer plano.");
            return;
        }

        _runningProfile = profile;
        _morphPaused = false;
        if (!_engine.Start(profile))
        {
            SetInfo("La macro ya está ejecutándose o no tiene acciones.");
            return;
        }

        _targetTimer.Start();
        SetInfo($"Macro «{profile.Name}» iniciada. {profile.EmergencyStopKey} siempre la detiene.");
    }

    private async void Stop_Click(object sender, RoutedEventArgs e) => await StopMacroAsync();

    private async Task StopMacroAsync()
    {
        _targetTimer.Stop();
        _resumeCancellation?.Cancel();
        _morphPaused = false;
        await _engine.StopAsync();
        _runningProfile = null;
        SetInfo("Macro detenida; todas las teclas sintéticas fueron liberadas.");
    }

    private void Engine_StateChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(UpdateStatus);

    private void UpdateStatus()
    {
        StatusText.Text = _engine.State switch
        {
            MacroEngineState.Running => "ACTIVO",
            MacroEngineState.Paused => "PAUSADO",
            _ => "DETENIDO"
        };
        StatusDot.Fill = _engine.State switch
        {
            MacroEngineState.Running => new SolidColorBrush(System.Windows.Media.Color.FromRgb(66, 214, 200)),
            MacroEngineState.Paused => new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11)),
            _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128))
        };

        var reasons = _engine.Pause.Reasons;
        PauseReasonsText.Text = reasons.Count == 0 ? "Sin pausas activas" : string.Join(" · ", reasons);
        if (_trayIcon is not null) _trayIcon.Text = $"MacroFlow — {StatusText.Text.ToLowerInvariant()}";
    }

    private void Engine_Faulted(object? sender, Exception exception)
    {
        Dispatcher.BeginInvoke(() =>
        {
            SetInfo($"Error: {exception.Message}");
            ShowError(exception.Message);
        });
    }

    private void AddAction_Click(object sender, RoutedEventArgs e)
    {
        if (ActionKindCombo.SelectedItem is not ComboBoxItem item ||
            !Enum.TryParse<MacroActionKind>(item.Tag?.ToString(), out var kind) ||
            !int.TryParse(ActionDurationBox.Text, out var duration) || duration < 0)
        {
            ShowError("Elegí una acción y escribí una duración válida.");
            return;
        }

        var value = ActionValueBox.Text.Trim();
        if (kind is MacroActionKind.KeyPress or MacroActionKind.KeyDown or MacroActionKind.KeyUp && !KeyMap.TryParse(value, out _))
        {
            ShowError("Tecla no reconocida. Usá A–Z, 0–9, F1–F24 o nombres como Space, Ctrl y Enter.");
            return;
        }

        if (kind == MacroActionKind.MouseClick && value.ToLowerInvariant() is not ("left" or "right" or "middle" or "izquierdo" or "derecho" or "medio"))
        {
            ShowError("Para un clic usá: left, right o middle.");
            return;
        }

        _actions.Add(new MacroAction { Kind = kind, Value = kind == MacroActionKind.Delay ? string.Empty : value, DurationMs = duration });
        ActionsGrid.SelectedIndex = _actions.Count - 1;
    }

    private void DeleteAction_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsGrid.SelectedItem is MacroAction action) _actions.Remove(action);
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int offset)
    {
        var index = ActionsGrid.SelectedIndex;
        var destination = index + offset;
        if (index < 0 || destination < 0 || destination >= _actions.Count) return;
        _actions.Move(index, destination);
        ActionsGrid.SelectedIndex = destination;
    }

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = BuildProfileFromEditor(showErrors: true);
        if (profile is null) return;
        try
        {
            await _profileStore.SaveAsync(profile);
            RefreshProfileList(profile.Name);
            SetInfo($"Perfil guardado en: {_profileStore.DirectoryPath}");
        }
        catch (Exception exception)
        {
            ShowError($"No se pudo guardar el perfil: {exception.Message}");
        }
    }

    private void RefreshProfiles_Click(object sender, RoutedEventArgs e) => RefreshProfileList();

    private async void ProfilesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilesCombo.SelectedItem is not string name) return;
        try
        {
            LoadProfileIntoEditor(await _profileStore.LoadAsync(name));
            SetInfo($"Perfil «{name}» cargado.");
        }
        catch (Exception exception)
        {
            ShowError($"No se pudo cargar el perfil: {exception.Message}");
        }
    }

    private void RefreshProfileList(string? selected = null)
    {
        ProfilesCombo.ItemsSource = _profileStore.ListProfiles();
        if (!string.IsNullOrWhiteSpace(selected)) ProfilesCombo.SelectedItem = selected;
    }

    private MacroProfile? BuildProfileFromEditor(bool showErrors)
    {
        if (string.IsNullOrWhiteSpace(ProfileNameBox.Text) ||
            !int.TryParse(ResumeDelayBox.Text, out var resumeDelay) || resumeDelay is < 0 or > 10_000)
        {
            if (showErrors) ShowError("El perfil necesita un nombre y un retardo entre 0 y 10000 ms.");
            return null;
        }

        var keys = new[] { ToggleKeyBox.Text, EmergencyKeyBox.Text, HoldPauseKeyBox.Text, MorphKeyBox.Text };
        if (keys.Any(key => !KeyMap.TryParse(key, out _)))
        {
            if (showErrors) ShowError("Una de las teclas de control no es válida.");
            return null;
        }

        if (_actions.Count == 0)
        {
            if (showErrors) ShowError("Agregá al menos una acción a la macro.");
            return null;
        }

        return new MacroProfile
        {
            Name = ProfileNameBox.Text.Trim(),
            Repeat = RepeatCheck.IsChecked == true,
            ToggleHotkey = ToggleKeyBox.Text.Trim(),
            EmergencyStopKey = EmergencyKeyBox.Text.Trim(),
            HoldPauseKey = HoldPauseKeyBox.Text.Trim(),
            MorphToggleKey = MorphKeyBox.Text.Trim(),
            ResumeDelayMs = resumeDelay,
            RestrictToTargetProcess = RestrictProcessCheck.IsChecked == true,
            TargetProcessName = TargetProcessBox.Text.Trim(),
            Actions = _actions.Select(CloneAction).ToList()
        };
    }

    private void LoadProfileIntoEditor(MacroProfile profile)
    {
        ProfileNameBox.Text = profile.Name;
        ToggleKeyBox.Text = profile.ToggleHotkey;
        EmergencyKeyBox.Text = profile.EmergencyStopKey;
        HoldPauseKeyBox.Text = profile.HoldPauseKey;
        MorphKeyBox.Text = profile.MorphToggleKey;
        ResumeDelayBox.Text = profile.ResumeDelayMs.ToString();
        RepeatCheck.IsChecked = profile.Repeat;
        RestrictProcessCheck.IsChecked = profile.RestrictToTargetProcess;
        TargetProcessBox.Text = profile.TargetProcessName;
        _actions.Clear();
        foreach (var action in profile.Actions) _actions.Add(CloneAction(action));
    }

    private bool IsTargetAllowed(MacroProfile? profile) =>
        profile is not null && (!profile.RestrictToTargetProcess || ForegroundProcessService.IsForeground(profile.TargetProcessName));

    private bool IsEditingText() => IsActive && System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox;
    private static bool Matches(int virtualKey, string key) => KeyMap.TryParse(key, out var configured) && configured == virtualKey;
    private static MacroAction CloneAction(MacroAction action) => new() { Kind = action.Kind, Value = action.Value, DurationMs = action.DurationMs };
    private void SetInfo(string message) => InfoText.Text = message;
    private void ShowError(string message) => System.Windows.MessageBox.Show(this, message, "MacroFlow", MessageBoxButton.OK, MessageBoxImage.Warning);

    protected override void OnClosing(CancelEventArgs e)
    {
        _keyboardHook.Dispose();
        _targetTimer.Stop();
        _resumeCancellation?.Cancel();
        _engine.StopAsync().GetAwaiter().GetResult();
        _trayIcon?.Dispose();
        base.OnClosing(e);
    }
}
