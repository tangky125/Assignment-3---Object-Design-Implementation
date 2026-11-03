namespace Dms.Domain;

/// <summary>Strategy base for a way of paying (A2 CRC Card 4).</summary>
public abstract class PaymentMethod
{
    /// <summary>Name shown to users and copied onto the payment record.</summary>
    public abstract string Name { get; }

    /// <summary>Settles the attempt and records its outcome on <paramref name="payment"/>.</summary>
    /// <param name="payment">The attempt to settle.</param>
    public abstract void TakePayment(Payment payment);
}
