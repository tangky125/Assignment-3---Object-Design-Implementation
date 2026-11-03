// STUB - compile-only stand-in. Owner: Ky. Delete when the real class lands.
namespace Dms.Domain;

/// <summary>One immutable history entry; shape dictated by the existing Delivery call site.</summary>
/// <param name="EventId">Idempotency key of the originating command (DA9).</param>
/// <param name="OccurredAt">Caller-supplied timestamp; the domain never reads the clock.</param>
/// <param name="From">Status left behind, or <c>null</c> for a non-status entry.</param>
/// <param name="To">Status entered, or <c>null</c> for a non-status entry.</param>
/// <param name="Note">Wording for the UI.</param>
/// <param name="ActorId">Who caused the entry.</param>
public sealed record DeliveryEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Status? From,
    Status? To,
    string Note,
    string ActorId);

