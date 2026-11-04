using System.Globalization;

namespace Dms.Domain;

/// <summary>
/// The quote negotiation for one request (A1 Tasks 2 and 3), extracted from DeliveryRequest as
/// the A2 hand-over notes suggested ("if DeliveryRequest grows, extract quote negotiation").
/// It owns the request's status until a quote is accepted, keeps every quote version, and creates
/// the <see cref="Delivery"/> on acceptance, after which the Delivery owns the status.
/// Every command takes a replay key (DA9) and the caller's time; the class never reads a clock.
/// </summary>
public sealed class QuoteNegotiator
{
    /// <summary>Actor recorded for changes the system makes itself (lazy expiry).</summary>
    public const string SystemActor = "System";

    private readonly StatusLifecycle _lifecycle;
    private readonly SystemConfiguration _config;
    private readonly List<Quote> _quotes = new();
    private readonly List<DeliveryEvent> _history = new();
    private readonly HashSet<Guid> _seen = new();
    private Status _status;

    /// <summary>Starts negotiation for a newly logged request (birth state Requested).</summary>
    /// <param name="subject">What pricing needs to know about the request.</param>
    /// <param name="request">Area 1's request, handed to the Delivery on acceptance.</param>
    /// <param name="lifecycle">The single owner of legal status moves.</param>
    /// <param name="config">Quote validity and window rules.</param>
    public QuoteNegotiator(QuoteSubject subject, DeliveryRequest request, StatusLifecycle lifecycle, SystemConfiguration config)
        : this(subject, request, lifecycle, config, Status.Requested)
    {
    }

    private QuoteNegotiator(QuoteSubject subject, DeliveryRequest request, StatusLifecycle lifecycle, SystemConfiguration config, Status status)
    {
        Subject = subject ?? throw new ArgumentNullException(nameof(subject));
        Request = request ?? throw new ArgumentNullException(nameof(request));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _status = status;
    }

    /// <summary>What pricing needs to know about the request.</summary>
    public QuoteSubject Subject { get; }

    /// <summary>Area 1's request.</summary>
    public DeliveryRequest Request { get; }

    /// <summary>The request's status as far as quoting knows it (Requested, Quoted, Accepted, Rejected).</summary>
    public Status Status => _status;

    /// <summary>Every quote version, oldest first.</summary>
    public IReadOnlyList<Quote> Quotes => _quotes;

    /// <summary>The latest quote version, or null when none has been prepared.</summary>
    public Quote? CurrentQuote => _quotes.Count == 0 ? null : _quotes[^1];

    /// <summary>Status changes and notes recorded by quoting, oldest first.</summary>
    public IReadOnlyList<DeliveryEvent> History => _history;

    /// <summary>The delivery created when a quote was accepted; null before that.</summary>
    public Delivery? Delivery { get; private set; }

    /// <summary>True while the latest quote is held for an owner to price.</summary>
    public bool IsAwaitingOwner => _status == Status.Requested && CurrentQuote is { AwaitingOwnerApproval: true };

    /// <summary>True when office staff should prepare a quote (A1 Task 2 subtask 1).</summary>
    public bool IsWaitingForQuote => _status == Status.Requested && !IsAwaitingOwner;

    /// <summary>
    /// Rebuilds a negotiation from stored data at start-up (A2 bootstrap stage 3).
    /// </summary>
    public static QuoteNegotiator Restore(
        QuoteSubject subject,
        DeliveryRequest request,
        StatusLifecycle lifecycle,
        SystemConfiguration config,
        Status status,
        IEnumerable<Quote> quotes)
    {
        ArgumentNullException.ThrowIfNull(quotes);
        var negotiator = new QuoteNegotiator(subject, request, lifecycle, config, status);
        negotiator._quotes.AddRange(quotes.OrderBy(q => q.Version));
        return negotiator;
    }

    /// <summary>
    /// Proposes achievable pickup and delivery windows: from when the parcels are ready (or now,
    /// if later), rounded up to the half hour (A1 Task 2 subtask 4).
    /// </summary>
    public (TimeWindow Pickup, TimeWindow Delivery) ProposeWindows(DateTimeOffset at)
    {
        DateTimeOffset start = RoundUpToHalfHour(Subject.ReadyTime > at ? Subject.ReadyTime : at);
        DateTimeOffset deliveryStart = start + _config.DeliveryStartsAfterPickup;
        return (new TimeWindow(start, start + _config.PickupWindowLength),
                new TimeWindow(deliveryStart, deliveryStart + _config.DeliveryWindowLength));
    }

