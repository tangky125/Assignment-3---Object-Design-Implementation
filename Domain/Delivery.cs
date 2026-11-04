namespace Dms.Domain;

/// Operational performance of an accepted quote (A2 CRC Card 1).
/// Scope: only A1 Task 5 commands live here. Scheduled enters via
/// AllocationCoordinator (CRC Table 31); Cancelled enters via DeliveryRequest
/// (CRC Table 13). Rejected can never reach a Delivery (A2 §4.3, Contract 1).
public class Delivery
{
    private readonly DeliveryRequest _request;
    private readonly StatusLifecycle _lifecycle;
    private readonly List<DeliveryEvent> _history = new();
    private readonly List<Payment> _payments = new();
    private readonly HashSet<Guid> _seen = new();            // DA9 replay memory
    private readonly List<StatusObserver> _observers = new();

    private Status _status = Status.Accepted;                // birth state (readme rule 3)
    private DeliveryStaffProfile? _allocatedProfile;

    public Delivery(DeliveryRequest request, StatusLifecycle lifecycle)
    { (_request, _lifecycle) = (request, lifecycle); }

    // ---- queries ----
    public Status Status => _status;
    public IReadOnlyList<DeliveryEvent> History => _history;
    public IReadOnlyList<Payment> Payments => _payments;

    // ---- Task 5 commands (the four "Records…" rows of CRC Card 1) ----

    // 1. Collection, checked off parcel by parcel (variant 4a); status flips only when ALL are in
    public void RecordParcelCollected(Guid eventId, string actorId, DateTimeOffset at, Parcel parcel)
    {
        if (_seen.Contains(eventId))
        {
            return;
        }

        parcel.MarkCollected();                               // Reuben's Parcel owns its flag
        if (_request.Parcels.All(p => p.IsCollected))
        {
            Move(Status.Collected, "All parcels collected", actorId, at, eventId);
        }

        _seen.Add(eventId);                                   // memory committed only after success (DA9)
    }

    // 2. Payment attempt: a record, NOT a status change.
    // A5 => only at pickup, so Scheduled (the UI's job-page order) or Collected.
    public Payment RecordPaymentAttempt(Guid eventId, string actorId, DateTimeOffset at,
                                        PaymentMethod method, decimal amount)
    {
        if (_seen.Contains(eventId))
        {
            return _payments.Find(p => p.EventId == eventId)
                ?? throw new InvalidOperationException($"Event {eventId} was already used by another command (DA9).");
        }

        if (_status is not (Status.Scheduled or Status.Collected))
        {
            throw new InvalidOperationException($"Payment is taken at pickup (A5); status is {_status}.");
        }

        if (_payments.Any(p => p.WasSuccessful))
        {
            throw new InvalidOperationException("A successful payment is already retained (DA4).");
        }

        var payment = new Payment(this, method, amount, eventId, at);
        method.TakePayment(payment);                          // Strategy: cash records, card asks gateway
        _payments.Add(payment);
        _history.Add(new DeliveryEvent(eventId, at, null, null,
                                       $"Payment attempt: {payment.Outcome}", actorId));
        _seen.Add(eventId);                                   // memory committed only after success (DA9)
        return payment;
    }

    // 3. Successful hand-over
    public void RecordSuccessfulHandover(Guid eventId, string actorId, DateTimeOffset at, string receivedBy)
    {
        if (_seen.Contains(eventId))
        {
            return;
        }

        Move(Status.Delivered, $"Handed over to {receivedBy}", actorId, at, eventId);
        _seen.Add(eventId);                                   // memory committed only after success (DA9)
    }

    // 4. Failed attempt; reason mandatory (Task 5 subtask 5; UI validation backs this)
    public void RecordFailedAttempt(Guid eventId, string actorId, DateTimeOffset at, string reason)
    {
        if (_seen.Contains(eventId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A failure reason is required.");
        }

        Move(Status.DeliveryFailed, $"Failed: {reason}", actorId, at, eventId);
        _seen.Add(eventId);                                   // memory committed only after success (DA9)
    }

    // ---- inbound hooks owned by OTHER areas (no Area-4 UI touches these) ----
    public void RecordAllocation(DeliveryStaffProfile profile, Guid eventId, string actorId, DateTimeOffset at)
    {   // called by AllocationCoordinator (Maleesha, Task 4)
        if (_seen.Contains(eventId))
        {
            return;
        }

        _allocatedProfile = profile;
        Move(Status.Scheduled, "Allocated", actorId, at, eventId);
        _seen.Add(eventId);                                   // memory committed only after success (DA9)
    }

    public void ApplyCustomerCancellation(Guid eventId, string actorId, DateTimeOffset at, string reason)
    {   // called by DeliveryRequest (Clarice, Task 3) — never by the driver screen
        if (_seen.Contains(eventId))
        {
            return;
        }

        Move(Status.Cancelled, $"Customer cancelled: {reason}", actorId, at, eventId);
        _seen.Add(eventId);                                   // memory committed only after success (DA9)
    }

    // ---- observer wiring + the one place status is written ----
    public void AttachObserver(StatusObserver o) => _observers.Add(o);

    private void Move(Status next, string note, string actor, DateTimeOffset at, Guid eventId)
    {
        if (!_lifecycle.CanTransition(_status, next))
        {
            throw new InvalidOperationException(_lifecycle.ExplainRefusal(_status, next));
        }

        _history.Add(new DeliveryEvent(eventId, at, _status, next, note, actor));
        _status = next;
        foreach (var o in _observers)
        {
            o.OnStatusChanged(this, next);
        }
    }
}
