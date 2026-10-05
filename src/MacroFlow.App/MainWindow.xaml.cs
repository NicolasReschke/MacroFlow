using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using MacroFlow.App.Input;
using MacroFlow.App.Services;
using MacroFlow.Core.Engine;
using MacroFlow.Core.Models;
using MacroFlow.Core.Persistence;
using MacroFlow.Core.Recording;

namespace MacroFlow.App;

public partial class MainWindow : Window
{
    private enum KeyCaptureTarget
    {
        None,
        Toggle,
        Emergency,
        HoldPause,
        Morph,
        Recorder
    }

    private const string HoldPauseReason = "Parry";
    private const string MorphPauseReason = "Morph";
    private const string TargetPauseReason = "Fuera del juego";

    private readonly ObservableCollection<MacroAction> _actions = [];
    private readonly WindowsInputEmitter _input = new();
    private readonly GlobalKeyboardHook _keyboardHook = new();
    private readonly GlobalMouseHook _mouseHook = new();
    private readonly MacroRecorder _recorder = new();
    private readonly MacroEngine _engine;
    private readonly ProfileStore _profileStore;
    private readonly HashSet<int> _physicalKeysDown = [];
    private readonly DispatcherTimer _targetTimer;
    private CancellationTokenSource? _resumeCancellation;
    private MacroProfile? _runningProfile;
    private bool _morphPaused;
    private bool _modalWindowOpen;
    private KeyCaptureTarget _captureTarget;
    private System.Windows.Point _dragStartPoint;
    private MacroAction? _draggedAction;
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    public MainWindow()
    {
        InitializeComponent();
        _engine = new MacroEngine(_input);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var profileDirectory = Path.Combine(documents, "MacroFlow", "Macros");
        _profileStore = new ProfileStore(profileDirectory);
        MigrateLegacyProfiles(Path.Combine(AppContext.BaseDirectory, "profiles"), profileDirectory);
        _targetTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, TargetTimer_Tick, Dispatcher);

        ActionsGrid.ItemsSource = new CompositeCollection
        {
            new CollectionContainer { Collection = _actions },
            AddActionPlaceholder.Instance
        };
        ActionsGrid.AlternationCount = 10_000;
        LoadProfileIntoEditor(new MacroProfile());

        Loaded += Window_Loaded;
        StateChanged += Window_StateChanged;
        _keyboardHook.KeyChanged += KeyboardHook_KeyChanged;
        _mouseHook.ButtonChanged += MouseHook_ButtonChanged;
        _engine.StateChanged += Engine_StateChanged;
        _engine.Faulted += Engine_Faulted;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _keyboardHook.Start();
            _mouseHook.Start();
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

        // Close the small check/send race before Windows forwards a parry or morph key to the game.
        // SetPauseAsync is synchronous for this emitter except for gate contention with an input already in progress.
        var runningProfile = _runningProfile;
        if (e.IsDown && runningProfile is not null && _engine.State != MacroEngineState.Stopped &&
            (Matches(e.VirtualKey, runningProfile.HoldPauseKey) ||
             Matches(e.VirtualKey, runningProfile.MorphToggleKey)))
        {
            _engine.SetPauseAsync(
                    Matches(e.VirtualKey, runningProfile.HoldPauseKey) ? HoldPauseReason : MorphPauseReason,
                    true)
                .GetAwaiter().GetResult();
        }

