using System.IO;
using System.Windows;
using System.Windows.Threading;
using Mullion.Core;
using Mullion.Hooks;
using Mullion.Interop;
using Mullion.UI;

namespace Mullion;

/// <summary>Starts the app, owns its parts and keeps the settings in step with them.</summary>
public partial class App : Application
{
    private Mutex? _singleInstance;
    private Settings _settings = new();
    private OverlayWindow? _overlay;
    private InputThread? _input;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private SettingsViewModel? _settingsViewModel;
    private DispatcherTimer? _saveTimer;
    private bool _paused;
    private bool _recordingShortcut;
    private readonly ZoneRunner _zoneRunner = new();
    private IReadOnlyList<Hotkey?> _appliedHotkeys = [];
    private IReadOnlyList<int> _takenOverShortcuts = [];

    /// <summary>The folder that holds the executable, the settings and the log.</summary>
    public static string DataDirectory => AppContext.BaseDirectory;

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // "Mullion.exe --render-overlay picture.png" draws the grid to a file and exits.
        if (e.Args.Length == 2 && e.Args[0] == "--render-overlay")
        {
            try
            {
                OverlayWindow.RenderPreview(e.Args[1], SettingsStore.Load(SettingsPath));
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Log.Write("Could not render the overlay picture.", ex);
                Shutdown(1);
            }

            return;
        }

        // "--second-instance" is for testing a build while another copy is running.
        // Two copies both react to the mouse gestures, so it is not for normal use.
        bool allowSecond = e.Args.Contains("--second-instance");

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Mullion.SingleInstance", out bool isFirst);
        if (!isFirst && !allowSecond)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write("Unexpected error.", args.Exception);
            args.Handled = true;
        };

        _settings = SettingsStore.Load(SettingsPath);

        _overlay = new OverlayWindow();
        _overlay.Prepare();

        _input = new InputThread(
            Dispatcher,
            model => _overlay.Apply(model),
            _zoneRunner.Run,
            OnShortcutProblems);
        ApplySettings();

        if (!_input.Start())
        {
            MessageBox.Show(
                "Mullion could not start watching the mouse, so the grid will not appear.\n\nDetails are in mullion.log next to Mullion.exe.",
                "Mullion",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        _tray = new TrayIcon(OpenSettings, SetPaused, Shutdown);

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) => SaveNow();

        // "Mullion.exe --settings" opens the settings window straight away;
        // "--settings zones" opens it on the named tab.
        int settingsArg = Array.IndexOf(e.Args, "--settings");
        if (settingsArg >= 0)
        {
            OpenSettings();
            if (settingsArg + 1 < e.Args.Length)
            {
                _settingsWindow?.SelectTab(e.Args[settingsArg + 1]);
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_saveTimer?.IsEnabled == true)
        {
            SaveNow();
        }

        _input?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Activate();
            return;
        }

        var viewModel = new SettingsViewModel(_settings)
        {
            SetRecorder = SetShortcutRecorder,
            IsOwnedElsewhere = IsOwnedByAnotherProgram,
        };
        viewModel.Changed += OnSettingsChanged;
        viewModel.SetTakenOverShortcuts(_takenOverShortcuts);
        _settingsViewModel = viewModel;

        _settingsWindow = new SettingsWindow(viewModel);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _settingsViewModel = null;
            SetShortcutRecorder(null);
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void SetPaused(bool paused)
    {
        _paused = paused;
        ApplySettings();
    }

    /// <summary>
    /// Starts recording a shortcut (or stops, with null). While recording, every key
    /// combination goes to the callback and existing shortcuts do not fire.
    /// </summary>
    private void SetShortcutRecorder(Action<Hotkey>? captured)
    {
        _recordingShortcut = captured is not null;
        _input?.SetRecorder(captured);
        _input?.SetHotkeysSuspended(_paused || _recordingShortcut);
    }

    /// <summary>These zones' shortcuts belong to other programs and were taken over; show that in the zone list.</summary>
    private void OnShortcutProblems(IReadOnlyList<int> positions)
    {
        _takenOverShortcuts = positions;
        _settingsViewModel?.SetTakenOverShortcuts(positions);
    }

    /// <summary>True when a program other than Mullion has registered the shortcut with Windows.</summary>
    private bool IsOwnedByAnotherProgram(Hotkey hotkey)
    {
        // While Mullion's own shortcuts are registered, Windows would report those as
        // taken too, so answer from what Mullion already knows about them.
        if (!_paused && !_recordingShortcut)
        {
            for (int i = 0; i < _appliedHotkeys.Count; i++)
            {
                if (_appliedHotkeys[i] == hotkey)
                {
                    return _takenOverShortcuts.Contains(i);
                }
            }
        }

        return Native.IsHotkeyTaken(hotkey);
    }

    private void OnSettingsChanged()
    {
        ApplySettings();

        // Sliders report many changes a second; write the file once they settle.
        _saveTimer?.Stop();
        _saveTimer?.Start();
    }

    /// <summary>Pushes the current settings to the gesture logic and the overlay.</summary>
    private void ApplySettings()
    {
        _settings.Normalize();

        if (_input is not null)
        {
            _input.SnapModifierKey = _settings.SnapMoveModifier switch
            {
                SnapModifier.Ctrl => Native.VK_CONTROL,
                SnapModifier.Shift => Native.VK_SHIFT,
                SnapModifier.Alt => Native.VK_MENU,
                _ => 0,
            };

            _input.Machine.Config = new GestureConfig(
                _settings.Columns,
                _settings.Rows,
                _settings.WindowGap,
                _settings.EdgeGap,
                SnapMoveEnabled: _settings.SnapMoveModifier != SnapModifier.None,
                Paused: _paused);

            // Registering shortcuts is comparatively slow and this runs for every
            // slider movement, so only do it when the shortcuts actually changed.
            var hotkeys = _settings.Zones.Select(zone => zone.Hotkey).ToList();
            if (!hotkeys.SequenceEqual(_appliedHotkeys))
            {
                _appliedHotkeys = hotkeys;
                _input.SetZoneHotkeys(hotkeys);
            }

            _input.SetHotkeysSuspended(_paused || _recordingShortcut);
        }

        _zoneRunner.Update(_settings);

        _overlay?.SetAppearance(_settings.Appearance);
    }

    private void SaveNow()
    {
        _saveTimer?.Stop();

        try
        {
            SettingsStore.Save(SettingsPath, _settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write("Could not save settings.", ex);
        }
    }
}
