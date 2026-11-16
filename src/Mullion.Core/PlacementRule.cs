namespace Mullion.Core;

/// <summary>
/// "When a window of this program opens, put it in this zone." A rule matches by
/// program name and, optionally, by text in the window's title.
/// </summary>
public sealed class PlacementRule
{
    public const int MaxTextLength = 120;
    public const int MaxTitleLength = 260;
    public const int MaxMonitor = 16;

    /// <summary>The program's name as Task Manager shows it, without ".exe"; for example "notepad++".</summary>
    public string Program { get; set; } = string.Empty;

    /// <summary>Text the window title must contain; empty to match every window of the program.</summary>
    public string TitleContains { get; set; } = string.Empty;

    /// <summary>
    /// The whole window title, for a placement Mullion remembered for one particular
    /// window. Unlike <see cref="TitleContains"/> the title must be exactly this.
    /// Empty for a placement remembered for the program as a whole, and for rules
    /// the user wrote.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The <see cref="Zone.Id"/> of the zone to place the window in; empty when the
    /// rule carries an area of its own instead (see <see cref="HasArea"/>).
    /// </summary>
    public string ZoneId { get; set; } = string.Empty;

    /// <summary>
    /// True for a placement Mullion remembered by itself when the user placed a
    /// window, as opposed to a rule the user wrote. A remembered placement is
    /// updated each time that program's window is placed again, and gives way to
    /// any rule the user wrote.
    /// </summary>
    public bool Learned { get; set; }

    // An area of the rule's own, used when no zone is named: a block of cells on
    // a grid, exactly as a zone describes one. This is how a placement made with
    // the grid gesture is remembered.
    public int Columns { get; set; }
    public int Rows { get; set; }
    public int Column0 { get; set; }
    public int Row0 { get; set; }
    public int Column1 { get; set; }
    public int Row1 { get; set; }

    /// <summary>True when the rule names no zone but carries an area of its own.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasArea => ZoneId.Length == 0 && Columns >= 1 && Rows >= 1;

    /// <summary>The rule's own area as a zone, for working out where it is on a monitor.</summary>
    public Zone AreaAsZone()
    {
        var zone = new Zone
        {
            Id = "area",
            Columns = Columns,
            Rows = Rows,
            Column0 = Column0,
            Row0 = Row0,
            Column1 = Column1,
            Row1 = Row1,
        };
        zone.Normalize();
        return zone;
    }

    /// <summary>Gives the rule an area of its own in place of a zone.</summary>
    public void SetArea(int columns, int rows, CellSpan span)
    {
        ZoneId = string.Empty;
        Columns = columns;
        Rows = rows;
        Column0 = span.Column0;
        Row0 = span.Row0;
        Column1 = span.Column1;
        Row1 = span.Row1;
    }

    /// <summary>Points the rule at a zone, dropping any area of its own.</summary>
    public void SetZone(string zoneId)
    {
        ZoneId = zoneId;
        Columns = Rows = Column0 = Row0 = Column1 = Row1 = 0;
    }

