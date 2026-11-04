using System.Globalization;

namespace Dms.Domain;

/// <summary>What the customer decided about a quote version.</summary>
public enum QuoteDecision
{
    /// <summary>Issued (or held for an owner) and waiting for a decision.</summary>
    Pending,

    /// <summary>Accepted: a Delivery now exists.</summary>
    Accepted,

    /// <summary>Rejected: the request is closed.</summary>
    Rejected,

    /// <summary>The customer asked for a revised quote (A1 Task 3 variant 2a).</summary>
    RevisionRequested,

    /// <summary>The validity period passed without a decision (A1 Task 2 variant 5a).</summary>
    Expired,
}

/// <summary>
/// A priced, time-windowed offer with a validity period (A2 CRC Quote). Every version is kept
/// against its request (variant 3a), and a version knows the earlier one it revises.
/// </summary>
public sealed class Quote
{
    /// <summary>Creates a new quote version.</summary>
    /// <param name="eventId">The command that issued it (DA9 replay key).</param>
    /// <param name="version">1 for the first quote on a request, then 2, 3...</param>
    /// <param name="proposedPrice">The policy's price, or null when an owner must set it.</param>
    /// <param name="adjustedPrice">The price office staff chose instead, if any.</param>
    /// <param name="adjustReason">Why the price was adjusted; required with <paramref name="adjustedPrice"/>.</param>
    /// <param name="issuedBy">Who issued it.</param>
    /// <param name="pickupWindow">Estimated pickup window.</param>
    /// <param name="deliveryWindow">Estimated delivery window; may not start before the pickup window.</param>
    /// <param name="issuedAt">When it was issued (or held for an owner).</param>
    /// <param name="validity">How long it may be accepted once issued.</param>
    /// <param name="revises">The earlier version this one replaces, if any.</param>
    public Quote(
        Guid eventId,
        int version,
        decimal? proposedPrice,
        decimal? adjustedPrice,
        string? adjustReason,
        string issuedBy,
        TimeWindow pickupWindow,
        TimeWindow deliveryWindow,
        DateTimeOffset issuedAt,
        TimeSpan validity,
        Quote? revises)
    {
        ArgumentNullException.ThrowIfNull(pickupWindow);
        ArgumentNullException.ThrowIfNull(deliveryWindow);
        if (deliveryWindow.Start < pickupWindow.Start)
        {
            throw new ArgumentException("The delivery window cannot start before the pickup window.");
        }

        if (adjustedPrice is not null)
        {
            if (adjustedPrice <= 0m)
            {
                throw new ArgumentException("An adjusted price must be more than $0.00.");
            }

            if (string.IsNullOrWhiteSpace(adjustReason))
            {
                throw new ArgumentException("A reason is required when the price is adjusted.");
            }
        }

        EventId = eventId;
        Version = version;
        ProposedPrice = proposedPrice;
        AdjustedPrice = adjustedPrice;
        AdjustReason = adjustedPrice is null ? null : adjustReason!.Trim();
        IssuedBy = issuedBy;
        PickupWindow = pickupWindow;
        DeliveryWindow = deliveryWindow;
        IssuedAt = issuedAt;
        Validity = validity;
        Revises = revises;
        AwaitingOwnerApproval = proposedPrice is null && adjustedPrice is null;
    }

    /// <summary>The command that issued this version.</summary>
    public Guid EventId { get; }

    /// <summary>Version number on its request.</summary>
    public int Version { get; }

    /// <summary>The policy's proposed price; null for an out-of-area quote until the owner prices it.</summary>
    public decimal? ProposedPrice { get; private set; }

    /// <summary>The price office staff set instead of the proposal, if any.</summary>
    public decimal? AdjustedPrice { get; }

    /// <summary>Why the price was adjusted.</summary>
    public string? AdjustReason { get; }

    /// <summary>Who issued it.</summary>
    public string IssuedBy { get; }

    /// <summary>Estimated pickup window.</summary>
    public TimeWindow PickupWindow { get; }

    /// <summary>Estimated delivery window.</summary>
    public TimeWindow DeliveryWindow { get; }

    /// <summary>When the validity period started: issue, or the owner's release.</summary>
    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>How long the quote may be accepted.</summary>
    public TimeSpan Validity { get; }

    /// <summary>When the quote stops being acceptable.</summary>
    public DateTimeOffset ValidUntil => IssuedAt + Validity;

    /// <summary>The earlier version this one revises (A1 Task 2 variant 3a).</summary>
    public Quote? Revises { get; }

    /// <summary>True while an owner still has to set the price (variant 2a).</summary>
    public bool AwaitingOwnerApproval { get; private set; }

