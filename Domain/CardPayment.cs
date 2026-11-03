namespace Dms.Domain;

/// <summary>Card payment through the gateway (A2 CRC Card 6); keeps no card data (A6, DA5).</summary>
public sealed class CardPayment : PaymentMethod
{
    private readonly PaymentGateway _gateway;
    private string? _cardToken;

    /// <summary>Creates the method around a gateway and a one-use card token.</summary>
    /// <param name="gateway">Adapter to the payment provider.</param>
    /// <param name="cardToken">Tokenised card data; cleared once used.</param>
    public CardPayment(PaymentGateway gateway, string cardToken)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _cardToken = cardToken;
    }

    /// <inheritdoc />
    public override string Name => "Card";

    /// <inheritdoc />
    public override void TakePayment(Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        if (_cardToken is null)
        {
            throw new InvalidOperationException("This card method has already been used; supply a fresh token (A6).");
        }

        AuthorisationResult result = _gateway.Authorise(payment.Amount, _cardToken);
        payment.RecordOutcome(result.Outcome, result.Message);
        _cardToken = null;
    }
}
