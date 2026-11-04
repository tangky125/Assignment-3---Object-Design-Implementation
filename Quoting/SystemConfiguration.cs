namespace Dms.Domain;

/// <summary>One pricing zone: a postcode range and its base rate.</summary>
/// <param name="Name">Zone name shown in the price breakdown, e.g. "A".</param>
/// <param name="FromPostcode">First postcode in the zone (inclusive).</param>
/// <param name="ToPostcode">Last postcode in the zone (inclusive).</param>
/// <param name="BaseRate">Price for the first parcel within this zone.</param>
public sealed record ZoneRule(string Name, int FromPostcode, int ToPostcode, decimal BaseRate);

/// <summary>
/// The owners' operational parameters (A2 data holder SystemConfiguration, DA6): pricing rules,
/// quote validity and how windows are proposed. Rule editing is out of scope, so the values are
/// set once at start-up; <see cref="Default"/> holds the rates the group agreed.
/// </summary>
public sealed record SystemConfiguration
{
    /// <summary>The configuration used unless the bootstrap supplies another.</summary>
    public static SystemConfiguration Default { get; } = new();

    /// <summary>How long an issued quote may be accepted (A1 assumption A10: 24 hours).</summary>
    public TimeSpan QuoteValidity { get; init; } = TimeSpan.FromHours(24);

    /// <summary>The service area (Greater Melbourne), as postcode zones.</summary>
    public IReadOnlyList<ZoneRule> Zones { get; init; } = new[]
    {
        new ZoneRule("A", 3000, 3099, 15m),
        new ZoneRule("B", 3100, 3199, 22m),
        new ZoneRule("C", 3200, 3207, 30m),
    };

    /// <summary>Charge for each parcel after the first.</summary>
    public decimal ExtraParcelCharge { get; init; } = 4m;

    /// <summary>Surcharge per parcel by size band; bands not listed cost nothing extra.</summary>
    public IReadOnlyDictionary<string, decimal> SizeSurcharges { get; init; } =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["Small"] = 0m,
            ["Medium"] = 5m,
            ["Large"] = 12m,
        };

    /// <summary>Surcharge per fragile parcel.</summary>
    public decimal FragileCharge { get; init; } = 5m;

    /// <summary>Surcharge per parcel that needs a signature on delivery.</summary>
    public decimal SignatureCharge { get; init; } = 2m;

    /// <summary>Surcharge per parcel that must be kept upright.</summary>
    public decimal KeepUprightCharge { get; init; } = 3m;

    /// <summary>A request whose parcels are ready within this time of quoting is express.</summary>
    public TimeSpan ExpressWithin { get; init; } = TimeSpan.FromHours(2);

    /// <summary>Multiplier applied to an express request's price.</summary>
    public decimal ExpressMultiplier { get; init; } = 1.5m;

    /// <summary>Length of a proposed pickup window.</summary>
    public TimeSpan PickupWindowLength { get; init; } = TimeSpan.FromHours(2);

    /// <summary>How long after the pickup window opens the proposed delivery window opens.</summary>
    public TimeSpan DeliveryStartsAfterPickup { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Length of a proposed delivery window.</summary>
    public TimeSpan DeliveryWindowLength { get; init; } = TimeSpan.FromHours(3);

    /// <summary>Longest window office staff may offer.</summary>
    public TimeSpan MaxWindowLength { get; init; } = TimeSpan.FromHours(12);
}
