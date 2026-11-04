using System.Globalization;

namespace Dms.Domain;

/// <summary>
/// Prices in-area requests from the owners' configured rules (A2 CRC ZonePricingPolicy):
/// zone base rate, extra parcels, size bands, handling flags and express.
/// </summary>
public sealed class ZonePricingPolicy : PricingPolicy
{
    private readonly ServiceArea _area;
    private readonly SystemConfiguration _config;

    /// <summary>Creates the policy.</summary>
    /// <param name="area">Finds the zone of each postcode.</param>
    /// <param name="config">The rates.</param>
    public ZonePricingPolicy(ServiceArea area, SystemConfiguration config)
    {
        _area = area ?? throw new ArgumentNullException(nameof(area));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc/>
    public override bool NeedsOwnerApproval => false;

    /// <inheritdoc/>
    public override PriceProposal Propose(QuoteSubject subject, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ZoneRule pickup = _area.ZoneOf(subject.PickupPostcode)
            ?? throw new InvalidOperationException($"Pickup postcode {subject.PickupPostcode} is outside the service area.");
        ZoneRule destination = _area.ZoneOf(subject.DestinationPostcode)
            ?? throw new InvalidOperationException($"Destination postcode {subject.DestinationPostcode} has no zone price; an owner must price it.");

        // The dearer of the two zones sets the base rate.
        ZoneRule zone = pickup.BaseRate >= destination.BaseRate ? pickup : destination;
        var lines = new List<string> { $"Zone {zone.Name} base price: {Money(zone.BaseRate)}" };
        decimal price = zone.BaseRate;

        int extraParcels = subject.Parcels.Count - 1;
        if (extraParcels > 0)
        {
            price += Add(lines, "Extra parcels", extraParcels, _config.ExtraParcelCharge);
        }

        foreach (IGrouping<string, ParcelSpec> band in subject.Parcels.GroupBy(p => p.SizeBand, StringComparer.OrdinalIgnoreCase))
        {
            if (_config.SizeSurcharges.TryGetValue(band.Key, out decimal surcharge) && surcharge > 0m)
            {
                price += Add(lines, $"{band.Key} parcels", band.Count(), surcharge);
            }
        }

        price += Add(lines, "Fragile handling", subject.Parcels.Count(p => p.Fragile), _config.FragileCharge);
        price += Add(lines, "Signature on delivery", subject.Parcels.Count(p => p.SignatureRequired), _config.SignatureCharge);
        price += Add(lines, "Keep upright", subject.Parcels.Count(p => p.KeepUpright), _config.KeepUprightCharge);

        if (subject.ReadyTime < at + _config.ExpressWithin)
        {
            price = decimal.Round(price * _config.ExpressMultiplier, 2);
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Express (ready within {_config.ExpressWithin.TotalHours:0.#} hours): x{_config.ExpressMultiplier:0.##}"));
        }

        return new PriceProposal(price, false, lines);
    }

    private static decimal Add(List<string> lines, string label, int count, decimal each)
    {
        if (count == 0)
        {
            return 0m;
        }

        decimal amount = each * count;
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"{label} x{count}: +{Money(amount)}"));
        return amount;
    }
}
