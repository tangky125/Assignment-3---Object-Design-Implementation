using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dms.Api.Contracts;
using Dms.Domain;

namespace Dms.Api.Controllers;

/// <summary>The Area 2 domain object and the read model behind one request reference.</summary>
/// <param name="Negotiator">The quote negotiation that owns quoting state for the request.</param>
/// <param name="Detail">The request as the UI sees it (from Area 1).</param>
public sealed record QuoteContext(QuoteNegotiator Negotiator, RequestDetail Detail);

/// <summary>
/// Gives the quote adapter the domain state for a reference and takes back the results.
/// Implemented once by the bootstrap over whatever storage Area 1 uses; it never changes the Domain.
/// </summary>
public interface IQuoteRegistry
{
    /// <summary>Every request, as list rows (Area 1's RequestRegister).</summary>
    IReadOnlyList<RequestSummary> AllRequests();

    /// <summary>One customer's requests, newest first.</summary>
    IReadOnlyList<RequestSummary> RequestsForCustomer(string customerId);

    /// <summary>The negotiation and read model for a reference, or null when it is unknown.</summary>
    QuoteContext? Find(string reference);

    /// <summary>
    /// Called after every command that changed a negotiation, so storage and the read models
    /// (tracking, allocation queue) show the new status, quote versions and history entries.
    /// </summary>
    void Saved(string reference, Status status, IReadOnlyList<QuoteView> quotes, IReadOnlyList<EventView> newEvents);

    /// <summary>Hands the Delivery created on acceptance to Areas 3 and 4, with the amount to collect.</summary>
    void DeliveryCreated(string reference, Delivery delivery, decimal amountDue);
}

/// <summary>
/// Area 2 adapter: translates the web UI's quoting actions (A1 Tasks 2 and 3) into commands on
/// <see cref="QuoteNegotiator"/>, with prices from <see cref="ServiceArea"/> and its pricing
/// policies. Status changes only happen inside the domain, every command carries a replay key
/// (DA9), and refusals are returned as data so the Vue UI can show them.
/// </summary>
public sealed class QuoteServiceAdapter : IQuoteService
{
    private readonly IQuoteRegistry _registry;
    private readonly ServiceArea _area;
    private readonly Func<DateTimeOffset> _clock;
    private readonly IHttpContextAccessor? _http;

    /// <summary>Creates the adapter.</summary>
    /// <param name="registry">Supplies the domain state for a reference.</param>
    /// <param name="area">The service area, which selects the pricing policy.</param>
    /// <param name="clock">Supplies the time sent into the domain; the domain never reads a clock.</param>
    /// <param name="http">Supplies the Idempotency-Key header when the caller sends one.</param>
    public QuoteServiceAdapter(
        IQuoteRegistry registry,
        ServiceArea area,
        Func<DateTimeOffset>? clock = null,
        IHttpContextAccessor? http = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _area = area ?? throw new ArgumentNullException(nameof(area));
        _clock = clock ?? (() => DateTimeOffset.Now);
        _http = http;
    }

    private DateTimeOffset Now => _clock();

    /// <inheritdoc/>
    public IReadOnlyList<RequestSummary> AwaitingQuote()
    {
        var waiting = new List<RequestSummary>();
        foreach (RequestSummary row in _registry.AllRequests())
        {
            if (_registry.Find(row.Reference) is not { } context)
            {
                continue;
            }

            // Lazy expiry (A2 S3): an expired quote returns its request to this queue (A1 Task 2 variant 5a).
            ExpireIfDue(row.Reference, context);
            if (context.Negotiator.IsWaitingForQuote)
            {
                (string tag, string note) = QueueTag(context.Negotiator, row.LoggedAt);
                waiting.Add(row with { Status = context.Negotiator.Status, Note = note, Tag = tag });
            }
        }

        return waiting.OrderBy(r => r.LoggedAt).ToList();
    }

    /// <inheritdoc/>
    public OperationResult<QuoteProposal> Propose(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (_registry.Find(reference) is not { } context)
        {
            return OperationResult.Fail<QuoteProposal>($"No request has the reference {reference}.");
        }

        QuoteNegotiator negotiator = context.Negotiator;
        DateTimeOffset now = Now;
        try
        {
            PriceProposal price = negotiator.ProposePrice(_area, now);
            (TimeWindow pickup, TimeWindow delivery) = negotiator.ProposeWindows(now);
            return OperationResult.Ok(
                new QuoteProposal(reference, price.Price, price.NeedsOwnerApproval, ToView(pickup), ToView(delivery), price.Breakdown, Array.Empty<string>()),
                "Quote proposed.");
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail<QuoteProposal>(ex.Message);
        }
    }

