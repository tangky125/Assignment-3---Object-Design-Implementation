namespace Dms.Domain;

/// <summary>A parcel as entered on the form, before it becomes a <see cref="Parcel"/>.</summary>
public sealed record ParcelEntry(string SizeBand, IReadOnlyList<string> Handling, string? Description);

/// <summary>Everything a customer or office staff member enters for a new request (A1 Task 1).</summary>
public sealed record RequestForm(
    string CustomerId,
    Address Pickup,
    Contact PickupContact,
    DateTime ReadyTime,
    Address Destination,
    Contact Recipient,
    IReadOnlyList<ParcelEntry> Parcels,
    string? Notes);

/// <summary>
/// Logs delivery requests (A2 CRC: RequestRegister). Applies the business rules for a new request,
/// issues the next reference, and hands back the new <see cref="DeliveryRequest"/>. The reference is
/// issued only when every rule passes, so an abandoned or refused request leaves no gap (variant 6a).
/// Time is supplied by the caller; the register never reads the clock.
/// </summary>
public sealed class RequestRegister
{
    /// <summary>Most parcels in one request.</summary>
    public const int MaxParcels = 10;

    /// <summary>Furthest ahead a pickup can be booked.</summary>
    public static readonly TimeSpan BookingHorizon = TimeSpan.FromDays(14);

    /// <summary>Longest note for the office.</summary>
    public const int MaxNotes = 200;

    /// <summary>Lowest and highest pickup postcode in the Greater Melbourne service area.</summary>
    public const int FirstPostcode = 3000, LastPostcode = 3207;

    private readonly CustomerDirectory _directory;

    /// <summary>Creates the register over the customer directory.</summary>
    /// <param name="directory">Customers a request can be for.</param>
    /// <param name="nextNumber">Next reference number to issue.</param>
    public RequestRegister(CustomerDirectory directory, int nextNumber)
    {
        _directory = directory;
        NextNumber = Math.Max(nextNumber, 1);
    }

    /// <summary>The next reference number this register will issue.</summary>
    public int NextNumber { get; private set; }

    /// <summary>True when parcels can be collected from this postcode.</summary>
    public static bool InServiceArea(string postcode) =>
        postcode.Length == 4 && int.TryParse(postcode, out int pc) && pc is >= FirstPostcode and <= LastPostcode;

    /// <summary>Checks the form and, if every rule passes, logs the request under a new reference.</summary>
    /// <param name="form">What was entered.</param>
    /// <param name="now">Current time, supplied by the caller.</param>
    /// <param name="loggedBy">Display name of the customer or staff member logging it.</param>
    public Outcome<DeliveryRequest> Log(RequestForm form, DateTime now, string loggedBy)
    {
        string? refusal = Check(form, now);
        if (refusal is not null)
        {
            return Outcome<DeliveryRequest>.Refused(refusal);
        }

        string reference = $"DMS-{now.Year}-{NextNumber:0000}";
        NextNumber++;
        string? notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim();
        var request = new DeliveryRequest(reference, form.CustomerId, form.Pickup, form.PickupContact, form.ReadyTime,
            form.Destination, form.Recipient, notes, now, loggedBy);
        int n = 1;
        foreach (ParcelEntry p in form.Parcels)
        {
            request.Parcels.Add(new Parcel($"P{n++}", p.SizeBand, p.Handling, p.Description));
        }

        return Outcome<DeliveryRequest>.Ok(request, $"Request {reference} logged.");
    }

    /// <summary>The first business rule the form breaks, or null when it passes them all.</summary>
    public string? Check(RequestForm form, DateTime now)
    {
        if (_directory.Get(form.CustomerId) is null)
        {
            return "Choose or register the customer before logging the request.";
        }

        if (!InServiceArea(form.Pickup.Postcode))
        {
            return $"Postcode {form.Pickup.Postcode} is outside our Greater Melbourne service area. Parcels can only be collected inside it (postcodes {FirstPostcode}-{LastPostcode}).";
        }

        if (form.Parcels.Count == 0)
        {
            return "A request needs at least one parcel.";
        }

        if (form.Parcels.Count > MaxParcels)
        {
            return $"A request can hold at most {MaxParcels} parcels. Log a second request for the rest.";
        }

        if (form.Parcels.Any(p => !Parcel.SizeBands.Contains(p.SizeBand, StringComparer.OrdinalIgnoreCase)))
        {
            return "Choose Small, Medium or Large for every parcel.";
        }

        if (form.Parcels.Any(p => p.Description is { } d && d.Trim().Length > Parcel.MaxDescription))
        {
            return $"A parcel description can be at most {Parcel.MaxDescription} characters.";
        }

        if (form.ReadyTime < now)
        {
            return "The ready time has already passed. Change the pickup ready time.";
        }

        if (form.ReadyTime > now + BookingHorizon)
        {
            return "Pickups can be booked at most 14 days ahead. Choose an earlier ready time.";
        }

        if (form.Notes is { } notes && notes.Trim().Length > MaxNotes)
        {
            return $"Notes can be at most {MaxNotes} characters.";
        }

        return null;
    }
}