    /// <summary>Asks the service area for a price (A1 Task 2 subtasks 2-3).</summary>
    public PriceProposal ProposePrice(ServiceArea area, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(area);
        if (!IsWaitingForQuote)
        {
            throw new InvalidOperationException($"{Subject.Reference} is not waiting for a quote (status {_status}).");
        }

        return area.ProposePrice(Subject, at);
    }

    /// <summary>
    /// Issues a quote version (A1 Task 2 subtask 5): Requested -> Quoted. An out-of-area quote is
    /// held for an owner instead and the request stays Requested (variant 2a).
    /// </summary>
    /// <param name="eventId">Replay key; a repeated id returns the quote already issued.</param>
    /// <param name="actorId">Who issued it.</param>
    /// <param name="at">When.</param>
    /// <param name="proposal">The policy's proposal.</param>
    /// <param name="adjustedPrice">A different price chosen by office staff, if any.</param>
    /// <param name="adjustReason">Why; required with <paramref name="adjustedPrice"/>.</param>
    /// <param name="pickupWindow">Pickup window offered.</param>
    /// <param name="deliveryWindow">Delivery window offered.</param>
    public Quote IssueQuote(
        Guid eventId,
        string actorId,
        DateTimeOffset at,
        PriceProposal proposal,
        decimal? adjustedPrice,
        string? adjustReason,
        TimeWindow pickupWindow,
        TimeWindow deliveryWindow)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(pickupWindow);
        ArgumentNullException.ThrowIfNull(deliveryWindow);
        if (!_seen.Add(eventId))
        {
            return _quotes.First(q => q.EventId == eventId);
        }

        RequireMove(Status.Quoted);
        if (IsAwaitingOwner)
        {
            throw new InvalidOperationException($"Quote v{CurrentQuote!.Version} is already waiting for an owner to set the price.");
        }

        if (pickupWindow.Start < Subject.ReadyTime)
        {
            throw new ArgumentException(string.Create(
                CultureInfo.InvariantCulture,
                $"Pickup cannot start before the parcels are ready ({Subject.ReadyTime:dd/MM/yyyy HH:mm})."));
        }

        if (pickupWindow.Length > _config.MaxWindowLength || deliveryWindow.Length > _config.MaxWindowLength)
        {
            throw new ArgumentException(string.Create(
                CultureInfo.InvariantCulture,
                $"A window can be at most {_config.MaxWindowLength.TotalHours:0} hours long."));
        }

        if (adjustedPrice is not null && proposal.NeedsOwnerApproval)
        {
            throw new InvalidOperationException("Out-of-area quotes are priced by an owner, so office staff cannot set the price.");
        }

        // Re-entering the proposed price is not an adjustment.
        decimal? adjusted = adjustedPrice == proposal.Price ? null : adjustedPrice;
        var quote = new Quote(
            eventId,
            _quotes.Count + 1,
            proposal.Price,
            adjusted,
            adjustReason,
            actorId,
            pickupWindow,
            deliveryWindow,
            at,
            _config.QuoteValidity,
            CurrentQuote);
        _quotes.Add(quote);

        if (quote.AwaitingOwnerApproval)
        {
            _history.Add(new DeliveryEvent(eventId, at, null, null, $"Quote v{quote.Version} held for owner pricing (destination out of area)", actorId));
            return quote;
        }

