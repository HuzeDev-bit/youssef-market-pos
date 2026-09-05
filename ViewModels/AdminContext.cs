using MarketPos.Models;

namespace MarketPos.ViewModels;

/// <summary>
/// The state the whole back office shares: which date window the owner is looking at.
///
/// One instance lives on the shell and is handed to every page, so changing the range on the
/// dashboard changes it on Reports and Expenses too. The owner picks a period once and the
/// whole office is talking about the same days.
/// </summary>
public sealed class AdminContext : ViewModelBase
{
    private DateRange _range = DateRange.For(DatePreset.Today);
    private DateTime _customFrom = DateTime.Today.AddDays(-6);
    private DateTime _customTo = DateTime.Today;

    public event EventHandler? RangeChanged;

    public DateRange Range
    {
        get => _range;
        private set
        {
            _range = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RangeLabel));
            foreach (var name in PresetNames) OnPropertyChanged(name);
            RangeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string RangeLabel => Range.Label;

    public DateTime CustomFrom
    {
        get => _customFrom;
        set { if (SetField(ref _customFrom, value) && IsCustom) ApplyCustom(); }
    }

    public DateTime CustomTo
    {
        get => _customTo;
        set { if (SetField(ref _customTo, value) && IsCustom) ApplyCustom(); }
    }

    public void Use(DatePreset preset)
    {
        Range = preset == DatePreset.Custom
            ? DateRange.Custom(CustomFrom, CustomTo)
            : DateRange.For(preset);
    }

    private void ApplyCustom() => Range = DateRange.Custom(CustomFrom, CustomTo);

    // One bool per preset, so the chips can bind straight to IsChecked without a converter.
    public bool IsToday => Range.Preset == DatePreset.Today;
    public bool IsYesterday => Range.Preset == DatePreset.Yesterday;
    public bool IsThisWeek => Range.Preset == DatePreset.ThisWeek;
    public bool IsThisMonth => Range.Preset == DatePreset.ThisMonth;
    public bool IsThisYear => Range.Preset == DatePreset.ThisYear;
    public bool IsCustom => Range.Preset == DatePreset.Custom;

    private static readonly string[] PresetNames =
        [nameof(IsToday), nameof(IsYesterday), nameof(IsThisWeek),
         nameof(IsThisMonth), nameof(IsThisYear), nameof(IsCustom)];
}
