namespace Dms.Domain;

/// <summary>Immutable answer from the payment gateway (A2 CRC Card 7).</summary>
/// <param name="Outcome">Approved or declined.</param>
/// <param name="Message">Wording for the UI; the domain never prints it.</param>
public sealed record AuthorisationResult(PaymentOutcome Outcome, string Message);
