using Dms.Api.Contracts;
using Dms.Api.Controllers;
using Dms.Domain;

namespace Dms.Api.Demo;

/// <summary>
/// TEMPORARY bridge for Area 2 while Area 1 (requests and customers) is still the demo backend.
/// It builds one real <see cref="QuoteNegotiator"/> per request from the demo's records (the A2
/// "restore at start-up" path) and mirrors every Area 2 result back into the demo's read models,
/// so tracking and the allocation queue stay consistent. Replace it with the real RequestRegister
/// and storage when Area 1 lands; <see cref="QuoteServiceAdapter"/> does not change.
/// </summary>
public sealed class DemoQuoteRegistry : IQuoteRegistry
{
    private readonly DmsServices _demo;
    private readonly DemoBackend _mirror;
    private readonly StatusLifecycle _lifecycle;
    private readonly SystemConfiguration _config;
    private readonly Dictionary<string, QuoteNegotiator> _negotiators = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (Delivery Delivery, decimal AmountDue)> _accepted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the bridge over the demo services.</summary>
    /// <param name="demo">The services built by <see cref="DemoBackend.Create"/>.</param>
    /// <param name="lifecycle">The shared status rules.</param>
    /// <param name="config">Quote validity and pricing rules.</param>
    public DemoQuoteRegistry(DmsServices demo, StatusLifecycle lifecycle, SystemConfiguration config)
    {
        _demo = demo ?? throw new ArgumentNullException(nameof(demo));
        _mirror = demo.Tracking as DemoBackend
            ?? throw new ArgumentException("The bridge needs the demo backend's tracking service.", nameof(demo));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>Deliveries created by accepted quotes, waiting for Area 3 to allocate them.</summary>
    public IReadOnlyDictionary<string, (Delivery Delivery, decimal AmountDue)> AcceptedDeliveries => _accepted;

    /// <inheritdoc/>
    public IReadOnlyList<RequestSummary> AllRequests() => _demo.Tracking.Search(null, null, null, null);

    /// <inheritdoc/>
    public IReadOnlyList<RequestSummary> RequestsForCustomer(string customerId) => _demo.Quotes.RequestsForCustomer(customerId);

    /// <inheritdoc/>
    public QuoteContext? Find(string reference)
    {
        RequestDetail? detail = _demo.Tracking.Find(reference);
        if (detail is null)
        {
            return null;
        }

        if (!_negotiators.TryGetValue(detail.Reference, out QuoteNegotiator? negotiator))
        {
            negotiator = Restore(detail);
            _negotiators[detail.Reference] = negotiator;
        }

        return new QuoteContext(negotiator, detail);
    }

    /// <inheritdoc/>
    public void Saved(string reference, Status status, IReadOnlyList<QuoteView> quotes, IReadOnlyList<EventView> newEvents) =>
        _mirror.MirrorQuoteState(reference, status, quotes, newEvents);

    /// <inheritdoc/>
    public void DeliveryCreated(string reference, Delivery delivery, decimal amountDue) =>
        _accepted[reference] = (delivery, amountDue);

    private QuoteNegotiator Restore(RequestDetail detail)
    {
        var subject = new QuoteSubject(
            detail.Reference,
            detail.Pickup.Postcode,
            detail.Destination.Postcode,
            new DateTimeOffset(detail.ReadyTime),
            detail.Parcels.Select(p => new ParcelSpec(
                p.Size.ToString(),
                p.Flags.HasFlag(ParcelHandling.Fragile),
                p.Flags.HasFlag(ParcelHandling.SignatureRequired),
                p.Flags.HasFlag(ParcelHandling.KeepUpright))).ToList());

        var request = new DeliveryRequest(detail.Reference);
        foreach (ParcelView p in detail.Parcels)
        {
            request.Parcels.Add(new Parcel(p.ParcelId, p.Size.ToString()));
        }

        var quotes = new List<Quote>();
        foreach (QuoteView view in _demo.Quotes.QuoteHistory(detail.Reference).OrderBy(q => q.Version))
        {
            quotes.Add(Quote.Restore(
                view.Version,
                view.Price,
                view.AdjustReason,
                new TimeWindow(new DateTimeOffset(view.PickupWindow.Start), new DateTimeOffset(view.PickupWindow.End)),
                new TimeWindow(new DateTimeOffset(view.DeliveryWindow.Start), new DateTimeOffset(view.DeliveryWindow.End)),
                new DateTimeOffset(view.IssuedAt),
                _config.QuoteValidity,
                DecisionOf(view.Outcome),
                ReasonOf(view.Outcome),
                quotes.Count == 0 ? null : quotes[^1]));
        }

        return QuoteNegotiator.Restore(subject, request, _lifecycle, _config, detail.Status, quotes);
    }

    private static QuoteDecision DecisionOf(string outcome)
    {
        if (outcome.StartsWith("Accepted", StringComparison.OrdinalIgnoreCase))
        {
            return QuoteDecision.Accepted;
        }

        if (outcome.StartsWith("Rejected", StringComparison.OrdinalIgnoreCase))
        {
            return QuoteDecision.Rejected;
        }

        if (outcome.StartsWith("Revision", StringComparison.OrdinalIgnoreCase))
        {
            return QuoteDecision.RevisionRequested;
        }

        // "Expired (no decision)" in the demo only means the time has passed: the domain decides
        // expiry itself when the quote is next consulted (A2 S3), so it is restored as undecided.
        return QuoteDecision.Pending;
    }

    private static string? ReasonOf(string outcome)
    {
        int colon = outcome.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? null : outcome[(colon + 1)..].Trim();
    }
}
