// STUB - compile-only stand-in. Owner: Reuben. Delete when the real class lands.
namespace Dms.Domain;

/// <summary>Placeholder parcel until Reuben's class lands.</summary>
public class Parcel
{
    /// <summary>Creates a parcel from a label and size band.</summary>
    /// <param name="label">Display label.</param>
    /// <param name="sizeBand">Size band used by allocation rules.</param>
    public Parcel(string label, string sizeBand)
    {
        Label = label;
        SizeBand = sizeBand;
    }

    /// <summary>Display label.</summary>
    public string Label { get; }

    /// <summary>Size band.</summary>
    public string SizeBand { get; }

    /// <summary>Set once the parcel is checked off at pickup.</summary>
    public bool IsCollected { get; private set; }

    /// <summary>Marks the parcel as collected.</summary>
    public void MarkCollected() => IsCollected = true;
}