    /// <summary>The owner who set the price, for an out-of-area quote.</summary>
    public string? PricedByOwner { get; private set; }

    /// <summary>The customer's decision so far.</summary>
    public QuoteDecision Decision { get; private set; } = QuoteDecision.Pending;

    /// <summary>Who recorded the decision.</summary>
    public string? DecidedBy { get; private set; }

    /// <summary>True when office staff recorded a decision given by phone or email (Task 3 variant 2c).</summary>
    public bool DecisionRecordedByStaff { get; private set; }

    /// <summary>When the decision was recorded.</summary>
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>The reason given for a rejection or a revision request.</summary>
    public string? DecisionReason { get; private set; }

    /// <summary>
    /// How the customer gave a decision that office staff recorded, e.g. "phone call from Jane at 10:15".
    /// Null when the customer decided online.
    /// </summary>
    public string? DecisionConfirmedVia { get; private set; }

    /// <summary>The price the customer pays: the adjusted price, else the proposal. Null while awaiting the owner.</summary>
    public decimal? FinalPrice => AdjustedPrice ?? ProposedPrice;

    /// <summary>
    /// True when the quote was issued, is still undecided and its validity has passed.
    /// Expiry is evaluated when the quote is next consulted, not by a timer (A2 S3).
    /// </summary>
    public bool IsExpired(DateTimeOffset at) =>
        Decision == QuoteDecision.Pending && !AwaitingOwnerApproval && at > ValidUntil;

    /// <summary>Records the owner's price for an out-of-area quote and starts its validity period.</summary>
    public void ReleaseOwnerPrice(decimal price, string ownerId, DateTimeOffset at)
    {
        if (!AwaitingOwnerApproval)
        {
            throw new InvalidOperationException($"Quote v{Version} is not waiting for an owner price.");
        }

        if (price <= 0m)
        {
            throw new ArgumentException("The owner's price must be more than $0.00.");
        }

        ProposedPrice = price;
        PricedByOwner = ownerId;
        AwaitingOwnerApproval = false;
        IssuedAt = at;
    }

    /// <summary>
    /// Records the customer's decision. Refused once decided, while waiting for an owner, for an
    /// acceptance after the quote has expired (A1 Task 3 variant 1a), and when office staff record a
    /// decision without saying how the customer gave it (Task 3 subtask 2).
    /// </summary>
    public void RecordDecision(QuoteDecision decision, string actorId, bool recordedByStaff, DateTimeOffset at, string? reason, string? confirmedVia = null)
    {
        if (decision == QuoteDecision.Pending)
        {
            throw new ArgumentException("A decision must be accept, reject, revise or expire.");
        }

        if (Decision != QuoteDecision.Pending)
        {
            throw new InvalidOperationException($"Quote v{Version} has already been decided ({Decision}).");
        }

        if (AwaitingOwnerApproval)
        {
            throw new InvalidOperationException($"Quote v{Version} is still waiting for an owner to set the price.");
        }

        if (decision == QuoteDecision.Accepted && IsExpired(at))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Quote v{Version} expired at {ValidUntil:dd/MM/yyyy HH:mm} and can no longer be accepted. A new quote is needed."));
        }

        if (recordedByStaff && string.IsNullOrWhiteSpace(confirmedVia))
        {
            throw new ArgumentException(
                "Say how the customer gave this decision, for example \"phone call from the customer at 10:15\".");
        }

        Decision = decision;
        DecidedBy = actorId;
        DecisionRecordedByStaff = recordedByStaff;
        DecidedAt = at;
        DecisionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        DecisionConfirmedVia = recordedByStaff ? confirmedVia!.Trim() : null;
    }

    /// <summary>
    /// Rebuilds a quote from stored data at start-up (A2 bootstrap stage 3), including any decision.
    /// </summary>
    public static Quote Restore(
        int version,
        decimal? price,
        string? adjustReason,
        TimeWindow pickupWindow,
        TimeWindow deliveryWindow,
        DateTimeOffset issuedAt,
        TimeSpan validity,
        QuoteDecision decision,
        string? decisionReason,
        Quote? revises)
    {
        bool adjusted = price is not null && !string.IsNullOrWhiteSpace(adjustReason);
        var quote = new Quote(
            Guid.Empty,
            version,
            adjusted ? null : price,
            adjusted ? price : null,
            adjustReason,
            "restored",
            pickupWindow,
            deliveryWindow,
            issuedAt,
            validity,
            revises);
        quote.Decision = decision;
        quote.DecisionReason = decisionReason;
        return quote;
    }
}
