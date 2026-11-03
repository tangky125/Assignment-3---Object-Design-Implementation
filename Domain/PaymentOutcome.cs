namespace Dms.Domain;

/// <summary>Settled result of one payment attempt (A2 CRC Card 3).</summary>
public enum PaymentOutcome
{
    /// <summary>Funds were taken; the attempt succeeded.</summary>
    Approved,

    /// <summary>Funds were refused; nothing was taken.</summary>
    Declined
}
