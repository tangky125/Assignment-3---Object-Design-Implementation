namespace Dms.Domain;

/// <summary>
/// Adapter to the card provider (A2 CRC Card 7). Simulated for this assignment: a real
/// vendor client can be swapped in here without any domain class changing (Adapter pattern).
/// </summary>
public sealed class PaymentGateway
{
    private readonly string _providerName = "simulated payment provider";

    /// <summary>Authorises <paramref name="amount"/> against a card token; stores nothing.</summary>
    /// <param name="amount">Amount to authorise.</param>
    /// <param name="cardToken">Tokenised card data.</param>
    /// <returns>Approved, or declined for tokens starting "0000".</returns>
    public AuthorisationResult Authorise(decimal amount, string cardToken)
    {
        ArgumentNullException.ThrowIfNull(cardToken);

        return cardToken.StartsWith("0000", StringComparison.Ordinal)
            ? new AuthorisationResult(PaymentOutcome.Declined, $"Card declined by {_providerName}")
            : new AuthorisationResult(PaymentOutcome.Approved, $"Card authorised by {_providerName}");
    }
}
