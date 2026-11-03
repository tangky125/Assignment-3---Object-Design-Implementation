// STUB - compile-only stand-in. Owner: Reuben. Delete when the real class lands.
namespace Dms.Domain;

/// <summary>Placeholder request until Reuben's status-bearing class lands.</summary>
public class DeliveryRequest
{
    /// <summary>Creates a request from its client reference.</summary>
    /// <param name="reference">Client-facing reference code.</param>
    public DeliveryRequest(string reference) => Reference = reference;

    /// <summary>Client-facing reference code.</summary>
    public string Reference { get; }

    /// <summary>Parcels belonging to this request.</summary>
    public List<Parcel> Parcels { get; } = new();
}
