namespace MarketPos.Models;

public enum DatePreset
{
    Today,
    Yesterday,
    ThisWeek,
    ThisMonth,
    ThisYear,
    Custom,
}

/// <summary>
/// A half-open window [From, To) over local time. Half-open on purpose: a sale at 23:59:59.7
/// on the last day belongs to that day, and "&lt;= end of day" is the classic way to lose it.
/// </summary>
public readonly record struct DateRange(DateTime From, DateTime To, DatePreset Preset, string Label)
{
    public static DateRange For(DatePreset preset, DateTime? customFrom = null, DateTime? customTo = null)
    {
        var today = DateTime.Today;
        return preset switch
        {
            DatePreset.Today => new DateRange(today, today.AddDays(1), preset, "Today"),
            DatePreset.Yesterday => new DateRange(today.AddDays(-1), today, preset, "Yesterday"),
            DatePreset.ThisWeek => Week(today),
            DatePreset.ThisMonth => new DateRange(
                new DateTime(today.Year, today.Month, 1),
                new DateTime(today.Year, today.Month, 1).AddMonths(1),
                preset, "This month"),
            DatePreset.ThisYear => new DateRange(
                new DateTime(today.Year, 1, 1), new DateTime(today.Year + 1, 1, 1), preset, "This year"),
            _ => Custom(customFrom ?? today, customTo ?? today),
        };
    }

    /// <summary>Monday-start week, which is how Moroccan trading weeks are counted.</summary>
    private static DateRange Week(DateTime today)
    {
        var offset = ((int)today.DayOfWeek + 6) % 7;   // Monday = 0
        var monday = today.AddDays(-offset);
        return new DateRange(monday, monday.AddDays(7), DatePreset.ThisWeek, "This week");
    }

    /// <summary>Both bounds are inclusive dates as the owner picked them; To is pushed to the next midnight.</summary>
    public static DateRange Custom(DateTime from, DateTime to)
    {
        if (to < from) (from, to) = (to, from);
        return new DateRange(from.Date, to.Date.AddDays(1), DatePreset.Custom,
                             from.Date == to.Date
                                 ? from.ToString("d MMM yyyy")
                                 : $"{from:d MMM} – {to:d MMM yyyy}");
    }

    /// <summary>The same length of time immediately before this one, for "vs previous" figures.</summary>
    public DateRange Previous()
    {
        var span = To - From;
        return new DateRange(From - span, From, Preset, "Previous " + Label.ToLowerInvariant());
    }

    public int Days => Math.Max(1, (int)(To - From).TotalDays);

    /// <summary>Whether a day-by-day chart makes sense, or the range should be bucketed by month.</summary>
    public bool ByMonth => Days > 62;
}
