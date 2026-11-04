namespace Dms.Domain;

/// <summary>
/// Handles requests whose destination is outside the priced zones (A1 Task 2 variant 2a,
/// A2 CRC OwnerApprovedPricingPolicy). It proposes no automatic price: the quote is held until
/// an owner sets the price, which is real behaviour rather than a do-nothing override (Riel 5.16).
/// </summary>
public sealed class OwnerApprovedPricingPolicy : PricingPolicy
{
    /// <inheritdoc/>
    public override bool NeedsOwnerApproval => true;

    /// <inheritdoc/>
    public override PriceProposal Propose(QuoteSubject subject, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return new PriceProposal(
            null,
            true,
            new[] { $"Destination postcode {subject.DestinationPostcode} is outside the zones with set prices: an owner must set the price." });
    }
}