    /// <inheritdoc/>
    public OperationResult IssueQuote(string reference, QuoteInput input, UserSession by)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(by);
        if (_registry.Find(reference) is not { } context)
        {
            return OperationResult.Fail($"No request has the reference {reference}.");
        }

        QuoteNegotiator negotiator = context.Negotiator;
        int nextVersion = negotiator.Quotes.Count + 1;
        return Run(reference, context, by, () =>
        {
            DateTimeOffset now = Now;
            PriceProposal proposal = negotiator.ProposePrice(_area, now);
            Quote quote = negotiator.IssueQuote(
                EventId(reference, $"issue-v{nextVersion}", by),
                Actor(by),
                now,
                proposal,
                input.AdjustedPrice,
                input.AdjustReason,
                ToDomain(input.PickupWindow),
                ToDomain(input.DeliveryWindow));

            if (quote.AwaitingOwnerApproval)
            {
                return $"Quote v{quote.Version} for {reference} sent to an owner for pricing. It will reach the customer once released.";
            }

            return $"Quote v{quote.Version} issued to {context.Detail.CustomerName} at {Display.Money(quote.FinalPrice!.Value)}, "
                + $"valid until {Display.When(quote.ValidUntil.LocalDateTime)}. Sent by SMS to {context.Detail.CustomerPhone}.";
        });
    }

    /// <inheritdoc/>
    public IReadOnlyList<RequestSummary> RequestsForCustomer(string customerId)
    {
        ArgumentNullException.ThrowIfNull(customerId);
        return _registry.RequestsForCustomer(customerId);
    }

    /// <inheritdoc/>
    public IReadOnlyList<QuoteView> QuoteHistory(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return _registry.Find(reference) is { } context ? Views(reference, context.Negotiator) : Array.Empty<QuoteView>();
    }

    /// <inheritdoc/>
    public OperationResult Accept(string reference, UserSession by, string? confirmedVia)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(by);
        if (_registry.Find(reference) is not { } context)
        {
            return OperationResult.Fail($"No request has the reference {reference}.");
        }

        QuoteNegotiator negotiator = context.Negotiator;
        Quote? quote = negotiator.CurrentQuote;
        if (quote is not null && ExpireIfDue(reference, context))
        {
            return OperationResult.Fail(
                $"This quote expired on {Display.When(quote.ValidUntil.LocalDateTime)} and cannot be accepted. "
                + "A new quote is needed; the request is back in the office queue.");
        }

        int version = quote?.Version ?? 0;
        return Run(reference, context, by, () =>
        {
            Delivery delivery = negotiator.Accept(EventId(reference, $"accept-v{version}", by), Actor(by), !IsCustomer(by), Now, ConfirmedVia(by, confirmedVia));
            decimal due = negotiator.CurrentQuote!.FinalPrice!.Value;
            _registry.DeliveryCreated(reference, delivery, due);
            return $"Quote accepted. {reference} is now Accepted and waiting for a delivery staff member. "
                + $"Payment of {Display.Money(due)} is taken at pickup.";
        });
    }

    /// <inheritdoc/>
    public OperationResult Reject(string reference, string reason, UserSession by, string? confirmedVia)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(by);
        if (_registry.Find(reference) is not { } context)
        {
            return OperationResult.Fail($"No request has the reference {reference}.");
        }

        int version = context.Negotiator.CurrentQuote?.Version ?? 0;
        return Run(reference, context, by, () =>
        {
            context.Negotiator.Reject(EventId(reference, $"reject-v{version}", by), Actor(by), !IsCustomer(by), Now, reason ?? string.Empty, ConfirmedVia(by, confirmedVia));
            return $"Quote rejected. {reference} is closed.";
        });
    }

    /// <inheritdoc/>
    public OperationResult RequestRevision(string reference, string reason, UserSession by, string? confirmedVia)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(by);
        if (_registry.Find(reference) is not { } context)
        {
            return OperationResult.Fail($"No request has the reference {reference}.");
        }

        int version = context.Negotiator.CurrentQuote?.Version ?? 0;
        return Run(reference, context, by, () =>
        {
            context.Negotiator.RequestRevision(EventId(reference, $"revise-v{version}", by), Actor(by), !IsCustomer(by), Now, reason ?? string.Empty, ConfirmedVia(by, confirmedVia));
            return $"Revision requested. {reference} is back in the office queue; quote v{version} is kept on record.";
        });
    }

    /// <inheritdoc/>
    public IReadOnlyList<RequestSummary> AwaitingOwnerApproval()
    {
        var held = new List<RequestSummary>();
        foreach (RequestSummary row in _registry.AllRequests())
        {
            if (_registry.Find(row.Reference) is { Negotiator.IsAwaitingOwner: true } context)
            {
                held.Add(row with { Note = $"out of area: {context.Negotiator.Subject.DestinationPostcode}" });
            }
        }

        return held.OrderBy(r => r.LoggedAt).ToList();
    }

    /// <inheritdoc/>
    public OperationResult ReleaseOwnerPrice(string reference, decimal price, UserSession owner)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(owner);
        if (!owner.Has(UserRole.Owner))
        {
            return OperationResult.Fail("Only an owner can price an out-of-area quote.");
        }

        if (_registry.Find(reference) is not { } context)
        {
            return OperationResult.Fail($"No request has the reference {reference}.");
        }

        int version = context.Negotiator.CurrentQuote?.Version ?? 0;
        return Run(reference, context, owner, () =>
        {
            context.Negotiator.ReleaseOwnerPrice(EventId(reference, $"release-v{version}", owner), Actor(owner), Now, price);
            Quote quote = context.Negotiator.CurrentQuote!;
            return $"Quote v{quote.Version} released to {context.Detail.CustomerName} at {Display.Money(price)}, "
                + $"valid until {Display.When(quote.ValidUntil.LocalDateTime)}.";
        });
    }

    /// <summary>The UI's view of one quote version.</summary>
    /// <param name="reference">The request reference.</param>
    /// <param name="quote">The quote version.</param>
    /// <param name="now">The time used to decide whether it has expired.</param>
    public static QuoteView ToView(string reference, Quote quote, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(quote);
        bool expired = quote.IsExpired(now);
        return new QuoteView(
            reference,
            quote.Version,
            quote.FinalPrice,
            quote.AdjustReason,
            ToView(quote.PickupWindow),
            ToView(quote.DeliveryWindow),
            quote.IssuedAt.LocalDateTime,
            quote.ValidUntil.LocalDateTime,
            expired,
            quote.AwaitingOwnerApproval,
            OutcomeText(quote, expired),
            quote.Decision.ToString(),
            quote.DecisionReason,
            quote.DecisionConfirmedVia);
    }

    private static string OutcomeText(Quote quote, bool expired)
    {
        if (quote.AwaitingOwnerApproval)
        {
            return "Waiting for owner price";
        }

        return quote.Decision switch
        {
            QuoteDecision.Pending => expired ? "Expired (no decision)" : "Issued, awaiting decision",
            QuoteDecision.Accepted => quote.DecisionRecordedByStaff ? $"Accepted (recorded by office staff{Via(quote)})" : "Accepted by customer",
            QuoteDecision.Rejected => $"Rejected: {quote.DecisionReason}{StaffNote(quote)}",
            QuoteDecision.RevisionRequested => $"Revision asked: {quote.DecisionReason}{StaffNote(quote)}",
            QuoteDecision.Expired => "Expired (no decision)",
            _ => quote.Decision.ToString(),
        };
    }

    private static string Via(Quote quote) =>
        quote.DecisionConfirmedVia is { } via ? $"; confirmed by {via}" : string.Empty;

    private static string StaffNote(Quote quote) =>
        quote.DecisionRecordedByStaff ? $" (recorded by office staff{Via(quote)})" : string.Empty;

    /// <summary>
    /// The confirmation note is only kept for decisions staff record; a customer deciding online
    /// never needs one. Longer notes are refused so the history stays readable.
    /// </summary>
    private static string? ConfirmedVia(UserSession by, string? confirmedVia)
    {
        if (IsCustomer(by))
        {
            return null;
        }

        string? trimmed = confirmedVia?.Trim();
        if (trimmed is { Length: > 0 and < 3 } || trimmed?.Length > 120)
        {
            throw new ArgumentException("Describe how the customer confirmed in 3 to 120 characters, e.g. \"phone call from Jane at 10:15\".");
        }

        return trimmed;
    }

    private static TimeWindowView ToView(TimeWindow window) => new(window.Start.LocalDateTime, window.End.LocalDateTime);

    private static TimeWindow ToDomain(TimeWindowView window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new TimeWindow(new DateTimeOffset(window.Start), new DateTimeOffset(window.End));
    }

    private static bool IsCustomer(UserSession by) => by.Has(UserRole.Customer) && !by.IsStaff;

    private static string Actor(UserSession by) => by.StaffId ?? by.CustomerId ?? by.Username;

    /// <summary>
    /// Why a request is in the quote queue, so office staff can see what is new and what needs a
    /// second look: never quoted, the customer asked for a revision, or the last quote expired.
    /// </summary>
    private (string Tag, string Note) QueueTag(QuoteNegotiator negotiator, DateTime loggedAt)
    {
        Quote? last = negotiator.CurrentQuote;
        return last?.Decision switch
        {
            QuoteDecision.RevisionRequested => (QueueTags.Revision, $"Revision asked: {last.DecisionReason}"),
            QuoteDecision.Expired => (QueueTags.Expired, $"Quote v{last.Version} expired with no decision"),
            _ => (QueueTags.New, $"waiting {Age(loggedAt)}"),
        };
    }

    private string Age(DateTime since)
    {
        TimeSpan age = Now.LocalDateTime - since;
        return age.TotalHours < 24
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, (int)age.TotalHours)} h")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalDays} d");
    }

    private List<QuoteView> Views(string reference, QuoteNegotiator negotiator)
    {
        DateTimeOffset now = Now;
        return negotiator.Quotes.Select(q => ToView(reference, q, now)).ToList();
    }

    /// <summary>Applies lazy expiry and saves the result when it changed anything.</summary>
    private bool ExpireIfDue(string reference, QuoteContext context)
    {
        QuoteNegotiator negotiator = context.Negotiator;
        int before = negotiator.History.Count;
        int version = negotiator.CurrentQuote?.Version ?? 0;
        if (!negotiator.ExpireIfDue(SystemEventId(reference, $"expire-v{version}"), Now))
        {
            return false;
        }

        Save(reference, negotiator, before, QuoteNegotiator.SystemActor);
        return true;
    }

    /// <summary>
    /// Runs one domain command, turns its refusals into a failed result, and saves the negotiation
    /// when it succeeded so the read models show the change.
    /// </summary>
    private OperationResult Run(string reference, QuoteContext context, UserSession by, Func<string> command)
    {
        int before = context.Negotiator.History.Count;
        string message;
        try
        {
            message = command();
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return OperationResult.Fail(ex.Message);
        }

        Save(reference, context.Negotiator, before, by.DisplayName);
        return OperationResult.Ok(message);
    }

    private void Save(string reference, QuoteNegotiator negotiator, int historyBefore, string actorName)
    {
        List<EventView> added = negotiator.History
            .Skip(historyBefore)
            .Select(e => new EventView(
                e.OccurredAt.LocalDateTime,
                e.From ?? negotiator.Status,
                e.To ?? negotiator.Status,
                e.ActorId == QuoteNegotiator.SystemActor ? QuoteNegotiator.SystemActor : actorName,
                e.Note))
            .ToList();
        _registry.Saved(reference, negotiator.Status, Views(reference, negotiator), added);
    }

    /// <summary>
    /// Builds the replay key (DA9) for one command: the Idempotency-Key header when the caller sends
    /// a valid Guid, otherwise a deterministic Guid from reference + action (which includes the quote
    /// version) + actor + minute, so a double-submitted POST collapses onto the domain's replay memory
    /// while issuing version 2 after version 1 is still a new command.
    /// </summary>
    private Guid EventId(string reference, string action, UserSession by)
    {
        string? header = _http?.HttpContext?.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header) && Guid.TryParse(header.Trim(), out Guid supplied))
        {
            return supplied;
        }

        long minute = Now.ToUnixTimeSeconds() / 60;
        return Hash(string.Create(CultureInfo.InvariantCulture, $"{reference}|{action}|{Actor(by)}|{minute}"));
    }

    /// <summary>The replay key for a change the system makes itself (lazy expiry of one quote version).</summary>
    private static Guid SystemEventId(string reference, string action) => Hash($"{reference}|{action}|system");

    private static Guid Hash(string seed) => new(SHA256.HashData(Encoding.UTF8.GetBytes(seed)).AsSpan(0, 16));
}
