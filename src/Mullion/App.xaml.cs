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
    private const string InstanceName = @"Local\Mullion.SingleInstance";

    private Mutex? _singleInstance;
    private bool _ownsInstance;
    private string[] _arguments = [];
    private readonly Updater _updater = new();
    private ReleaseInfo? _update;
    private Version? _announcedUpdate;
    private string _updateStatus = string.Empty;
    private bool _updateBusy;
    private DispatcherTimer? _updateTimer;
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
    private ArrangeSession? _arrange;
    private volatile int _arrangeHotkeyPosition = -1;
    private readonly RuleRunner _ruleRunner = new();
    private bool _watchingWindows;
    private IReadOnlyList<Hotkey?> _appliedHotkeys = [];
    private IReadOnlyList<int> _takenOverShortcuts = [];

    /// <summary>The folder that holds the executable, the settings and the log.</summary>
    public static string DataDirectory => AppContext.BaseDirectory;

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // "--trace" adds step-by-step details to mullion.log, for tracking down problems.
        Log.TraceEnabled = e.Args.Contains("--trace");

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

        // "Mullion.exe --render-arrange picture.png" draws the arrange-windows screen
        // to a file and exits.
        if (e.Args.Length == 2 && e.Args[0] == "--render-arrange")
        {
            try
            {
                ArrangeOverlay.RenderPreview(e.Args[1], SettingsStore.Load(SettingsPath));
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Log.Write("Could not render the arrange picture.", ex);
                Shutdown(1);
            }

            return;
        }

        // "Mullion.exe --render-settings zones picture.png" draws one tab of the
        // settings window to a file and exits.
        if (e.Args.Length == 3 && e.Args[0] == "--render-settings")
        {
            try
            {
                SettingsWindow.RenderPreview(e.Args[2], SettingsStore.Load(SettingsPath), e.Args[1]);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Log.Write("Could not render the settings picture.", ex);
                Shutdown(1);
            }

            return;
        }

        // "Mullion.exe --list-windows file.txt" writes the windows that "Arrange
        // windows" would go through, front-most first, and exits. Titles are left
        // out so the file can be shared when reporting a problem.
        if (e.Args.Length == 2 && e.Args[0] == "--list-windows")
        {
            try
            {
                var lines = WindowList.GetArrangeable().Select(entry =>
                {
                    var frame = Native.GetFrameBounds(entry.Handle) ?? default;
                    return $"{WindowList.ProgramOf(entry.Handle)} | {Native.GetClassName(entry.Handle)} | {frame.Width} x {frame.Height} at {frame.Left},{frame.Top}";
                });
                File.WriteAllLines(e.Args[1], lines);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Log.Write("Could not list the windows.", ex);
                Shutdown(1);
            }

            return;
        }

        // "Mullion.exe --task-xml file.xml" writes the scheduled task that starts
        // Mullion as administrator at sign-in, without registering it, and exits.
        if (e.Args.Length == 2 && e.Args[0] == "--task-xml")
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                File.WriteAllText(
                    e.Args[1],
                    ElevatedStartTask.BuildXml(Environment.ProcessPath ?? "Mullion.exe", identity.User?.Value ?? identity.Name));
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Log.Write("Could not write the task definition.", ex);
                Shutdown(1);
            }

            return;
        }

        // "--second-instance" is for testing a build while another copy is running.
        // Two copies both react to the mouse gestures, so it is not for normal use.
        bool allowSecond = e.Args.Contains("--second-instance");
        _arguments = e.Args;

        if (!TakeInstance() && !allowSecond)
        {
            ReleaseInstance();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write("Unexpected error.", args.Exception);
            args.Handled = true;
        };

        _settings = SettingsStore.Load(SettingsPath);

        if (_settings.RunAsAdministrator)
        {
            if (Elevation.IsElevated)
            {
                // Starting at sign-in as administrator needs the scheduled task; switch
                // to it now that Mullion has the rights to create it.
                Task.Run(() => AutoStart.Sync(asAdministrator: true));
            }
            else
            {
                // Hand over to a copy that runs as administrator. Windows asks the
                // user first; if they say no, carry on as an ordinary program.
                ReleaseInstance();
                if (Elevation.StartElevated(e.Args))
                {
                    Shutdown();
                    return;
                }

                Log.Write("Mullion is set to run as administrator, but permission was not given. Running without it.");
                TakeInstance();
            }
        }

        _overlay = new OverlayWindow();
        _overlay.Prepare();

        _arrange = new ArrangeSession(() => _settings) { Placed = RememberPlacement };
        _zoneRunner.Placed = RememberPlacement;

        _input = new InputThread(
            Dispatcher,
            model => _overlay.Apply(model),
            OnShortcutPressed,
            OnShortcutProblems)
        {
            WindowShown = _ruleRunner.OnWindowShown,
            GridPlaced = RememberGridPlacement,
        };
        ApplySettings();

        if (!_input.Start())
        {
            MessageBox.Show(
                "Mullion could not start watching the mouse, so the grid will not appear.\n\nDetails are in mullion.log next to Mullion.exe.",
                "Mullion",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        _tray = new TrayIcon(OpenSettings, StartArranging, SetPaused, Shutdown);

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) => SaveNow();

        // Look for a new version shortly after starting, then once a day. The
        // first look also clears away what the last update left behind, by which
        // time the copy that was replaced has closed.
        var updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _updateTimer = updateTimer;
        updateTimer.Tick += (_, _) =>
        {
            if (updateTimer.Interval < TimeSpan.FromDays(1))
            {
                updateTimer.Interval = TimeSpan.FromDays(1);
                if (Environment.ProcessPath is { } executable)
                {
                    Task.Run(() => UpdateInstaller.CleanUp(executable));
                }
            }

            if (_settings.CheckForUpdates && !_updateBusy)
            {
                CheckForUpdates(askedByUser: false);
            }
        };
        updateTimer.Start();

        // "Mullion.exe --arrange" starts arranging windows straight away.
        if (e.Args.Contains("--arrange"))
        {
            StartArranging();
        }

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
        ReleaseInstance();
        base.OnExit(e);
    }

    /// <summary>
    /// Claims the right to be the one running copy. False when another copy has it.
    /// </summary>
    private bool TakeInstance()
    {
        try
        {
            _singleInstance = new Mutex(initiallyOwned: true, InstanceName, out bool isFirst);
            _ownsInstance = isFirst;
            return isFirst;
        }
        catch (UnauthorizedAccessException)
        {
            // The copy that is running runs as administrator and this one does not,
            // so its claim may not even be looked at.
            _singleInstance = null;
            _ownsInstance = false;
            return false;
        }
    }

    /// <summary>Gives the claim up, so that a copy started next can take it.</summary>
    private void ReleaseInstance()
    {
        if (_singleInstance is null)
        {
            return;
        }

        if (_ownsInstance)
        {
            try
            {
                _singleInstance.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not held by this thread after all; nothing to give up.
            }
        }

        _singleInstance.Dispose();
        _singleInstance = null;
        _ownsInstance = false;
    }

    /// <summary>
    /// The switches a restarted copy should get: the ones this copy was started
    /// with that still make sense, and the settings window open on a tab.
    /// </summary>
    private IEnumerable<string> RestartArguments(string settingsTab)
    {
        foreach (string argument in _arguments)
        {
            if (argument is "--trace" or "--second-instance")
            {
                yield return argument;
            }
        }

        yield return "--settings";
        yield return settingsTab;
    }

    /// <summary>
    /// Switches running as administrator on or off, which means restarting. The
    /// setting is left as it was when the user backs out or Windows refuses.
    /// </summary>
    private void SetRunAsAdministrator(bool value)
    {
        if (value == _settings.RunAsAdministrator)
        {
            return;
        }

        if (value == Elevation.IsElevated || Elevation.IsAlwaysElevated)
        {
            // Already running the way that is asked for, or in the only way this PC
            // runs anything; only the setting changes.
            _settings.RunAsAdministrator = value;
            SaveNow();
            AutoStart.Sync(value);
            return;
        }

        string question = value
            ? "Mullion will restart as administrator, and Windows will ask for your permission.\n\nRestart now?"
            : "Mullion will restart without administrator rights.\n\nRestart now?";
        if (Ask(question) != MessageBoxResult.OK)
        {
            return;
        }

        _settings.RunAsAdministrator = value;
        SaveNow();

        if (!value)
        {
            // The scheduled task can only be removed while Mullion still has the rights.
            AutoStart.Sync(asAdministrator: false);
        }

        ReleaseInstance();
        bool started = value ? Elevation.StartElevated(RestartArguments("general")) : Elevation.StartNormal();
        if (started)
        {
            Shutdown();
            return;
        }

        TakeInstance();
        if (value)
        {
            _settings.RunAsAdministrator = false;
            SaveNow();
            Tell("Mullion was not given permission, so it keeps running as it was.");
        }
        else
        {
            Tell("Mullion could not restart itself. It will run without administrator rights the next time it starts.");
        }
    }

    private void PushUpdateState() =>
        _settingsViewModel?.SetUpdateState(_updateStatus, _update is not null, _updateBusy);

    private void OpenUpdateSettings()
    {
        OpenSettings();
        _settingsWindow?.SelectTab("General");
    }

    /// <summary>Asks GitHub whether a newer version has been published.</summary>
    /// <param name="askedByUser">
    /// True when the user pressed the button, and so wants to hear the answer
    /// whatever it is. The daily look only speaks up when there is a new version.
    /// </param>
    private async void CheckForUpdates(bool askedByUser)
    {
        if (_updateBusy)
        {
            return;
        }

        try
        {
            _updateBusy = true;
            if (askedByUser)
            {
                _updateStatus = "Asking GitHub…";
            }

            PushUpdateState();
            var result = await _updater.CheckAsync();
            _updateBusy = false;

            if (result.Available is { } release)
            {
                _update = release;
                string version = release.Version.ToString(3);
                _updateStatus = $"Version {version} is available. You have {Updater.CurrentVersion.ToString(3)}.";

                if (!askedByUser && _announcedUpdate != release.Version)
                {
                    _announcedUpdate = release.Version;
                    _tray?.Notify($"Version {version} of Mullion is available. Click here to update.", OpenUpdateSettings);
                }
            }
            else if (result.Error is not null)
            {
                // A failed daily look is not worth a message; the next one may work.
                if (askedByUser)
                {
                    _updateStatus = result.Error;
                }
            }
            else
            {
                _update = null;
                _updateStatus = askedByUser ? "You have the latest version." : string.Empty;
            }
        }
        catch (Exception ex)
        {
            _updateBusy = false;
            Log.Write("Checking for a new version failed.", ex);
            _updateStatus = askedByUser ? "Something went wrong while asking. Details are in mullion.log." : string.Empty;
        }

        PushUpdateState();
    }

    /// <summary>Downloads the new version, puts it in place and restarts into it.</summary>
    private async void InstallUpdate()
    {
        if (_updateBusy || _update is not { } release)
        {
            return;
        }

        try
        {
            _updateBusy = true;
            _updateStatus = $"Downloading version {release.Version.ToString(3)}…";
            PushUpdateState();

            string? error = await _updater.InstallAsync(release);
            _updateBusy = false;

            if (error is not null)
            {
                _updateStatus = error;
                PushUpdateState();
                return;
            }

            SaveNow();
            ReleaseInstance();
            if (Elevation.StartSame(RestartArguments("general")))
            {
                Shutdown();
                return;
            }

            TakeInstance();
            _update = null;
            _updateStatus = "The new version is in place. Exit Mullion and start it again to use it.";
        }
        catch (Exception ex)
        {
            _updateBusy = false;
            _updateStatus = "The new version could not be installed. Details are in mullion.log.";
            Log.Write("Installing the new version failed.", ex);
        }

        PushUpdateState();
    }

    private MessageBoxResult Ask(string question) =>
        _settingsWindow is { } owner
            ? MessageBox.Show(owner, question, "Mullion", MessageBoxButton.OKCancel, MessageBoxImage.Question)
            : MessageBox.Show(question, "Mullion", MessageBoxButton.OKCancel, MessageBoxImage.Question);

    private void Tell(string message)
    {
        if (_settingsWindow is { } owner)
        {
            MessageBox.Show(owner, message, "Mullion", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(message, "Mullion", MessageBoxButton.OK, MessageBoxImage.Information);
        }
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
            StartArranging = StartArranging,
            ApplyRulesNow = ApplyRulesNow,
            SetRunAsAdministrator = SetRunAsAdministrator,
            CheckForUpdatesNow = () => CheckForUpdates(askedByUser: true),
            InstallUpdate = InstallUpdate,
            GetMonitorCount = () => Math.Max(1, Native.GetWorkAreas().Count),
            GetOpenPrograms = () => WindowList.GetArrangeable(includeExcluded: true)
                .Select(entry => WindowList.ProgramOf(entry.Handle))
                .Where(program => program != "?")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(program => program, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            GetOpenWindows = () => WindowList.GetArrangeable()
                .Select(entry => (Program: WindowList.ProgramOf(entry.Handle), entry.Title))
                .Where(window => window.Program != "?")
                .Distinct()
                .OrderBy(window => window.Program, StringComparer.OrdinalIgnoreCase)
                .ThenBy(window => window.Title, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
        viewModel.Changed += OnSettingsChanged;
        viewModel.SetTakenOverShortcuts(_takenOverShortcuts);
        _settingsViewModel = viewModel;
        PushUpdateState();

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
    /// A window was placed on grid cells with the mouse gesture. Called on a worker thread.
    /// </summary>
    private void RememberGridPlacement(nint window, GridPlacement grid)
    {
        var monitors = Native.GetWorkAreas();
        var placement = new PlacementRule { Monitor = monitors.Count == 0 ? 0 : MonitorList.IndexOf(monitors, grid.Area) + 1 };
        placement.SetArea(grid.Columns, grid.Rows, grid.Span);
        RememberPlacement(window, placement);
    }

    /// <summary>
    /// The user placed a window with Mullion: remember it, so that the program's
    /// next window opens in the same place. Called on a worker thread.
    /// </summary>
    /// <param name="window">The window that was placed.</param>
    /// <param name="placement">Where it went: a rule with only its target and monitor filled in.</param>
    private void RememberPlacement(nint window, PlacementRule placement)
    {
        if (!_settings.RememberPlacements)
        {
            return;
        }

        // Mullion's own windows are not something to arrange for the user.
        Native.GetWindowThreadProcessId(window, out uint process);
        if (process == (uint)Environment.ProcessId)
        {
            return;
        }

        string program = WindowList.ProgramOf(window);
        string title = Native.GetWindowTitle(window);
        if (program == "?" || WindowList.IsExcluded(window))
        {
            return;
        }

        // The settings belong to the user interface thread.
        Dispatcher.BeginInvoke(() =>
        {
            if (_settings.RememberPlacements && PlacementRules.Remember(_settings.Rules, program, title, placement))
            {
                Log.Trace($"remembered: {program} on monitor {placement.Monitor}");
                _settingsViewModel?.RefreshRules();
                OnSettingsChanged();
            }
        });
    }

    /// <summary>Places every open window that has a rule, then says how many were placed.</summary>
    private void ApplyRulesNow()
    {
        // Work on a copy: moving windows waits for their applications, so it runs
        // away from the user interface, which may change the settings meanwhile.
        var copy = new Settings();
        copy.CopyFrom(_settings);

        Task.Run(() =>
        {
            int placed;
            try
            {
                placed = _ruleRunner.ApplyToOpenWindows(copy);
            }
            catch (Exception ex)
            {
                Log.Write("Applying the rules to open windows failed.", ex);
                return;
            }

            Dispatcher.BeginInvoke(() =>
            {
                string text = placed switch
                {
                    0 => "No open window matches a rule that is switched on.",
                    1 => "Placed 1 window.",
                    _ => $"Placed {placed} windows.",
                };
                MessageBox.Show(text, "Mullion", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        });
    }

    /// <summary>Starts going through the open windows, or says why there is nothing to do.</summary>
    private void StartArranging()
    {
        if (_arrange?.Start() is { } reason)
        {
            MessageBox.Show(reason, "Mullion", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>
    /// A registered shortcut was pressed. Called on a worker thread with the
    /// shortcut's position: zones first, then the arrange shortcut.
    /// </summary>
    private void OnShortcutPressed(int position)
    {
        if (position == _arrangeHotkeyPosition)
        {
            // Pressing it again while arranging ends it.
            Dispatcher.BeginInvoke(() =>
            {
                if (_arrange?.IsActive == true)
                {
                    _arrange.Finish();
                }
                else
                {
                    StartArranging();
                }
            });
        }
        else if (_arrange?.IsActive == true)
        {
            // While arranging, a zone shortcut places the window being arranged.
            Dispatcher.BeginInvoke(() => _arrange?.PlaceByShortcut(position));
        }
        else
        {
            _zoneRunner.Run(position);
        }
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
        WindowList.SetExcludedPrograms(_settings.ExcludedPrograms);

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
                Paused: _paused,
                GridButton: _settings.GridButton);

            _input.CancelKey = _settings.CancelKeyCode;

            // Registering shortcuts is comparatively slow and this runs for every
            // slider movement, so only do it when the shortcuts actually changed.
            // The arrange shortcut is registered after the zones' shortcuts.
            var hotkeys = _settings.Zones.Select(zone => zone.Hotkey).ToList();
            _arrangeHotkeyPosition = hotkeys.Count;
            hotkeys.Add(_settings.ArrangeHotkey);
            if (!hotkeys.SequenceEqual(_appliedHotkeys))
            {
                _appliedHotkeys = hotkeys;
                _input.SetZoneHotkeys(hotkeys);
            }

            _input.SetHotkeysSuspended(_paused || _recordingShortcut);
        }

        _zoneRunner.Update(_settings);

        // Watching every window that opens costs a little, so it is on only while
        // there are rules to apply.
        _ruleRunner.Update(_settings, _paused);
        if (_input is not null && _watchingWindows != _ruleRunner.IsWatching)
        {
            _watchingWindows = _ruleRunner.IsWatching;
            _input.SetWindowWatch(_watchingWindows);
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