        Dispatcher.BeginInvoke(async () => await HandlePhysicalKeyAsync(e));
    }

    private void MouseHook_ButtonChanged(object? sender, GlobalMouseEvent e)
    {
        if (e.IsInjected || !e.IsDown || !_recorder.IsRecording) return;
        _recorder.RecordMouseClick(e.Button, Environment.TickCount64);
    }

    private async Task HandlePhysicalKeyAsync(GlobalKeyEvent e)
    {
        var isFirstDown = e.IsDown && _physicalKeysDown.Add(e.VirtualKey);
        if (!e.IsDown) _physicalKeysDown.Remove(e.VirtualKey);

        if (_captureTarget != KeyCaptureTarget.None)
        {
            if (!e.IsDown) return;
            if (!isFirstDown) return;
            if (_captureTarget == KeyCaptureTarget.Recorder)
            {
                if (HotkeyGesture.IsModifier(e.VirtualKey))
                {
                    CaptureText.Text = "Mantené el modificador y presioná la tecla principal…";
                    return;
                }
                CompleteRecorderHotkeyCapture(e.VirtualKey);
                return;
            }
            CompleteKeyCapture(e.VirtualKey);
            return;
        }

        if (IsEditingText()) return;

        var recorderGesture = GetRecorderHotkey();
        if (isFirstDown && HotkeyGesture.Matches(e.VirtualKey, _physicalKeysDown, recorderGesture))
        {
            await ToggleRecordingAsync(recorderGesture);
            return;
        }

        if (_recorder.IsRecording)
        {
            _recorder.RecordKey(KeyMap.ToToken(e.VirtualKey), e.IsDown, Environment.TickCount64);
            return;
        }

        if (Matches(e.VirtualKey, GetKeyToken(EmergencyKeyBox)) && isFirstDown)
        {
            await StopMacroAsync();
            SetInfo("Parada de emergencia ejecutada.");
            return;
        }

        if (Matches(e.VirtualKey, GetKeyToken(ToggleKeyBox)) && isFirstDown)
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

    private async Task ToggleRecordingAsync(string gesture)
    {
        if (_recorder.IsRecording)
        {
            var actions = _recorder.Stop(Environment.TickCount64, HotkeyGesture.Tokens(gesture));
            UpdateStatus();
            if (actions.Count == 0)
            {
                SetInfo("La grabación terminó sin entradas para agregar.");
                return;
            }

            ShowFromTray();
            var review = new RecordingReviewWindow(actions) { Owner = this };
            _modalWindowOpen = true;
            bool accepted;
            try
            {
                accepted = review.ShowDialog() == true;
            }
            finally
            {
                _modalWindowOpen = false;
            }

            if (!accepted) { SetInfo("Grabación descartada."); return; }
            if (review.Result == RecordingReviewResult.Replace) _actions.Clear();
            foreach (var action in actions) _actions.Add(CloneAction(action));
            SetInfo($"Grabación agregada: {actions.Count} acciones. Guardá el perfil para conservarla.");
            return;
        }

        var profile = BuildProfileFromEditor(showErrors: false, requireActions: false);
        if (profile is null || !IsTargetAllowed(profile))
        {
            SetInfo("No se inició la grabación: el juego configurado no está en primer plano.");
            return;
        }

        if (_engine.State != MacroEngineState.Stopped) await StopMacroAsync();
        _recorder.Start(Environment.TickCount64);
        UpdateStatus();
        SetInfo($"Grabando entradas físicas… Presioná {HotkeyGesture.GetDisplayName(gesture)} para terminar.");
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
        if (_recorder.IsRecording)
        {
            StatusText.Text = "GRABANDO";
            StatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
            PauseReasonsText.Text = "Sólo entradas físicas";
            if (_trayIcon is not null) _trayIcon.Text = "MacroFlow — grabando";
            return;
        }
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

    private void CaptureKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string target } ||
            !Enum.TryParse<KeyCaptureTarget>(target, out var captureTarget)) return;

        _captureTarget = captureTarget;
        CapturePanel.Visibility = Visibility.Visible;
        CaptureText.Text = captureTarget switch
        {
            KeyCaptureTarget.Toggle => "Presioná la tecla para iniciar o detener la secuencia…",
            KeyCaptureTarget.Emergency => "Presioná la tecla de parada de emergencia…",
            KeyCaptureTarget.HoldPause => "Presioná la tecla que pausará mientras la mantengas…",
            KeyCaptureTarget.Morph => "Presioná la tecla que alternará la pausa de morph…",
            KeyCaptureTarget.Recorder => "Mantené Ctrl, Shift o Alt si querés y presioná la tecla principal…",
            _ => "Presioná una tecla…"
        };
        SetInfo("Captura activa. La próxima tecla física quedará asignada.");
    }

    private void CancelCapture_Click(object sender, RoutedEventArgs e) => CancelKeyCapture();

    private void ShowVirtualKeyboard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string target } ||
            !Enum.TryParse<KeyCaptureTarget>(target, out var captureTarget)) return;

        var purpose = captureTarget switch
        {
            KeyCaptureTarget.Toggle => "Elegí la tecla para iniciar o detener la secuencia.",
            KeyCaptureTarget.Emergency => "Elegí la tecla de parada de emergencia.",
            KeyCaptureTarget.HoldPause => "Elegí la tecla que pausará mientras la mantengas.",
            KeyCaptureTarget.Morph => "Elegí la tecla para entrar o salir de morph.",
            _ => "Elegí una tecla."
        };
        var keyboard = new VirtualKeyboardWindow(purpose) { Owner = this };
        _modalWindowOpen = true;
        try
        {
            if (keyboard.ShowDialog() == true && keyboard.SelectedToken is { } token)
            {
                SetCapturedKey(captureTarget, token);
            }
        }
        finally
        {
            _modalWindowOpen = false;
        }
    }

    private void CancelKeyCapture()
    {
        _captureTarget = KeyCaptureTarget.None;
        CapturePanel.Visibility = Visibility.Collapsed;
        SetInfo("Captura cancelada; no se modificó ninguna tecla.");
    }

    private void CompleteKeyCapture(int virtualKey)
    {
        var target = _captureTarget;
        var token = KeyMap.ToToken(virtualKey);
        SetCapturedKey(target, token);
        _captureTarget = KeyCaptureTarget.None;
        CapturePanel.Visibility = Visibility.Collapsed;
    }

    private void CompleteRecorderHotkeyCapture(int virtualKey)
    {
        var gesture = HotkeyGesture.FromPressedKeys(virtualKey, _physicalKeysDown);
        SetRecorderHotkey(gesture);
        _captureTarget = KeyCaptureTarget.None;
        CapturePanel.Visibility = Visibility.Collapsed;
        SetInfo($"Combinación de grabación: {HotkeyGesture.GetDisplayName(gesture)}.");
    }

    private void SetCapturedKey(KeyCaptureTarget target, string token)
    {
        var box = target switch
        {
            KeyCaptureTarget.Toggle => ToggleKeyBox,
            KeyCaptureTarget.Emergency => EmergencyKeyBox,
            KeyCaptureTarget.HoldPause => HoldPauseKeyBox,
            KeyCaptureTarget.Morph => MorphKeyBox,
            _ => null
        };

        if (box is not null) SetKeyBox(box, token);
        SetInfo($"Tecla capturada: {KeyMap.GetDisplayName(token)}.");
    }

    private async void AddAction_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.State != MacroEngineState.Stopped) await StopMacroAsync();
        var editor = new MacroActionEditorWindow { Owner = this };
        _modalWindowOpen = true;
        bool accepted;
        try
        {
            accepted = editor.ShowDialog() == true;
        }
        finally
        {
            _modalWindowOpen = false;
        }
        if (!accepted || editor.ResultAction is not { } action) return;
        _actions.Add(action);
        ActionsGrid.SelectedIndex = _actions.Count - 1;
        ActionsGrid.ScrollIntoView(action);
        SetInfo("Acción agregada. Arrastrala para cambiar su posición.");
    }

    private async void ActionItem_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: MacroAction action }) return;
        e.Handled = true;
        if (_engine.State != MacroEngineState.Stopped) await StopMacroAsync();
        var editor = new MacroActionEditorWindow(CloneAction(action)) { Owner = this };
        _modalWindowOpen = true;
        bool accepted;
        try
        {
            accepted = editor.ShowDialog() == true;
        }
        finally
        {
            _modalWindowOpen = false;
        }
        if (!accepted || editor.ResultAction is not { } edited) return;
        action.Kind = edited.Kind;
        action.Value = edited.Value;
        action.DurationMs = edited.DurationMs;
        ActionsGrid.Items.Refresh();
        ActionsGrid.SelectedItem = action;
        SetInfo("Acción actualizada.");
    }

    private void ActionsGrid_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(ActionsGrid);
        _draggedAction = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as MacroAction;
    }

    private void ActionsGrid_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || _draggedAction is null) return;
        var position = e.GetPosition(ActionsGrid);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var action = _draggedAction;
        _draggedAction = null;
        System.Windows.DragDrop.DoDragDrop(ActionsGrid, action, System.Windows.DragDropEffects.Move);
    }

    private void ActionsGrid_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(MacroAction))
            ? System.Windows.DragDropEffects.Move
            : System.Windows.DragDropEffects.None;
        if (e.Effects == System.Windows.DragDropEffects.Move) MoveDraggedAction(e);
        e.Handled = true;
    }

    private void ActionsGrid_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(typeof(MacroAction)) is not MacroAction action) return;
        MoveDraggedAction(e);
        ActionsGrid.SelectedItem = action;
        SetInfo("Orden actualizado. Guardá el perfil para conservar el cambio.");
    }

    private void MoveDraggedAction(System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(typeof(MacroAction)) is not MacroAction action) return;
        var oldIndex = _actions.IndexOf(action);
        if (oldIndex < 0) return;

        var targetItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        var insertionIndex = targetItem?.DataContext is MacroAction target ? _actions.IndexOf(target) : _actions.Count;
        if (targetItem is not null && e.GetPosition(targetItem).X > targetItem.ActualWidth / 2) insertionIndex++;
        if (oldIndex < insertionIndex) insertionIndex--;
        insertionIndex = Math.Clamp(insertionIndex, 0, _actions.Count - 1);
        if (oldIndex != insertionIndex) _actions.Move(oldIndex, insertionIndex);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void DeleteAction_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsGrid.SelectedItem is MacroAction action) _actions.Remove(action);
    }

    private void ClearActions_Click(object sender, RoutedEventArgs e)
    {
        if (_actions.Count == 0) return;
        if (System.Windows.MessageBox.Show(this, "¿Vaciar toda la secuencia?", "MacroFlow", MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _actions.Clear();
            SetInfo("Secuencia vaciada.");
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int offset)
    {
        if (ActionsGrid.SelectedItem is not MacroAction action) return;
        var index = _actions.IndexOf(action);
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

    private async void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importar macro de MacroFlow",
            Filter = "Perfil de MacroFlow (*.json)|*.json|Todos los archivos (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var profile = await _profileStore.LoadFromPathAsync(dialog.FileName);
            ValidateProfileKeys(profile);
            await _profileStore.SaveAsync(profile);
            LoadProfileIntoEditor(profile);
            RefreshProfileList(profile.Name);
            SetInfo($"Perfil «{profile.Name}» importado.");
        }
        catch (Exception exception)
        {
            ShowError($"No se pudo importar el perfil: {exception.Message}");
        }
    }

    private async void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = BuildProfileFromEditor(showErrors: true);
        if (profile is null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar macro de MacroFlow",
            Filter = "Perfil de MacroFlow (*.json)|*.json",
            FileName = profile.Name + ".json",
            DefaultExt = ".json",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await _profileStore.ExportAsync(profile, dialog.FileName);
            SetInfo($"Perfil exportado a: {dialog.FileName}");
        }
        catch (Exception exception)
        {
            ShowError($"No se pudo exportar el perfil: {exception.Message}");
        }
    }

    private void OpenProfilesFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_profileStore.DirectoryPath);
        Process.Start(new ProcessStartInfo(_profileStore.DirectoryPath) { UseShellExecute = true });
    }

    private void RefreshProfiles_Click(object sender, RoutedEventArgs e) => RefreshProfileList();

    private async void ProfilesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilesCombo.SelectedItem is not string name) return;
        try
        {
            var profile = await _profileStore.LoadAsync(name);
            ValidateProfileKeys(profile);
            LoadProfileIntoEditor(profile);
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

    private MacroProfile? BuildProfileFromEditor(bool showErrors, bool requireActions = true)
    {
        if (string.IsNullOrWhiteSpace(ProfileNameBox.Text) ||
            !int.TryParse(ResumeDelayBox.Text, out var resumeDelay) || resumeDelay is < 0 or > 10_000)
        {
            if (showErrors) ShowError("El perfil necesita un nombre y un retardo entre 0 y 10000 ms.");
            return null;
        }

        var keys = new[] { GetKeyToken(ToggleKeyBox), GetKeyToken(EmergencyKeyBox), GetKeyToken(HoldPauseKeyBox), GetKeyToken(MorphKeyBox) };
        if (keys.Any(key => !KeyMap.TryParse(key, out _)) || !HotkeyGesture.TryParse(GetRecorderHotkey(), out _))
        {
            if (showErrors) ShowError("Una de las teclas de control o la combinación de grabación no es válida.");
            return null;
        }

        if (requireActions && _actions.Count == 0)
        {
            if (showErrors) ShowError("Agregá al menos una acción a la macro.");
            return null;
        }

        return new MacroProfile
        {
            Name = ProfileNameBox.Text.Trim(),
            Repeat = RepeatCheck.IsChecked == true,
            ToggleHotkey = GetKeyToken(ToggleKeyBox),
            EmergencyStopKey = GetKeyToken(EmergencyKeyBox),
            HoldPauseKey = GetKeyToken(HoldPauseKeyBox),
            MorphToggleKey = GetKeyToken(MorphKeyBox),
            RecorderHotkey = GetRecorderHotkey(),
            ResumeDelayMs = resumeDelay,
            RestrictToTargetProcess = RestrictProcessCheck.IsChecked == true,
            TargetProcessName = TargetProcessBox.Text.Trim(),
            Actions = _actions.Select(CloneAction).ToList()
        };
    }

    private void LoadProfileIntoEditor(MacroProfile profile)
    {
        ProfileNameBox.Text = profile.Name;
        SetKeyBox(ToggleKeyBox, profile.ToggleHotkey);
        SetKeyBox(EmergencyKeyBox, profile.EmergencyStopKey);
        SetKeyBox(HoldPauseKeyBox, profile.HoldPauseKey);
        SetKeyBox(MorphKeyBox, profile.MorphToggleKey);
        SetRecorderHotkey(string.IsNullOrWhiteSpace(profile.RecorderHotkey) ? "CTRL+F8" : profile.RecorderHotkey);
        ResumeDelayBox.Text = profile.ResumeDelayMs.ToString();
        RepeatCheck.IsChecked = profile.Repeat;
        RestrictProcessCheck.IsChecked = profile.RestrictToTargetProcess;
        TargetProcessBox.Text = profile.TargetProcessName;
        _actions.Clear();
        foreach (var action in profile.Actions) _actions.Add(CloneAction(action));
    }

    private bool IsTargetAllowed(MacroProfile? profile) =>
        profile is not null && (!profile.RestrictToTargetProcess || ForegroundProcessService.IsForeground(profile.TargetProcessName));

    private bool IsEditingText() => _modalWindowOpen ||
        (IsActive && System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox);
    private static bool Matches(int virtualKey, string key) => KeyMap.Matches(virtualKey, key);
    private static string GetKeyToken(System.Windows.Controls.TextBox box) => box.Tag as string ?? box.Text.Trim();
    private string GetRecorderHotkey() => RecorderHotkeyBox.Tag as string ?? "CTRL+F8";
    private void SetRecorderHotkey(string gesture)
    {
        RecorderHotkeyBox.Tag = gesture;
        RecorderHotkeyBox.Text = HotkeyGesture.GetDisplayName(gesture);
    }
    private static void SetKeyBox(System.Windows.Controls.TextBox box, string token)
    {
        box.Tag = token;
        box.Text = KeyMap.GetDisplayName(token);
    }
    private static MacroAction CloneAction(MacroAction action) => new() { Kind = action.Kind, Value = action.Value, DurationMs = action.DurationMs };
    private static void ValidateProfileKeys(MacroProfile profile)
    {
        var controlKeys = new[] { profile.ToggleHotkey, profile.EmergencyStopKey, profile.HoldPauseKey, profile.MorphToggleKey };
        if (controlKeys.Any(key => !KeyMap.TryParse(key, out _)) || !HotkeyGesture.TryParse(profile.RecorderHotkey, out _))
            throw new InvalidDataException("El perfil contiene una tecla de control no válida.");
        if (profile.Actions.Any(action =>
                action.Kind is MacroActionKind.KeyPress or MacroActionKind.KeyDown or MacroActionKind.KeyUp &&
                !KeyMap.TryParse(action.Value, out _)))
            throw new InvalidDataException("El perfil contiene una acción de teclado no válida.");
        if (profile.Actions.Any(action => action.Kind == MacroActionKind.MouseClick &&
                action.Value.ToLowerInvariant() is not ("left" or "right" or "middle" or "xbutton1" or "xbutton2")))
            throw new InvalidDataException("El perfil contiene un botón de mouse no válido.");
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
    private void SetInfo(string message) => InfoText.Text = message;
    private void ShowError(string message) => System.Windows.MessageBox.Show(this, message, "MacroFlow", MessageBoxButton.OK, MessageBoxImage.Warning);

    protected override void OnClosing(CancelEventArgs e)
    {
        _keyboardHook.Dispose();
        _mouseHook.Dispose();
        _targetTimer.Stop();
        _resumeCancellation?.Cancel();
        _engine.StopAsync().GetAwaiter().GetResult();
        _trayIcon?.Dispose();
        base.OnClosing(e);
    }
}
