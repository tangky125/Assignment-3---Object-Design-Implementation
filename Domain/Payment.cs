using System.Globalization;

namespace Dms.Domain;

/// <summary>One payment attempt on a delivery (A2 CRC Card 3). Holds no card details (A6, DA5).</summary>
public class Payment
{
    private readonly Delivery _delivery;

    /// <summary>Creates an attempt that has not been settled yet.</summary>
    /// <param name="delivery">Delivery being paid for.</param>
    /// <param name="method">Strategy that will settle the attempt.</param>
    /// <param name="amount">Amount to take.</param>
    /// <param name="eventId">Idempotency key of the originating command (DA9).</param>
    /// <param name="attemptedAt">Caller-supplied timestamp; the domain never reads the clock.</param>
    public Payment(Delivery delivery, PaymentMethod method, decimal amount, Guid eventId, DateTimeOffset attemptedAt)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(method);

        _delivery = delivery;
        MethodName = method.Name;
        Amount = amount;
        EventId = eventId;
        AttemptedAt = attemptedAt;
    }

    /// <summary>Delivery this attempt belongs to.</summary>
    public Delivery Delivery => _delivery;


    /// <summary>Method name taken at construction ("Cash" or "Card"), so no card reference survives (A6).</summary>
    public string MethodName { get; }

    /// <summary>Amount being taken.</summary>
    public decimal Amount { get; }

    /// <summary>Idempotency key of the originating command (DA9).</summary>
    public Guid EventId { get; }

    /// <summary>When the attempt was made, supplied by the caller.</summary>
    public DateTimeOffset AttemptedAt { get; }

    /// <summary>Settled outcome, or <c>null</c> while the attempt is pending.</summary>
    public PaymentOutcome? Outcome { get; private set; }

    /// <summary>Gateway wording recorded with the outcome.</summary>
    public string OutcomeNote { get; private set; } = string.Empty;

    /// <summary><c>true</c> when approved; a delivery retains at most one such payment (DA4).</summary>
    public bool WasSuccessful => Outcome == PaymentOutcome.Approved;

    /// <summary>One-line text for delivery history notes.</summary>
    public string Summary => Outcome is null
        ? $"{MethodName} {Money(Amount)} pending"
        : $"{MethodName} {Money(Amount)} {Outcome}: {OutcomeNote}";

    /// <summary>Records the settled outcome once; a second call is a programming error.</summary>
    /// <param name="outcome">What the provider or the field device returned.</param>
    /// <param name="note">Wording to keep for the audit trail.</param>
    public void RecordOutcome(PaymentOutcome outcome, string note)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (Outcome is not null)
        {
            throw new InvalidOperationException($"Outcome already recorded for payment event {EventId}.");
        }

        Outcome = outcome;
        OutcomeNote = note;
    }

    private static string Money(decimal amount) =>
        "$" + amount.ToString("#,##0.00", CultureInfo.InvariantCulture);
}
