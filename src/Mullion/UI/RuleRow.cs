using Mullion.Core;

namespace Mullion.UI;

/// <summary>One line of the rule list in the settings window.</summary>
internal sealed class RuleRow
{
    private readonly Action _changed;

    /// <param name="rule">The rule shown.</param>
    /// <param name="target">Where it sends windows, or null when it names a zone that was deleted.</param>
    /// <param name="changed">Called after the rule was switched on or off.</param>
    public RuleRow(PlacementRule rule, Zone? target, Action changed)
    {
        Rule = rule;
        _changed = changed;

        ZoneMissing = target is null;
        string monitor = rule.Monitor == 0 ? "on the monitor it opens on" : $"on monitor {rule.Monitor}";

        if (target is null)
        {
            Target = "Goes to a zone that was deleted; edit the rule to choose another.";
        }
        else if (rule.HasArea)
        {
            var span = target.Span;
            string area = span.Columns == target.Columns && span.Rows == target.Rows
                ? "the whole screen"
                : $"{span.Columns} × {span.Rows} of {target.Columns} × {target.Rows} cells";
            Target = $"Goes to {area}, {monitor}";
        }
        else
        {
            string name = string.IsNullOrWhiteSpace(target.Name) ? "an unnamed zone" : target.Name;
            Target = $"Goes to {name}, {monitor}";
        }

        // A small picture of the area, like the one in the zone list.
        Columns = target?.Columns ?? 1;
        Rows = target?.Rows ?? 1;
        Span = target?.Span ?? default;
    }

    public PlacementRule Rule { get; }

    public string Program => Rule.Program;

    public string Condition
    {
        get
        {
            if (Rule.Title.Length > 0)
            {
                return $"The window titled “{Rule.Title}”";
            }

            if (Rule.TitleContains.Length > 0)
            {
                return $"Windows whose title contains “{Rule.TitleContains}”";
            }

            return Rule.Learned ? "Any other window of this program" : "Every window";
        }
    }

    public string Target { get; }

    /// <summary>True when the rule's zone no longer exists, so the rule does nothing.</summary>
    public bool ZoneMissing { get; }

    /// <summary>True for a placement Mullion remembered by itself rather than a rule the user wrote.</summary>
    public bool IsRemembered => Rule.Learned;

    public int Columns { get; }
    public int Rows { get; }
    public CellSpan Span { get; }

    public bool Enabled
    {
        get => Rule.Enabled;
        set
        {
            if (Rule.Enabled != value)
            {
                Rule.Enabled = value;
                _changed();
            }
        }
    }
}
