using System.Globalization;

namespace Dms.Domain;

/// <summary>
/// The region served and its zones (A2 CRC ServiceArea). It is the single class that changes
/// when the business expands beyond Greater Melbourne, and it chooses the pricing policy.
/// </summary>
public sealed class ServiceArea
{
    private readonly SystemConfiguration _config;
    private readonly ZonePricingPolicy _zonePricing;
    private readonly OwnerApprovedPricingPolicy _ownerPricing = new();

    /// <summary>Creates the service area from the configured zones.</summary>
    public ServiceArea(SystemConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _zonePricing = new ZonePricingPolicy(this, config);
    }

    /// <summary>The zone a postcode falls in, or null when it is outside the service area.</summary>
    public ZoneRule? ZoneOf(string postcode)
    {
        if (!int.TryParse(postcode, NumberStyles.None, CultureInfo.InvariantCulture, out int code))
        {
            return null;
        }

        return _config.Zones.FirstOrDefault(z => code >= z.FromPostcode && code <= z.ToPostcode);
    }

    /// <summary>True when the postcode is inside the service area.</summary>
    public bool Contains(string postcode) => ZoneOf(postcode) is not null;

    /// <summary>
    /// Selects the pricing policy for an address pair. The pickup must be in the area (A2 DA11);
    /// an out-of-area destination is priced by an owner.
    /// </summary>
    public PricingPolicy SelectPolicy(string pickupPostcode, string destinationPostcode)
    {
        if (!Contains(pickupPostcode))
        {
            throw new InvalidOperationException(
                $"Postcode {pickupPostcode} is outside our Greater Melbourne service area, so the parcels cannot be collected.");
        }

        return Contains(destinationPostcode) ? _zonePricing : _ownerPricing;
    }

    /// <summary>Obtains a proposed price for a request from the selected policy.</summary>
    public PriceProposal ProposePrice(QuoteSubject subject, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return SelectPolicy(subject.PickupPostcode, subject.DestinationPostcode).Propose(subject, at);
    }
}
