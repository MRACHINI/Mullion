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
    private DispatcherTimer? _saveTimer;
    private bool _paused;

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

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Mullion.SingleInstance", out bool isFirst);
        if (!isFirst)
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

        _input = new InputThread(Dispatcher, model => _overlay.Apply(model));
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

        // "Mullion.exe --settings" opens the settings window straight away.
        if (e.Args.Contains("--settings"))
        {
            OpenSettings();
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

        var viewModel = new SettingsViewModel(_settings);
        viewModel.Changed += OnSettingsChanged;

        _settingsWindow = new SettingsWindow(viewModel);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void SetPaused(bool paused)
    {
        _paused = paused;
        ApplySettings();
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
        }

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
