using System.Globalization;

namespace Dms.Domain;

/// <summary>
/// Proposes a price for a request (A2 CRC PricingPolicy, abstract; Strategy pattern).
/// <see cref="ServiceArea"/> selects the policy, so a request never knows which one priced it.
/// </summary>
public abstract class PricingPolicy
{
    /// <summary>True when this policy's proposals must be priced by an owner before issue.</summary>
    public abstract bool NeedsOwnerApproval { get; }

    /// <summary>Proposes a price for <paramref name="subject"/> quoted at <paramref name="at"/>.</summary>
    /// <param name="subject">The request being priced.</param>
    /// <param name="at">When the quote is being prepared (express depends on it).</param>
    public abstract PriceProposal Propose(QuoteSubject subject, DateTimeOffset at);

    /// <summary>Money as the UI shows it, e.g. $1,027.50.</summary>
    protected static string Money(decimal amount) =>
        "$" + amount.ToString("#,##0.00", CultureInfo.InvariantCulture);
}
