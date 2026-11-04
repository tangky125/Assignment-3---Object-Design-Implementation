namespace Dms.Domain;

/// <summary>One parcel as pricing sees it: its size band and handling flags.</summary>
/// <param name="SizeBand">"Small", "Medium" or "Large" (A1 Task 1 subtask 4).</param>
/// <param name="Fragile">Needs careful handling.</param>
/// <param name="SignatureRequired">Needs a signature on delivery.</param>
/// <param name="KeepUpright">Must be kept upright.</param>
public sealed record ParcelSpec(string SizeBand, bool Fragile, bool SignatureRequired, bool KeepUpright);

/// <summary>
/// What quoting needs to know about a request: where it goes, when it is ready and what the
/// parcels are. Built from Area 1's request, so pricing does not depend on that class's shape.
/// </summary>
/// <param name="Reference">The request reference, e.g. DMS-2026-0001.</param>
/// <param name="PickupPostcode">Pickup postcode (must be in the service area, A2 DA11).</param>
/// <param name="DestinationPostcode">Destination postcode (may be out of area).</param>
/// <param name="ReadyTime">When the parcels are ready for pickup.</param>
/// <param name="Parcels">The parcels on the request.</param>
public sealed record QuoteSubject(
    string Reference,
    string PickupPostcode,
    string DestinationPostcode,
    DateTimeOffset ReadyTime,
    IReadOnlyList<ParcelSpec> Parcels);
