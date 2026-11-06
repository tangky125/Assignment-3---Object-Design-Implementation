namespace Dms.Domain;

/// <summary>A street address in Victoria.</summary>
public sealed record Address(string Street, string Suburb, string Postcode);

/// <summary>A named person and their phone number.</summary>
public sealed record Contact(string Name, string Phone);

/// <summary>
/// A customer's request to collect parcels and deliver them (A2 CRC: DeliveryRequest). Knows who
/// it is for, where it is collected and delivered, when the parcels are ready, and its parcels.
/// It is born in <see cref="Status.Requested"/> (A2 birth state); later moves belong to quoting,
/// allocation and delivery, and go through <see cref="StatusLifecycle"/>.
/// Quote negotiation was moved to <c>QuoteNegotiator</c> (Area 2) as the A2 hand-over advised.
/// </summary>
public class DeliveryRequest
{
    /// <summary>Creates a request shell from its reference (used when other areas rebuild a request).</summary>
    /// <param name="reference">Client-facing reference code.</param>
    public DeliveryRequest(string reference) => Reference = reference;

    /// <summary>Creates a request shell that knows its customer (used for access checks on stored requests).</summary>
    /// <param name="reference">Client-facing reference code.</param>
    /// <param name="customerId">Directory id of the customer who placed it.</param>
    public DeliveryRequest(string reference, string customerId)
        : this(reference) => CustomerId = customerId;

    internal DeliveryRequest(string reference, string customerId, Address pickup, Contact pickupContact,
        DateTime readyTime, Address destination, Contact recipient, string? notes, DateTime loggedAt, string loggedBy)
        : this(reference, customerId)
    {
        Pickup = pickup;
        PickupContact = pickupContact;
        ReadyTime = readyTime;
        Destination = destination;
        Recipient = recipient;
        Notes = notes;
        LoggedAt = loggedAt;
        LoggedBy = loggedBy;
    }

    /// <summary>Client-facing reference code, e.g. DMS-2026-0010.</summary>
    public string Reference { get; }

    /// <summary>Status at birth; this class never moves it.</summary>
    public Status Status { get; } = Status.Requested;

    /// <summary>Directory id of the customer the request is for.</summary>
    public string? CustomerId { get; }

    /// <summary>Where the parcels are collected.</summary>
    public Address? Pickup { get; }

    /// <summary>Who hands the parcels over.</summary>
    public Contact? PickupContact { get; }

    /// <summary>When the parcels are ready to collect.</summary>
    public DateTime ReadyTime { get; }

    /// <summary>Where the parcels are going.</summary>
    public Address? Destination { get; }

    /// <summary>Who receives the parcels.</summary>
    public Contact? Recipient { get; }

    /// <summary>Optional notes for the office.</summary>
    public string? Notes { get; }

    /// <summary>When the request was logged.</summary>
    public DateTime LoggedAt { get; }

    /// <summary>Who logged it: the customer online or the office staff member on the phone.</summary>
    public string? LoggedBy { get; }

    /// <summary>Parcels belonging to this request.</summary>
    public List<Parcel> Parcels { get; } = new();

    /// <summary>True when the customer with this id placed the request.</summary>
    public bool BelongsTo(string? customerId) => customerId is not null && customerId == CustomerId;
}
