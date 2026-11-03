// STUB - compile-only stand-in. Owner: Reuben. Delete when the real interface lands.
namespace Dms.Domain;

// The team standard wants an "I" prefix on interfaces, but Delivery.cs already refers to this name.
// The stub keeps it (with a scoped suppression) until the real interface and Delivery.cs are renamed together.
#pragma warning disable CA1715, IDE1006
/// <summary>Receives a call after a delivery status change.</summary>
public interface StatusObserver
{
    /// <summary>Called after <paramref name="delivery"/> moved to <paramref name="newStatus"/>.</summary>
    /// <param name="delivery">The delivery that moved.</param>
    /// <param name="newStatus">The status it moved into.</param>
    void OnStatusChanged(Delivery delivery, Status newStatus);
}
#pragma warning restore CA1715, IDE1006