    /// <summary>
    /// 0 to use the monitor the window opens on; otherwise the number of the monitor
    /// to move it to, counting from 1, left to right.
    /// </summary>
    public int Monitor { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Forces every value into its valid range.</summary>
    public void Normalize()
    {
        Program = PlacementRules.NormalizeProgram(Program);
        TitleContains = Limit((TitleContains ?? string.Empty).Trim());

        // Only a remembered placement is tied to one exact title.
        Title = Learned ? PlacementRules.NormalizeTitle(Title) : string.Empty;
        if (Title.Length > 0)
        {
            TitleContains = string.Empty;
        }

        ZoneId = (ZoneId ?? string.Empty).Trim();
        Monitor = Math.Clamp(Monitor, 0, MaxMonitor);

        if (ZoneId.Length > 0 || Columns < 1 || Rows < 1)
        {
            // A zone is named, or there is no usable area: keep no half-filled area.
            Columns = Rows = Column0 = Row0 = Column1 = Row1 = 0;
        }
        else
        {
            var area = AreaAsZone();
            Columns = area.Columns;
            Rows = area.Rows;
            Column0 = area.Column0;
            Row0 = area.Row0;
            Column1 = area.Column1;
            Row1 = area.Row1;
        }
    }

    /// <summary>True when this rule is switched on and applies to a window of the given program and title.</summary>
    public bool Matches(string program, string title)
    {
        if (!Enabled || Program.Length == 0)
        {
            return false;
        }

        if (!string.Equals(Program, PlacementRules.NormalizeProgram(program), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Title.Length > 0)
        {
            return string.Equals(Title, PlacementRules.NormalizeTitle(title), StringComparison.OrdinalIgnoreCase);
        }

        return TitleContains.Length == 0
            || (title ?? string.Empty).Contains(TitleContains, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// How strongly the rule should be preferred when several match: a rule the user
    /// wrote first, then a placement remembered for this exact title, then one
    /// remembered for the program.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Rank => !Learned ? 2 : Title.Length > 0 ? 1 : 0;

    public PlacementRule Clone() => (PlacementRule)MemberwiseClone();

    private static string Limit(string text) => text.Length > MaxTextLength ? text[..MaxTextLength] : text;
}

/// <summary>Helpers for choosing and applying placement rules.</summary>
public static class PlacementRules
{
    /// <summary>Trims a program name and drops a trailing ".exe", so "Notepad++.exe " and "notepad++" are the same.</summary>
    public static string NormalizeProgram(string? program)
    {
        string name = (program ?? string.Empty).Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4].TrimEnd();
        }

        return name.Length > PlacementRule.MaxTextLength ? name[..PlacementRule.MaxTextLength] : name;
    }

    /// <summary>The most remembered titles kept for one program; the least recently placed go first.</summary>
    public const int MaxRememberedTitles = 20;

    /// <summary>Trims a window title and limits its length, so titles compare the same way everywhere.</summary>
    public static string NormalizeTitle(string? title)
    {
        string text = (title ?? string.Empty).Trim();
        return text.Length > PlacementRule.MaxTitleLength ? text[..PlacementRule.MaxTitleLength] : text;
    }

    /// <summary>
    /// The rule to use for a window, or null.
    ///
    /// A rule the user wrote always beats a placement Mullion remembered. Among
    /// remembered placements, one for this exact window title beats the one for the
    /// program as a whole, so a window that was placed before returns to its own
    /// spot and a new window goes where the program's last window was put. Among
    /// written rules, the one that asks for the most title text wins; between
    /// equals, the earlier rule wins.
    /// </summary>
    public static PlacementRule? Find(IEnumerable<PlacementRule> rules, string program, string title)
    {
        PlacementRule? best = null;
        foreach (var rule in rules)
        {
            if (!rule.Matches(program, title))
            {
                continue;
            }

            bool better = best is null
                || rule.Rank > best.Rank
                || (rule.Rank == best.Rank && rule.TitleContains.Length > best.TitleContains.Length);
            if (better)
            {
                best = rule;
            }
        }

        return best;
    }

    /// <summary>
    /// The zone a rule sends windows to: the zone it names, or its own area. Null
    /// when it names a zone that no longer exists.
    /// </summary>
    public static Zone? TargetOf(PlacementRule rule, Func<string, Zone?> findZone) =>
        rule.HasArea ? rule.AreaAsZone() : findZone(rule.ZoneId);

    /// <summary>
    /// Remembers where the user just placed a window, so the program's next window
    /// opens there. Returns true when <paramref name="rules"/> was changed.
    ///
    /// Nothing is remembered when a rule the user wrote already covers the window:
    /// that rule decides, and stays exactly as written. Otherwise the placement is
    /// remembered twice: for this exact window title, and for the program as a
    /// whole. The first brings this window back to its own spot; the second gives
    /// windows of the program that were never placed somewhere sensible to open.
    /// </summary>
    /// <param name="rules">The rule list to update.</param>
    /// <param name="program">The program that owns the window.</param>
    /// <param name="title">The window's title.</param>
    /// <param name="placement">Where the window was placed: a rule with only its target and monitor filled in.</param>
    public static bool Remember(List<PlacementRule> rules, string program, string title, PlacementRule placement)
    {
        program = NormalizeProgram(program);
        title = NormalizeTitle(title);
        placement = placement.Clone();
        placement.Normalize();
        if (program.Length == 0 || (placement.ZoneId.Length == 0 && !placement.HasArea))
        {
            return false;
        }

        if (rules.Any(rule => !rule.Learned && rule.Matches(program, title)))
        {
            return false;
        }

        bool changed = false;

        if (title.Length > 0)
        {
            changed |= RememberFor(rules, program, title, placement);
            changed |= TrimRememberedTitles(rules, program);
        }

        changed |= RememberFor(rules, program, string.Empty, placement);
        return changed;
    }

    /// <summary>Updates or adds the remembered placement for a program and exact title (empty for the whole program).</summary>
    private static bool RememberFor(List<PlacementRule> rules, string program, string title, PlacementRule placement)
    {
        var remembered = rules.FirstOrDefault(rule =>
            rule.Learned
            && string.Equals(rule.Program, program, StringComparison.OrdinalIgnoreCase)
            && string.Equals(rule.Title, title, StringComparison.OrdinalIgnoreCase));

        if (remembered is not null && remembered.Enabled && SameTarget(remembered, placement))
        {
            return false;
        }

        if (remembered is not null && title.Length > 0)
        {
            // Move it to the end, so the titles placed longest ago are the first to go.
            rules.Remove(remembered);
            rules.Add(remembered);
        }

        if (remembered is null)
        {
            remembered = new PlacementRule { Program = program, Learned = true, Title = title };
            rules.Add(remembered);
        }

        remembered.TitleContains = string.Empty;
        remembered.Enabled = true;
        remembered.Monitor = placement.Monitor;
        if (placement.HasArea)
        {
            remembered.SetArea(placement.Columns, placement.Rows, new CellSpan(placement.Column0, placement.Row0, placement.Column1, placement.Row1));
        }
        else
        {
            remembered.SetZone(placement.ZoneId);
        }

        remembered.Normalize();
        return true;
    }

    /// <summary>Keeps only the most recently placed titles of a program. Returns true when any were dropped.</summary>
    private static bool TrimRememberedTitles(List<PlacementRule> rules, string program)
    {
        var titles = rules
            .Where(rule => rule.Learned && rule.Title.Length > 0
                && string.Equals(rule.Program, program, StringComparison.OrdinalIgnoreCase))
            .ToList();

        int excess = titles.Count - MaxRememberedTitles;
        for (int i = 0; i < excess; i++)
        {
            rules.Remove(titles[i]);
        }

        return excess > 0;
    }

    private static bool SameTarget(PlacementRule a, PlacementRule b) =>
        a.Monitor == b.Monitor
        && a.ZoneId == b.ZoneId
        && a.Columns == b.Columns && a.Rows == b.Rows
        && a.Column0 == b.Column0 && a.Row0 == b.Row0
        && a.Column1 == b.Column1 && a.Row1 == b.Row1;

    /// <summary>
    /// The position, counting from 0, of the monitor a rule sends a window to.
    /// A rule that names a monitor that is not connected falls back to the monitor
    /// the window opened on.
    /// </summary>
    public static int ResolveMonitor(PlacementRule rule, int monitorCount, int openedOn)
    {
        if (monitorCount <= 0)
        {
            return 0;
        }

        return rule.Monitor >= 1 && rule.Monitor <= monitorCount
            ? rule.Monitor - 1
            : Math.Clamp(openedOn, 0, monitorCount - 1);
    }
}
