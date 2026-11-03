namespace Dms.Domain;

/// <summary>Cash taken in the field (A2 CRC Card 5); no external authorisation.</summary>
public sealed class CashPayment : PaymentMethod
{
    /// <inheritdoc />
    public override string Name => "Cash";

    /// <inheritdoc />
    public override void TakePayment(Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        payment.RecordOutcome(PaymentOutcome.Approved,
            "Cash collected in field; no external authorisation required (A7).");
    }
}
