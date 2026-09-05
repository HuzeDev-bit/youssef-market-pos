namespace MarketPos.Views.Admin;

/// <summary>Which way a number moved, and whether that is good news.</summary>
public enum TileDirection
{
    Flat,
    Up,
    Down,
    Bad,
}

/// <summary>
/// One headline number, as the back-office pages present it: a label, the figure, and a
/// line underneath saying what it is measured against.
/// </summary>
public sealed class StatTile
{
    public required string Label { get; init; }
    public required string Value { get; init; }
    public string Note { get; init; } = string.Empty;
    public TileDirection Direction { get; init; } = TileDirection.Flat;

    /// <summary>Whether to draw the up/down arrow beside the note.</summary>
    public bool HasDelta => Direction is TileDirection.Up or TileDirection.Down;
}
