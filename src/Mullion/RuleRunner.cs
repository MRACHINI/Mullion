using Mullion.Core;
using Mullion.Interop;

namespace Mullion;

/// <summary>
/// Applies placement rules: when a window opens, finds the rule for its program
/// and title and puts the window in that rule's zone.
/// </summary>
internal sealed class RuleRunner
{
    /// <summary>
    /// How long to wait after a window appears before looking at it. Programs often
    /// show a window first and only then give it its title and its own position.
    /// </summary>
    private static readonly TimeSpan[] Delays = [TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(1200)];

    /// <summary>The usable rules, each with the zone (or own area) it sends windows to.</summary>
    private sealed record Snapshot(
        IReadOnlyList<PlacementRule> Rules,
        IReadOnlyDictionary<PlacementRule, Zone> Targets,
        int WindowGap,
        int EdgeGap,
        bool Enabled);

    private readonly object _gate = new();
    private readonly HashSet<nint> _handled = [];
    private volatile Snapshot _snapshot = new([], new Dictionary<PlacementRule, Zone>(), 0, 0, false);

    /// <summary>True when there is at least one usable rule and applying them on open is switched on.</summary>
    public bool IsWatching => _snapshot.Enabled;

    /// <summary>Copies the rules that are switched on and have somewhere to send windows.</summary>
    private static Snapshot Capture(Settings settings, bool enabled)
    {
        var rules = new List<PlacementRule>();
        var targets = new Dictionary<PlacementRule, Zone>(ReferenceEqualityComparer.Instance);

        foreach (var rule in settings.Rules)
        {
            if (rule.Enabled && PlacementRules.TargetOf(rule, settings.FindZone) is { } zone)
            {
                var copy = rule.Clone();
                rules.Add(copy);
                targets[copy] = zone.Clone();
            }
        }

        return new Snapshot(rules, targets, settings.WindowGap, settings.EdgeGap, enabled && rules.Count > 0);
    }

    /// <summary>Takes a copy of the rules, zones and margins. Safe to call from the user interface.</summary>
    public void Update(Settings settings, bool paused)
    {
        bool wasWatching = _snapshot.Enabled;
        var snapshot = Capture(settings, !paused && settings.ApplyRulesOnOpen);
        bool enabled = snapshot.Enabled;
        _snapshot = snapshot;

        // Windows that are already open when watching starts have not "just opened".
        if (enabled && !wasWatching)
        {
            lock (_gate)
            {
                _handled.Clear();
                foreach (var entry in WindowList.GetArrangeable())
                {
                    _handled.Add(entry.Handle);
                }
            }
        }
    }

    /// <summary>
    /// A top-level window became visible. Called for every such window on the
    /// system, on the input thread, so it only notes the window and returns.
    /// </summary>
    public void OnWindowShown(nint window)
    {
        if (!_snapshot.Enabled)
        {
            return;
        }

        lock (_gate)
        {
            // A window is handled once: showing it again later (from the tray, say)
            // must not undo where the user has put it since.
            if (!_handled.Add(window))
            {
                return;
            }
        }

        Task.Run(() => HandleNewWindowAsync(window));
    }

    /// <summary>Applies the rules to every open window now. Returns how many windows were placed.</summary>
    public int ApplyToOpenWindows(Settings settings)
    {
        var snapshot = Capture(settings, enabled: true);
        if (snapshot.Rules.Count == 0)
        {
            return 0;
        }

        int placed = 0;
        foreach (var entry in WindowList.GetArrangeable())
        {
            if (TryPlace(entry.Handle, snapshot))
            {
                placed++;
            }
        }

        return placed;
    }

    private async Task HandleNewWindowAsync(nint window)
    {
        try
        {
            bool arrangeable = false;
            foreach (var delay in Delays)
            {
                await Task.Delay(delay).ConfigureAwait(false);

                if (!Native.IsWindow(window))
                {
                    break;
                }

                if (WindowList.IsStillArrangeable(window))
                {
                    arrangeable = true;
                    var snapshot = _snapshot;
                    if (snapshot.Enabled)
                    {
                        var used = Place(window, snapshot, alreadyUsed: null);
                        await LookAgainForTitleAsync(window, used).ConfigureAwait(false);
                    }

                    break;
                }
            }

            if (!arrangeable)
            {
                // A tooltip, menu or other passing window. Forget it, so that a real
                // window that later gets the same handle is not ignored.
                lock (_gate)
                {
                    _handled.Remove(window);
                }
            }

            Prune();
        }
        catch (Exception ex)
        {
            Log.Write("Applying a placement rule failed.", ex);
        }
    }

    /// <summary>
    /// Many programs show a window first and give it its real title a moment later.
    /// If the window was placed only by its program's remembered placement (or by
    /// nothing), look once more: by then its title may match a placement remembered
    /// for that exact window, which should win.
    /// </summary>
    private async Task LookAgainForTitleAsync(nint window, PlacementRule? used)
    {
        // A written rule, or a placement for this exact title, is already the best there is.
        if (used is { Rank: > 0 })
        {
            return;
        }

        // Nothing to wait for unless some rule depends on the title.
        if (!_snapshot.Rules.Any(rule => rule.Rank == 1 || rule.TitleContains.Length > 0))
        {
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(1500)).ConfigureAwait(false);

        var snapshot = _snapshot;
        if (snapshot.Enabled && WindowList.IsStillArrangeable(window))
        {
            Place(window, snapshot, alreadyUsed: used);
        }
    }

    private static bool TryPlace(nint window, Snapshot snapshot) => Place(window, snapshot, alreadyUsed: null) is not null;

    /// <summary>
    /// Places a window by the rule that fits it best and returns that rule; null
    /// when no rule fits. Does nothing when the best rule is <paramref name="alreadyUsed"/>
    /// or no better than it.
    /// </summary>
    private static PlacementRule? Place(nint window, Snapshot snapshot, PlacementRule? alreadyUsed)
    {
        string program = WindowList.ProgramOf(window);
        string title = Native.GetWindowTitle(window);

        var rule = PlacementRules.Find(snapshot.Rules, program, title);
        if (rule is null || !snapshot.Targets.TryGetValue(rule, out var zone))
        {
            return null;
        }

        if (alreadyUsed is not null && rule.Rank <= alreadyUsed.Rank)
        {
            return null;
        }

        var monitors = Native.GetWorkAreas();
        if (monitors.Count == 0)
        {
            return null;
        }

        var frame = Native.GetFrameBounds(window);
        int openedOn = frame is { } bounds ? MonitorList.IndexOf(monitors, bounds) : 0;
        int monitor = PlacementRules.ResolveMonitor(rule, monitors.Count, openedOn);
        var target = zone.BoundsOn(monitors[monitor], snapshot.WindowGap, snapshot.EdgeGap);

        if (Log.TraceEnabled)
        {
            string kind = rule.Rank switch
            {
                2 => "written rule",
                1 => "remembered for this title",
                _ => "remembered for the program",
            };
            Log.Trace($"rule: {program} to {(rule.HasArea ? "its own area" : "zone " + zone.Id)} on monitor {monitor + 1} ({kind}): {target}");
        }

        WindowPlacer.Place(window, target, keepSize: false);
        return rule;
    }

    /// <summary>Drops closed windows from the list, so it cannot grow without end.</summary>
    private void Prune()
    {
        lock (_gate)
        {
            if (_handled.Count > 256)
            {
                _handled.RemoveWhere(window => !Native.IsWindow(window));
            }
        }
    }
}