        Move(Status.Quoted, $"Quote v{quote.Version} issued at {Money(quote.FinalPrice!.Value)}", actorId, at, eventId);
        return quote;
    }

    /// <summary>
    /// Records an owner's price for a held out-of-area quote and releases it to the customer:
    /// Requested -> Quoted, with the validity period starting now.
    /// </summary>
    public void ReleaseOwnerPrice(Guid eventId, string ownerId, DateTimeOffset at, decimal price)
    {
        if (!_seen.Add(eventId))
        {
            return;
        }

        if (!IsAwaitingOwner)
        {
            throw new InvalidOperationException($"{Subject.Reference} has no quote waiting for an owner.");
        }

        RequireMove(Status.Quoted);
        Quote quote = CurrentQuote!;
        quote.ReleaseOwnerPrice(price, ownerId, at);
        Move(Status.Quoted, $"Owner priced quote v{quote.Version} at {Money(price)}", ownerId, at, eventId);
    }

    /// <summary>
    /// Lazy expiry (A2 S3, A1 Task 2 variant 5a): when the current quote has passed its validity
    /// without a decision, mark it expired and return the request to Requested so it reappears in
    /// the quote queue. Returns true when it expired the quote.
    /// </summary>
    public bool ExpireIfDue(Guid eventId, DateTimeOffset at)
    {
        Quote? quote = CurrentQuote;
        if (_status != Status.Quoted || quote is null || !quote.IsExpired(at) || !_seen.Add(eventId))
        {
            return false;
        }

        RequireMove(Status.Requested);
        quote.RecordDecision(QuoteDecision.Expired, SystemActor, false, at, null);
        Move(Status.Requested, $"Quote v{quote.Version} expired without a decision; back in the quote queue", SystemActor, at, eventId);
        return true;
    }

    /// <summary>
    /// Accepts the current quote (A1 Task 3 subtask 3a): Quoted -> Accepted and the Delivery is
    /// created. Refused once the quote has expired (variant 1a).
    /// </summary>
    /// <param name="eventId">Replay key; a repeated id returns the delivery already created.</param>
    /// <param name="actorId">Who recorded the decision.</param>
    /// <param name="recordedByStaff">True when office staff record a decision given by phone or email.</param>
    /// <param name="at">When.</param>
    public Delivery Accept(Guid eventId, string actorId, bool recordedByStaff, DateTimeOffset at)
    {
        if (!_seen.Add(eventId))
        {
            return Delivery ?? throw new InvalidOperationException($"{Subject.Reference} has no delivery.");
        }

        RequireMove(Status.Accepted);
        Quote quote = CurrentQuote ?? throw new InvalidOperationException($"{Subject.Reference} has no quote to accept.");
        quote.RecordDecision(QuoteDecision.Accepted, actorId, recordedByStaff, at, null);
        Move(Status.Accepted, $"Quote v{quote.Version} accepted", actorId, at, eventId);

        // A2 CRC DeliveryRequest: "Creates its delivery when a quote is accepted". Birth state Accepted.
        Delivery = new Delivery(Request, _lifecycle);
        return Delivery;
    }

    /// <summary>Rejects the current quote and closes the request (A1 Task 3 subtask 3b): Quoted -> Rejected.</summary>
    public void Reject(Guid eventId, string actorId, bool recordedByStaff, DateTimeOffset at, string reason)
    {
        if (!_seen.Add(eventId))
        {
            return;
        }

        RequireReason(reason);
        RequireMove(Status.Rejected);
        Quote quote = CurrentQuote ?? throw new InvalidOperationException($"{Subject.Reference} has no quote to reject.");
        quote.RecordDecision(QuoteDecision.Rejected, actorId, recordedByStaff, at, reason);
        Move(Status.Rejected, $"Quote v{quote.Version} rejected: {reason.Trim()}", actorId, at, eventId);
    }

    /// <summary>
    /// The customer asks for a revised quote (A1 Task 3 subtask 4, variant 2a): Quoted -> Requested.
    /// The current version is kept, and the next one records that it revises it (Task 2 variant 3a).
    /// </summary>
    public void RequestRevision(Guid eventId, string actorId, bool recordedByStaff, DateTimeOffset at, string reason)
    {
        if (!_seen.Add(eventId))
        {
            return;
        }

        RequireReason(reason);
        RequireMove(Status.Requested);
        Quote quote = CurrentQuote ?? throw new InvalidOperationException($"{Subject.Reference} has no quote to revise.");
        quote.RecordDecision(QuoteDecision.RevisionRequested, actorId, recordedByStaff, at, reason);
        Move(Status.Requested, $"Revised quote requested: {reason.Trim()}", actorId, at, eventId);
    }

    private static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required.");
        }
    }

    private static DateTimeOffset RoundUpToHalfHour(DateTimeOffset t)
    {
        var hour = new DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Offset);
        if (hour == t)
        {
            return t;
        }

        return t <= hour.AddMinutes(30) ? hour.AddMinutes(30) : hour.AddHours(1);
    }

    private static string Money(decimal amount) =>
        "$" + amount.ToString("#,##0.00", CultureInfo.InvariantCulture);

    /// <summary>Checks a move before anything changes, so a refused command leaves no trace.</summary>
    private void RequireMove(Status next)
    {
        if (!_lifecycle.CanTransition(_status, next))
        {
            throw new InvalidOperationException(_lifecycle.ExplainRefusal(_status, next));
        }
    }

    /// <summary>The one place quoting writes the status (team contract rule 2).</summary>
    private void Move(Status next, string note, string actorId, DateTimeOffset at, Guid eventId)
    {
        RequireMove(next);
        _history.Add(new DeliveryEvent(eventId, at, _status, next, note, actorId));
        _status = next;
    }
}
