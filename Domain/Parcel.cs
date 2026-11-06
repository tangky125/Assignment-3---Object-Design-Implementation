namespace Dms.Domain;

/// <summary>
/// One parcel in a delivery request (A2 CRC: Parcel). Holds its declared size band (A1 Task 1
/// subtask 4: sizes, not exact weights), handling needs and a short description, and is ticked off
/// at pickup by Area 4.
/// </summary>
public class Parcel
{
    /// <summary>The size bands a customer can declare.</summary>
    public static readonly IReadOnlyList<string> SizeBands = new[] { "Small", "Medium", "Large" };

    /// <summary>Longest description allowed.</summary>
    public const int MaxDescription = 40;

    /// <summary>Creates a parcel from a label and size band.</summary>
    /// <param name="label">Display label, e.g. P1.</param>
    /// <param name="sizeBand">Small, Medium or Large.</param>
    public Parcel(string label, string sizeBand)
        : this(label, sizeBand, Array.Empty<string>(), null)
    {
    }

    /// <summary>Creates a parcel with handling needs and an optional description.</summary>
    public Parcel(string label, string sizeBand, IEnumerable<string> handling, string? description)
    {
        string? band = SizeBands.FirstOrDefault(b => b.Equals(sizeBand?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (band is null)
        {
            throw new ArgumentException("Choose Small, Medium or Large.", nameof(sizeBand));
        }

        string? text = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (text is { Length: > MaxDescription })
        {
            throw new ArgumentException($"A parcel description can be at most {MaxDescription} characters.", nameof(description));
        }

        Label = label;
        SizeBand = band;
        Handling = handling.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Description = text;
    }

    /// <summary>Display label.</summary>
    public string Label { get; }

    /// <summary>Size band.</summary>
    public string SizeBand { get; }

    /// <summary>Handling needs, e.g. Fragile, SignatureRequired, KeepUpright.</summary>
    public IReadOnlyList<string> Handling { get; }

    /// <summary>Optional short description, e.g. "documents".</summary>
    public string? Description { get; }

    /// <summary>Set once the parcel is checked off at pickup.</summary>
    public bool IsCollected { get; private set; }

    /// <summary>Marks the parcel as collected.</summary>
    public void MarkCollected() => IsCollected = true;
}
