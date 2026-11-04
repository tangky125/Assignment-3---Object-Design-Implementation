using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dms.Api.Contracts;
using Dms.Domain;

namespace Dms.Api.Demo;

/// <summary>
/// TEMPORARY STAND-IN for the real business layer, so the UI can be built, demonstrated and
/// tested before the domain classes exist. It keeps data in memory only and uses the real
/// <see cref="StatusLifecycle"/> for every status change. When DmsInitialiser can build the
/// real services, program.cs stops calling <see cref="Create"/> and this folder can be deleted.
/// </summary>
public sealed class DemoBackend : IAuthService, IRequestService, IQuoteService, IAllocationService, IDeliveryService, ITrackingService
{
    /// <summary>Sign-in details shown on the help screen.</summary>
    public static readonly IReadOnlyList<string> AccountsHelp = new[]
    {
        "All demo passwords are: demo123",
        "jane    - customer (Jane Citizen)",
        "olivia  - office staff",
        "sam     - owner and office staff",
        "ben     - delivery staff (works 07:00-19:00)",
        "ava     - delivery staff (works 08:00-20:00)",
        "raj     - delivery staff (works 15:00-22:00)",
    };

    private const string SystemActor = "System";
    private static readonly TimeSpan QuoteValidity = TimeSpan.FromHours(24);

    private readonly Func<DateTime> _clock;
    private readonly StatusLifecycle _lifecycle = new();
    private readonly List<CustomerRecord> _customers = new();
    private readonly List<StaffRecord> _staff = new();
    private readonly List<UserRecord> _users = new();
    private readonly List<RequestRecord> _requests = new();
    private int _nextReference = 1;
    private int _nextCustomer = 1;

    private DemoBackend(Func<DateTime> clock)
    {
        _clock = clock;
    }

    private DateTime Now => _clock();

    /// <summary>Builds the stand-in with demonstration data and returns it as the UI's services.</summary>
    public static DmsServices Create(Func<DateTime> clock)
    {
        var backend = new DemoBackend(clock);
        backend.Seed();
        return new DmsServices(backend, backend, backend, backend, backend, backend, backend._lifecycle);
    }

    // ---------------------------------------------------------------- IAuthService

    /// <inheritdoc/>
    public UserSession? SignIn(string username, string password)
    {
        UserRecord? user = _users.FirstOrDefault(u => u.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase));
        if (user is null || user.PasswordHash != Hash(password))
        {
            return null;
        }

        return new UserSession(user.Username, user.DisplayName, user.Roles, user.CustomerId, user.StaffId);
    }

    // ---------------------------------------------------------------- IRequestService

    /// <inheritdoc/>
    public IReadOnlyList<CustomerSummary> FindCustomers(string? phone, string? name) =>
        _customers
            .Where(c => (phone is not null && c.Phone == phone) || (name is not null && NameMatches(c.Name, name)))
            .Select(ToSummary)
            .ToList();

    /// <inheritdoc/>
    public CustomerSummary? GetCustomer(string customerId)
    {
        CustomerRecord? c = _customers.FirstOrDefault(x => x.Id == customerId);
        return c is null ? null : ToSummary(c);
    }

    /// <inheritdoc/>
    public OperationResult<CustomerSummary> RegisterCustomer(string name, string phone, string? email)
    {
        if (_customers.Any(c => c.Phone == phone))
        {
            return OperationResult.Fail<CustomerSummary>($"A customer with phone {phone} is already held. Search again and choose them.");
        }

        var customer = new CustomerRecord($"C{_nextCustomer++:000}", name, phone, email);
        _customers.Add(customer);
        return OperationResult.Ok(ToSummary(customer), $"Customer {name} registered.");
    }

    /// <inheritdoc/>
    public IReadOnlyList<AddressPair> RecentAddressPairs(string customerId) =>
        _requests
            .Where(r => r.CustomerId == customerId)
            .OrderByDescending(r => r.LoggedAt)
            .Select(r => new AddressPair(r.Pickup, r.Destination, r.Recipient))
            .DistinctBy(p => (p.Pickup, p.Destination))
            .Take(3)
            .ToList();

    /// <inheritdoc/>
    public OperationResult CheckPickupAddress(AddressInput pickup) =>
        Zone(pickup.Postcode) is null
            ? OperationResult.Fail($"Postcode {pickup.Postcode} is outside our Greater Melbourne service area. Parcels can only be collected inside it (postcodes 3000-3207).")
            : OperationResult.Ok("Pickup address is inside the service area.");

    /// <inheritdoc/>
    public OperationResult<string> SubmitRequest(RequestDraft draft, UserSession loggedBy)
    {
        OperationResult pickupCheck = CheckPickupAddress(draft.Pickup);
        if (!pickupCheck.Succeeded)
        {
            return OperationResult.Fail<string>(pickupCheck.Message);
        }

        if (draft.Parcels.Count == 0)
        {
            return OperationResult.Fail<string>("A request needs at least one parcel.");
        }

        if (draft.ReadyTime < Now)
        {
            return OperationResult.Fail<string>("The ready time has already passed. Change the pickup ready time.");
        }

        CustomerRecord customer = _customers.First(c => c.Id == draft.CustomerId);
        string reference = NewReference();
        var request = new RequestRecord(reference, customer.Id, Now, Actor(loggedBy), draft.Pickup, draft.PickupContact, draft.ReadyTime, draft.Destination, draft.Recipient, draft.Notes);
        int n = 1;
        foreach (ParcelInput p in draft.Parcels)
        {
            request.Parcels.Add(new ParcelRecord($"P{n++}", p.Size, p.Flags, p.Description));
        }

        request.Events.Add(new EventView(Now, null, Status.Requested, Actor(loggedBy), loggedBy.Has(UserRole.Customer) ? "Request logged online" : "Request logged by phone"));
        _requests.Add(request);
        return OperationResult.Ok(reference, $"Request {reference} submitted. A confirmation was sent to {customer.Phone}.");
    }

    /// <inheritdoc/>
    public OperationResult Cancel(string reference, string reason, UserSession by) => Command(reference, r =>
    {
        Move(r, Status.Cancelled, by, $"Cancelled: {reason}");
        r.StaffId = null;
        return OperationResult.Ok($"{reference} cancelled.");
    });

    // ---------------------------------------------------------------- IQuoteService

    /// <inheritdoc/>
    public IReadOnlyList<RequestSummary> AwaitingQuote() =>
        _requests
            .Where(r => r.Status == Status.Requested && r.CurrentQuote is not { AwaitingOwner: true })
            .OrderBy(r => r.LoggedAt)
            .Select(r => Summary(r, $"waiting {Age(r.LoggedAt)}"))
            .ToList();

    /// <inheritdoc/>
    public OperationResult<QuoteProposal> Propose(string reference)
    {
        RequestRecord? r = Get(reference);
        if (r is null || r.Status != Status.Requested)
        {
            return OperationResult.Fail<QuoteProposal>($"{reference} is not waiting for a quote.");
        }

        DateTime start = RoundUp(r.ReadyTime > Now ? r.ReadyTime : Now);
        var pickup = new TimeWindowView(start, start.AddHours(2));
        var delivery = new TimeWindowView(start.AddHours(1), start.AddHours(4));
        var warnings = new List<string>();
        if (!_staff.Any(s => s.IsDelivery && Eligibility(s, r, pickup.Start, delivery.End).Eligible))
        {
            warnings.Add("No delivery staff member is free for these windows. Offer the customer a later window.");
        }

        (decimal? price, List<string> breakdown) = Price(r);
        return OperationResult.Ok(
            new QuoteProposal(reference, price, price is null, pickup, delivery, breakdown, warnings),
            "Quote proposed.");
    }

    /// <inheritdoc/>
    public OperationResult IssueQuote(string reference, QuoteInput input, UserSession by) => Command(reference, r =>
    {
        if (input.PickupWindow.End <= input.PickupWindow.Start || input.DeliveryWindow.End <= input.DeliveryWindow.Start)
        {
            return OperationResult.Fail("Each window must end after it starts.");
        }

        if (input.DeliveryWindow.Start < input.PickupWindow.Start)
        {
            return OperationResult.Fail("The delivery window cannot start before the pickup window.");
        }

        if (input.AdjustedPrice is not null && string.IsNullOrWhiteSpace(input.AdjustReason))
        {
            return OperationResult.Fail("A reason is required when the price is adjusted.");
        }

        (decimal? proposed, _) = Price(r);
        int version = r.Quotes.Count + 1;
        var quote = new QuoteRecord(version, input.PickupWindow, input.DeliveryWindow, Now)
        {
            Price = input.AdjustedPrice ?? proposed,
            AdjustReason = input.AdjustReason,
            AwaitingOwner = proposed is null,
            Outcome = proposed is null ? "Waiting for owner price" : "Issued, awaiting decision",
        };
        r.Quotes.Add(quote);
        if (quote.AwaitingOwner)
        {
            r.Events.Add(new EventView(Now, r.Status, r.Status, Actor(by), $"Quote v{version} held for owner pricing (out of area)"));
            return OperationResult.Ok($"Quote v{version} for {reference} sent to an owner for pricing. It will reach the customer once released.");
        }

        Move(r, Status.Quoted, by, $"Quote v{version} issued at {Display.Money(quote.Price!.Value)}");
        CustomerRecord c = Customer(r);
        return OperationResult.Ok($"Quote v{version} issued to {c.Name} at {Display.Money(quote.Price!.Value)}, valid until {Display.When(quote.ValidUntil)}. Sent by SMS to {c.Phone}.");
    });

    /// <inheritdoc/>
    public IReadOnlyList<RequestSummary> RequestsForCustomer(string customerId) =>
        _requests.Where(r => r.CustomerId == customerId).OrderByDescending(r => r.LoggedAt).Select(r => Summary(r, null)).ToList();

    /// <inheritdoc/>
    public IReadOnlyList<QuoteView> QuoteHistory(string reference) =>
        Get(reference)?.Quotes.Select(q => ToView(reference, q)).ToList() ?? new List<QuoteView>();

    /// <inheritdoc/>
    public OperationResult Accept(string reference, UserSession by) => Command(reference, r =>
    {
        QuoteRecord? q = r.CurrentQuote;
        if (r.Status != Status.Quoted || q is null)
        {
            return OperationResult.Fail($"{reference} has no quote waiting for a decision.");
        }

        if (Now > q.ValidUntil)
        {
            q.Outcome = "Expired";
            Move(r, Status.Requested, null, $"Quote v{q.Version} expired without a decision; back in the quote queue");
            return OperationResult.Fail($"This quote expired on {Display.When(q.ValidUntil)} and cannot be accepted. A new quote is needed; the request is back in the office queue.");
        }

        q.Outcome = by.Has(UserRole.Customer) ? "Accepted by customer" : $"Accepted (recorded by {by.DisplayName})";
        Move(r, Status.Accepted, by, $"Quote v{q.Version} accepted");
        return OperationResult.Ok($"Quote accepted. {reference} is now Accepted and waiting for a delivery staff member. Payment of {Display.Money(q.Price ?? 0m)} is taken at pickup.");
    });

    /// <inheritdoc/>
    public OperationResult Reject(string reference, string reason, UserSession by) => Command(reference, r =>
    {
        QuoteRecord q = r.CurrentQuote ?? throw new InvalidOperationException("There is no quote to reject.");
        q.Outcome = $"Rejected: {reason}";
        Move(r, Status.Rejected, by, $"Quote v{q.Version} rejected: {reason}");
        return OperationResult.Ok($"Quote rejected. {reference} is closed.");
    });

    /// <inheritdoc/>
    public OperationResult RequestRevision(string reference, string reason, UserSession by) => Command(reference, r =>
    {
        QuoteRecord q = r.CurrentQuote ?? throw new InvalidOperationException("There is no quote to revise.");
        q.Outcome = $"Revision asked: {reason}";
        Move(r, Status.Requested, by, $"Revised quote requested: {reason}");
        return OperationResult.Ok($"Revision requested. {reference} is back in the office queue; quote v{q.Version} is kept on record.");
    });

    /// <inheritdoc/>
    public IReadOnlyList<RequestSummary> AwaitingOwnerApproval() =>
        _requests
            .Where(r => r.Status == Status.Requested && r.CurrentQuote is { AwaitingOwner: true })
            .OrderBy(r => r.LoggedAt)
            .Select(r => Summary(r, $"out of area: {r.Destination.Postcode}"))
            .ToList();

    /// <inheritdoc/>
    public OperationResult ReleaseOwnerPrice(string reference, decimal price, UserSession owner) => Command(reference, r =>
    {
        if (!owner.Has(UserRole.Owner))
        {
            return OperationResult.Fail("Only an owner can price an out-of-area quote.");
        }

        QuoteRecord q = r.CurrentQuote is { AwaitingOwner: true } held
            ? held
            : throw new InvalidOperationException($"{reference} has no quote waiting for an owner.");
        q.Price = price;
        q.AwaitingOwner = false;
        q.IssuedAt = Now;
        q.Outcome = "Issued, awaiting decision";
        Move(r, Status.Quoted, owner, $"Owner priced quote v{q.Version} at {Display.Money(price)}");
        return OperationResult.Ok($"Quote v{q.Version} released to {Customer(r).Name} at {Display.Money(price)}, valid until {Display.When(q.ValidUntil)}.");
    });

    // ---------------------------------------------------------------- IAllocationService

    /// <inheritdoc/>
    public IReadOnlyList<RequestSummary> AwaitingAllocation() =>
        _requests
            .Where(r => r.Status is Status.Accepted or Status.DeliveryFailed)
            .OrderBy(r => r.CurrentQuote?.PickupWindow.Start)
            .Select(r => Summary(r, r.Status == Status.DeliveryFailed ? "re-allocate after failure" : null))
            .ToList();

    /// <inheritdoc/>
    public IReadOnlyList<RequestSummary> ScheduledDeliveries() =>
        _requests
            .Where(r => r.Status == Status.Scheduled)
            .OrderBy(r => r.CurrentQuote?.PickupWindow.Start)
            .Select(r => Summary(r, StaffName(r.StaffId)))
            .ToList();

    /// <inheritdoc/>
    public IReadOnlyList<StaffOption> StaffOptions(string reference)
    {
        RequestRecord? r = Get(reference);
        if (r?.CurrentQuote is null)
        {
            return new List<StaffOption>();
        }

        return _staff
            .Where(s => s.IsDelivery && s.Active)
            .Select(s =>
            {
                (bool ok, string why) = s.Id == r.StaffId
                    ? (false, "Already allocated to this delivery")
                    : Eligibility(s, r, r.CurrentQuote.PickupWindow.Start, r.CurrentQuote.DeliveryWindow.End);
                return new StaffOption(s.Id, s.Name, ok, why);
            })
            .ToList();
    }

    /// <inheritdoc/>
    public OperationResult Allocate(string reference, string staffId, UserSession by) => Command(reference, r =>
    {
        StaffRecord staff = _staff.First(s => s.Id == staffId);
        QuoteRecord q = r.CurrentQuote ?? throw new InvalidOperationException("The delivery has no accepted quote.");
        (bool ok, string why) = Eligibility(staff, r, q.PickupWindow.Start, q.DeliveryWindow.End);
        if (!ok)
        {
            return OperationResult.Fail($"{staff.Name} is no longer free: {why}. Nothing was saved.");
        }

        Move(r, Status.Scheduled, by, $"Allocated to {staff.Name}");
        r.StaffId = staff.Id;
        return OperationResult.Ok($"{reference} allocated to {staff.Name}. Status: Scheduled. It is now in {staff.Name}'s job list.");
    });

    /// <inheritdoc/>
    public OperationResult Replace(string reference, string newStaffId, string reason, UserSession by) => Command(reference, r =>
    {
        if (r.Status != Status.Scheduled)
        {
            return OperationResult.Fail($"{reference} is not scheduled, so there is no staff member to replace.");
        }

        StaffRecord staff = _staff.First(s => s.Id == newStaffId);
        QuoteRecord q = r.CurrentQuote!;
        (bool ok, string why) = Eligibility(staff, r, q.PickupWindow.Start, q.DeliveryWindow.End);
        if (!ok)
        {
            return OperationResult.Fail($"{staff.Name} is not free: {why}. Nothing was changed.");
        }

        string previous = StaffName(r.StaffId);
        r.StaffId = staff.Id;
        r.Events.Add(new EventView(Now, r.Status, r.Status, Actor(by), $"Re-allocated from {previous} to {staff.Name}: {reason}"));
        return OperationResult.Ok($"{reference} moved from {previous} to {staff.Name}. Status stays Scheduled.");
    });

    // ---------------------------------------------------------------- IDeliveryService

    /// <inheritdoc/>
    public IReadOnlyList<RequestSummary> JobsFor(string staffId) =>
        _requests
            .Where(r => r.StaffId == staffId && r.Status is Status.Scheduled or Status.Collected)
            .OrderBy(r => r.CurrentQuote?.PickupWindow.Start)
            .Select(r => Summary(r, r.Status == Status.Collected ? "deliver next" : "pick up"))
            .ToList();

    /// <inheritdoc/>
    public decimal AmountDue(string reference) => Get(reference)?.CurrentQuote?.Price ?? 0m;

    /// <inheritdoc/>
    public OperationResult SetParcelCollected(string reference, string parcelId, bool collected, UserSession by) => Command(reference, r =>
    {
        if (r.Status != Status.Scheduled)
        {
            return OperationResult.Fail("Parcels can only be ticked off while the job is at the pickup stage.");
        }

        ParcelRecord parcel = r.Parcels.First(p => p.Id == parcelId);
        parcel.Collected = collected;
        int number = r.Parcels.IndexOf(parcel) + 1;
        int ticked = r.Parcels.Count(p => p.Collected);
        return OperationResult.Ok($"Parcel {number} {(collected ? "ticked as collected" : "unticked")} ({ticked} of {r.Parcels.Count} ticked).");
    });

    /// <inheritdoc/>
    public PaymentOutcomeView TakeCash(string reference, decimal amountReceived, UserSession by)
    {
        RequestRecord? r = Get(reference);
        if (r is null || r.Status != Status.Scheduled)
        {
            return new PaymentOutcomeView(false, "Payment is taken at pickup, while the job is Scheduled.", null);
        }

        if (r.Payments.Any(p => p.Successful))
        {
            return new PaymentOutcomeView(false, "Payment has already been received for this delivery.", null);
        }

        decimal due = AmountDue(reference);
        if (amountReceived < due)
        {
            return new PaymentOutcomeView(false, $"Cash received ({Display.Money(amountReceived)}) is less than the amount due ({Display.Money(due)}). Collect the full amount or use a card.", null);
        }

        r.Payments.Add(new PaymentView(Now, "Cash", due, true, "Received"));
        r.Events.Add(new EventView(Now, r.Status, r.Status, Actor(by), $"Cash payment {Display.Money(due)} received"));
        return new PaymentOutcomeView(true, $"Cash payment of {Display.Money(due)} recorded.", amountReceived - due);
    }

    /// <inheritdoc/>
    public PaymentOutcomeView TakeCard(string reference, bool approved, UserSession by)
    {
        RequestRecord? r = Get(reference);
        if (r is null || r.Status != Status.Scheduled)
        {
            return new PaymentOutcomeView(false, "Payment is taken at pickup, while the job is Scheduled.", null);
        }

        if (r.Payments.Any(p => p.Successful))
        {
            return new PaymentOutcomeView(false, "Payment has already been received for this delivery.", null);
        }

        decimal due = AmountDue(reference);
        r.Payments.Add(new PaymentView(Now, "Card", due, approved, approved ? "Approved" : "Declined"));
        r.Events.Add(new EventView(Now, r.Status, r.Status, Actor(by), $"Card payment {Display.Money(due)} {(approved ? "approved" : "declined")}"));
        return approved
            ? new PaymentOutcomeView(true, $"Card payment of {Display.Money(due)} approved.", null)
            : new PaymentOutcomeView(false, "Card payment declined. The attempt is kept on record. Ask for another card or take cash.", null);
    }

    /// <inheritdoc/>
    public OperationResult ConfirmCollection(string reference, UserSession by) => Command(reference, r =>
    {
        ParcelRecord? missing = r.Parcels.FirstOrDefault(p => !p.Collected);
        if (missing is not null)
        {
            return OperationResult.Fail($"Parcel {r.Parcels.IndexOf(missing) + 1} is not ticked as collected. Tick every parcel before confirming pickup.");
        }

        if (!r.Payments.Any(p => p.Successful))
        {
            return OperationResult.Fail("Payment has not been taken. Payment is taken at pickup, before the parcels leave.");
        }

        Move(r, Status.Collected, by, "All parcels collected and payment received");
        return OperationResult.Ok($"Pickup confirmed. {reference} is now Collected. The customer has been notified.");
    });

    /// <inheritdoc/>
    public OperationResult RecordDelivered(string reference, string receivedBy, UserSession by) => Command(reference, r =>
    {
        Move(r, Status.Delivered, by, $"Delivered; received by {receivedBy}");
        return OperationResult.Ok($"Delivery completed. {reference} is now Delivered. The customer has been notified.");
    });

    /// <inheritdoc/>
    public OperationResult RecordFailure(string reference, string reason, UserSession by) => Command(reference, r =>
    {
        bool failedAtPickup = r.Status == Status.Scheduled;
        Move(r, Status.DeliveryFailed, by, reason);
        r.StaffId = null;
        if (failedAtPickup)
        {
            // Nothing left the pickup address, so the next attempt starts with every parcel unticked.
            r.Parcels.ForEach(p => p.Collected = false);
        }

        return OperationResult.Ok($"Failure recorded: {reason}. {reference} is back in the allocation queue for office staff to reschedule.");
    });

    // ---------------------------------------------------------------- ITrackingService

    /// <inheritdoc/>
    public RequestDetail? Find(string reference)
    {
        RequestRecord? r = Get(reference);
        if (r is null)
        {
            return null;
        }

        CustomerRecord c = Customer(r);
        return new RequestDetail(
            r.Reference,
            c.Id,
            c.Name,
            c.Phone,
            r.Status,
            r.LoggedAt,
            r.LoggedBy,
            r.Pickup,
            r.PickupContact,
            r.ReadyTime,
            r.Destination,
            r.Recipient,
            r.Parcels.Select(p => new ParcelView(p.Id, p.Size, p.Flags, p.Description, p.Collected)).ToList(),
            r.Notes,
            r.CurrentQuote is null ? null : ToView(r.Reference, r.CurrentQuote),
            r.StaffId,
            r.StaffId is null ? null : StaffName(r.StaffId),
            r.Payments.ToList(),
            r.Events.ToList());
    }

    /// <inheritdoc/>
    public IReadOnlyList<RequestSummary> Search(string? reference, string? name, string? phone, DateOnly? loggedOn) =>
        _requests
            .Where(r => reference is null || r.Reference.Equals(reference, StringComparison.OrdinalIgnoreCase))
            .Where(r => name is null || NameMatches(Customer(r).Name, name))
            .Where(r => phone is null || Customer(r).Phone == phone || r.Recipient.Phone == phone)
            .Where(r => loggedOn is null || DateOnly.FromDateTime(r.LoggedAt) == loggedOn)
            .OrderByDescending(r => r.LoggedAt)
            .Select(r => Summary(r, null))
            .ToList();

    /// <inheritdoc/>
    public IReadOnlyList<RequestSummary> OpenRequests() =>
        _requests
            .Where(r => !_lifecycle.IsFinal(r.Status))
            .OrderBy(r => r.LoggedAt)
            .Select(r => Summary(r, $"open {Age(r.LoggedAt)}"))
            .ToList();

    /// <inheritdoc/>
    public ViewerLevel LevelFor(UserSession? viewer, string reference)
    {
        RequestRecord? r = Get(reference);
        if (r is null)
        {
            return ViewerLevel.None;
        }

        if (viewer is null)
        {
            return ViewerLevel.Recipient;
        }

        if (viewer.IsStaff)
        {
            return ViewerLevel.Staff;
        }

        return viewer.CustomerId == r.CustomerId ? ViewerLevel.Customer : ViewerLevel.None;
    }

    // ---------------------------------------------------------------- read-model mirror for real areas

    /// <summary>
    /// Copies the result of a real Area 2 command into this stand-in's request record, so tracking,
    /// customer lists and the allocation queue (still answered here) show the new status, quote
    /// versions and history. The real domain already checked the move, so no lifecycle check here.
    /// </summary>
    /// <param name="reference">The request reference.</param>
    /// <param name="status">The status the domain moved the request to.</param>
    /// <param name="quotes">Every quote version, as the UI sees them.</param>
    /// <param name="newEvents">History entries the command added.</param>
    public void MirrorQuoteState(string reference, Status status, IReadOnlyList<QuoteView> quotes, IReadOnlyList<EventView> newEvents)
    {
        ArgumentNullException.ThrowIfNull(quotes);
        ArgumentNullException.ThrowIfNull(newEvents);
        RequestRecord? r = Get(reference);
        if (r is null)
        {
            return;
        }

        r.Quotes.Clear();
        foreach (QuoteView q in quotes)
        {
            r.Quotes.Add(new QuoteRecord(q.Version, q.PickupWindow, q.DeliveryWindow, q.IssuedAt)
            {
                Price = q.Price,
                AdjustReason = q.AdjustReason,
                AwaitingOwner = q.AwaitingOwnerApproval,
                Outcome = q.IsExpired ? "Issued, awaiting decision" : q.Outcome,
            });
        }

        r.Status = status;
        r.Events.AddRange(newEvents);
    }

    // ---------------------------------------------------------------- helpers

    private static string Hash(string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));

    private static string? Zone(string postcode)
    {
        if (!int.TryParse(postcode, NumberStyles.None, CultureInfo.InvariantCulture, out int pc))
        {
            return null;
        }

        return pc switch
        {
            >= 3000 and <= 3099 => "A",
            >= 3100 and <= 3199 => "B",
            >= 3200 and <= 3207 => "C",
            _ => null,
        };
    }

    private static DateTime RoundUp(DateTime t)
    {
        var rounded = new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Kind);
        return rounded < t ? rounded.AddMinutes(t.Minute <= 30 ? 30 : 60) : rounded;
    }

    private static bool NameMatches(string fullName, string query)
    {
        string q = query.Trim().ToUpperInvariant();
        if (q.Length == 0)
        {
            return false;
        }

        string n = fullName.ToUpperInvariant();
        if (n.Contains(q, StringComparison.Ordinal))
        {
            return true;
        }

        // Close match for small spelling differences, e.g. "Citizn" finds "Citizen".
        return n.Split(' ', StringSplitOptions.RemoveEmptyEntries).Append(n)
            .Any(word => 1.0 - ((double)Distance(word, q) / Math.Max(word.Length, q.Length)) >= 0.75);
    }

    private static int Distance(string a, string b)
    {
        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (int j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }

        return d[a.Length, b.Length];
    }

    private static CustomerSummary ToSummary(CustomerRecord c) => new(c.Id, c.Name, c.Phone, c.Email);

    private static string Actor(UserSession? by) => by is null ? SystemActor : by.DisplayName;

    private OperationResult Command(string reference, Func<RequestRecord, OperationResult> action)
    {
        RequestRecord? r = Get(reference);
        if (r is null)
        {
            return OperationResult.Fail($"No request has the reference {reference}.");
        }

        try
        {
            return action(r);
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    /// <summary>Changes status only through the StatusLifecycle check (team contract rule 2).</summary>
    private void Move(RequestRecord r, Status next, UserSession? by, string note)
    {
        if (!_lifecycle.CanTransition(r.Status, next))
        {
            throw new InvalidOperationException(_lifecycle.ExplainRefusal(r.Status, next));
        }

        r.Events.Add(new EventView(Now, r.Status, next, Actor(by), note));
        r.Status = next;
    }

    private (bool Eligible, string Reason) Eligibility(StaffRecord s, RequestRecord job, DateTime start, DateTime end)
    {
        if (!s.Active)
        {
            return (false, "Has left the business");
        }

        bool sameDay = start.Date == end.Date;
        if (!sameDay || TimeOnly.FromDateTime(start) < s.From || TimeOnly.FromDateTime(end) > s.To)
        {
            return (false, $"Not working then (works {s.From:HH:mm}-{s.To:HH:mm})");
        }

        RequestRecord? clash = _requests.FirstOrDefault(o =>
            o != job
            && o.StaffId == s.Id
            && o.Status is Status.Scheduled or Status.Collected
            && o.CurrentQuote is not null
            && o.CurrentQuote.PickupWindow.Start < end
            && start < o.CurrentQuote.DeliveryWindow.End);
        if (clash is not null)
        {
            TimeWindowView p = clash.CurrentQuote!.PickupWindow;
            return (false, $"Busy with {clash.Reference} ({p.Start:HH:mm}-{clash.CurrentQuote.DeliveryWindow.End:HH:mm})");
        }

        return (true, "Free for both windows");
    }

    private (decimal? Price, List<string> Breakdown) Price(RequestRecord r)
    {
        var lines = new List<string>();
        string? pickupZone = Zone(r.Pickup.Postcode);
        string? destinationZone = Zone(r.Destination.Postcode);
        if (pickupZone is null || destinationZone is null)
        {
            lines.Add($"Destination postcode {r.Destination.Postcode} is outside the zones with set prices.");
            return (null, lines);
        }

        string zone = string.CompareOrdinal(pickupZone, destinationZone) >= 0 ? pickupZone : destinationZone;
        decimal price = zone switch { "A" => 15m, "B" => 22m, _ => 30m };
        lines.Add($"Zone {zone} base price: {Display.Money(price)}");
        int extraParcels = r.Parcels.Count - 1;
        if (extraParcels > 0)
        {
            price += 4m * extraParcels;
            lines.Add($"Extra parcels x{extraParcels}: +{Display.Money(4m * extraParcels)}");
        }

        foreach ((SizeBand size, decimal extra) in new[] { (SizeBand.Medium, 5m), (SizeBand.Large, 12m) })
        {
            int count = r.Parcels.Count(p => p.Size == size);
            if (count > 0)
            {
                price += extra * count;
                lines.Add($"{size} parcels x{count}: +{Display.Money(extra * count)}");
            }
        }

        foreach ((ParcelHandling flag, decimal extra, string label) in new[]
        {
            (ParcelHandling.Fragile, 5m, "Fragile handling"),
            (ParcelHandling.SignatureRequired, 2m, "Signature on delivery"),
            (ParcelHandling.KeepUpright, 3m, "Keep upright"),
        })
        {
            int count = r.Parcels.Count(p => p.Flags.HasFlag(flag));
            if (count > 0)
            {
                price += extra * count;
                lines.Add($"{label} x{count}: +{Display.Money(extra * count)}");
            }
        }

        if (r.ReadyTime < Now.AddHours(2))
        {
            price = decimal.Round(price * 1.5m, 2);
            lines.Add("Express (ready within 2 hours): x1.5");
        }

        return (price, lines);
    }

    private QuoteView ToView(string reference, QuoteRecord q) =>
        new(
            reference,
            q.Version,
            q.Price,
            q.AdjustReason,
            q.PickupWindow,
            q.DeliveryWindow,
            q.IssuedAt,
            q.ValidUntil,
            q.Outcome.StartsWith("Issued", StringComparison.Ordinal) && Now > q.ValidUntil,
            q.AwaitingOwner,
            q.Outcome.StartsWith("Issued", StringComparison.Ordinal) && Now > q.ValidUntil ? "Expired (no decision)" : q.Outcome);

    private RequestSummary Summary(RequestRecord r, string? note) =>
        new(r.Reference, Customer(r).Name, r.Status, r.LoggedAt, r.Pickup.Suburb, r.Destination.Suburb, r.Parcels.Count, r.CurrentQuote?.PickupWindow, note);

    private string Age(DateTime since)
    {
        TimeSpan age = Now - since;
        return age.TotalHours < 24
            ? $"{Math.Max(0, (int)age.TotalHours)} h"
            : $"{(int)age.TotalDays} d";
    }

    private RequestRecord? Get(string reference) =>
        _requests.FirstOrDefault(r => r.Reference.Equals(reference.Trim(), StringComparison.OrdinalIgnoreCase));

    private CustomerRecord Customer(RequestRecord r) => _customers.First(c => c.Id == r.CustomerId);

    private string StaffName(string? staffId) => _staff.FirstOrDefault(s => s.Id == staffId)?.Name ?? "-";

    private string NewReference() => $"DMS-{Now.Year}-{_nextReference++:0000}";

    // ---------------------------------------------------------------- demonstration data

    private void Seed()
    {
        DateTime now = Now;
        DateTime tomorrow = now.Date.AddDays(1);

        _customers.Add(new CustomerRecord("C001", "Jane Citizen", "0412 345 678", "jane@example.com"));
        _customers.Add(new CustomerRecord("C002", "Acme Florist Pty Ltd", "03 9123 4567", "orders@acmeflorist.example"));
        _customers.Add(new CustomerRecord("C003", "Jon Citizen", "0400 111 222", null));
        _nextCustomer = 4;

        _staff.Add(new StaffRecord("S001", "Olivia Brown", false, TimeOnly.MinValue, TimeOnly.MaxValue));
        _staff.Add(new StaffRecord("S002", "Sam Taylor", false, TimeOnly.MinValue, TimeOnly.MaxValue));
        _staff.Add(new StaffRecord("S003", "Ben Nguyen", true, new TimeOnly(7, 0), new TimeOnly(19, 0)));
        _staff.Add(new StaffRecord("S004", "Ava Patel", true, new TimeOnly(8, 0), new TimeOnly(20, 0)));
        _staff.Add(new StaffRecord("S005", "Raj Singh", true, new TimeOnly(15, 0), new TimeOnly(22, 0)));

        string pw = Hash("demo123");
        _users.Add(new UserRecord("jane", pw, "Jane Citizen", new HashSet<UserRole> { UserRole.Customer }, "C001", null));
        _users.Add(new UserRecord("olivia", pw, "Olivia Brown", new HashSet<UserRole> { UserRole.OfficeStaff }, null, "S001"));
        _users.Add(new UserRecord("sam", pw, "Sam Taylor", new HashSet<UserRole> { UserRole.Owner, UserRole.OfficeStaff }, null, "S002"));
        _users.Add(new UserRecord("ben", pw, "Ben Nguyen", new HashSet<UserRole> { UserRole.DeliveryStaff }, null, "S003"));
        _users.Add(new UserRecord("ava", pw, "Ava Patel", new HashSet<UserRole> { UserRole.DeliveryStaff }, null, "S004"));
        _users.Add(new UserRecord("raj", pw, "Raj Singh", new HashSet<UserRole> { UserRole.DeliveryStaff }, null, "S005"));

        var swanston = new AddressInput("10 Swanston St", "Melbourne", "3000");
        var chapel = new AddressInput("5 Chapel St", "Windsor", "3181");
        var boxHill = new AddressInput("22 Station St", "Box Hill", "3128");
        var kew = new AddressInput("8 High St", "Kew", "3101");
        var acme = new AddressInput("77 Bridge Rd", "Richmond", "3121");
        var geelong = new AddressInput("1 Moorabool St", "Geelong", "3220");
        var tom = new ContactInput("Tom Lee", "0498 765 432");
        var jane = new ContactInput("Jane Citizen", "0412 345 678");
        var acmeDesk = new ContactInput("Acme front desk", "03 9123 4567");

        // 0001: Jane, waiting for a quote (oldest in the queue).
        RequestRecord r1 = SeedRequest("C001", now.AddHours(-26), "Jane Citizen", swanston, jane, tomorrow.AddHours(12), chapel, tom, "Leave with reception if Tom is out");
        r1.Parcels.Add(new ParcelRecord("P1", SizeBand.Small, ParcelHandling.Fragile, "Glass vase"));
        r1.Parcels.Add(new ParcelRecord("P2", SizeBand.Medium, ParcelHandling.None, "Books"));

        // 0002: Acme, destination out of area (needs owner pricing).
        RequestRecord r2 = SeedRequest("C002", now.AddHours(-3), "Olivia Brown", acme, acmeDesk, tomorrow.AddHours(13), geelong, new ContactInput("Geelong Events", "03 5222 1000"), null);
        r2.Parcels.Add(new ParcelRecord("P1", SizeBand.Large, ParcelHandling.KeepUpright, "Flower arrangement"));

        // 0003: Jane, quoted 25 hours ago: the quote has expired.
        RequestRecord r3 = SeedRequest("C001", now.AddHours(-50), "Jane Citizen", boxHill, jane, tomorrow.AddHours(15), kew, new ContactInput("Mia Wong", "0433 222 111"), null);
        r3.Parcels.Add(new ParcelRecord("P1", SizeBand.Small, ParcelHandling.None, "Documents"));
        SeedQuote(r3, 22m, tomorrow.AddHours(15), now.AddHours(-25), "Olivia Brown");

        // 0004: Acme, accepted and waiting for a staff member (Ben is free, Ava is busy, Raj is off).
        RequestRecord r4 = SeedRequest("C002", now.AddHours(-27), "Olivia Brown", acme, acmeDesk, tomorrow.AddHours(10), swanston, new ContactInput("Hotel concierge", "03 9650 1234"), null);
        r4.Parcels.Add(new ParcelRecord("P1", SizeBand.Medium, ParcelHandling.Fragile, "Table flowers"));
        SeedQuote(r4, 30m, tomorrow.AddHours(10), now.AddHours(-20), "Olivia Brown");
        SeedMove(r4, Status.Accepted, now.AddHours(-19), "Quote v1 accepted", "Acme Florist Pty Ltd");

        // 0005: Jane, scheduled to Ava at an overlapping time.
        RequestRecord r5 = SeedRequest("C001", now.AddHours(-30), "Jane Citizen", swanston, jane, tomorrow.AddHours(11), kew, new ContactInput("Mia Wong", "0433 222 111"), null);
        r5.Parcels.Add(new ParcelRecord("P1", SizeBand.Small, ParcelHandling.None, null));
        SeedQuote(r5, 22m, tomorrow.AddHours(11), now.AddHours(-20), "Olivia Brown");
        SeedMove(r5, Status.Accepted, now.AddHours(-19), "Quote v1 accepted", "Jane Citizen");
        SeedMove(r5, Status.Scheduled, now.AddHours(-18), "Allocated to Ava Patel", "Olivia Brown");
        r5.StaffId = "S004";

        // 0006: Jane, scheduled to Ben early tomorrow: used to demonstrate executing a delivery.
        RequestRecord r6 = SeedRequest("C001", now.AddHours(-31), "Jane Citizen", swanston, jane, tomorrow.AddHours(7), chapel, tom, "Gate code 1234");
        r6.Parcels.Add(new ParcelRecord("P1", SizeBand.Small, ParcelHandling.SignatureRequired, "Contract"));
        r6.Parcels.Add(new ParcelRecord("P2", SizeBand.Medium, ParcelHandling.None, "Laptop box"));
        SeedQuote(r6, 34m, tomorrow.AddHours(7), now.AddHours(-20), "Olivia Brown", pickupHours: 1, deliveryStartHours: 1, deliveryEndHours: 2.5);
        SeedMove(r6, Status.Accepted, now.AddHours(-19), "Quote v1 accepted", "Jane Citizen");
        SeedMove(r6, Status.Scheduled, now.AddHours(-18), "Allocated to Ben Nguyen", "Olivia Brown");
        r6.StaffId = "S003";

        // 0007: Jane, delivered yesterday by Ben (complete history and a receipt).
        RequestRecord r7 = SeedRequest("C001", now.AddDays(-3), "Jane Citizen", swanston, jane, now.Date.AddDays(-1).AddHours(9), boxHill, new ContactInput("Box Hill Clinic", "03 9890 0000"), null);
        r7.Parcels.Add(new ParcelRecord("P1", SizeBand.Medium, ParcelHandling.None, "Bouquet") { Collected = true });
        SeedQuote(r7, 27m, now.Date.AddDays(-1).AddHours(9), now.AddDays(-3).AddHours(1), "Olivia Brown");
        SeedMove(r7, Status.Accepted, now.AddDays(-3).AddHours(2), "Quote v1 accepted", "Jane Citizen");
        SeedMove(r7, Status.Scheduled, now.AddDays(-2), "Allocated to Ben Nguyen", "Olivia Brown");
        r7.Payments.Add(new PaymentView(now.Date.AddDays(-1).AddHours(9.2), "Card", 27m, true, "Approved"));
        SeedMove(r7, Status.Collected, now.Date.AddDays(-1).AddHours(9.25), "All parcels collected and payment received", "Ben Nguyen");
        SeedMove(r7, Status.Delivered, now.Date.AddDays(-1).AddHours(10.5), "Delivered; received by Dr Patel", "Ben Nguyen");
        r7.StaffId = "S003";

        // 0008: Jane, scheduled to Ben but the pickup window has passed: shows as running late.
        RequestRecord r8 = SeedRequest("C001", now.AddHours(-32), "Jane Citizen", boxHill, jane, now.AddHours(-3), swanston, new ContactInput("Front desk", "03 9600 0000"), null);
        r8.Parcels.Add(new ParcelRecord("P1", SizeBand.Small, ParcelHandling.None, "Keys"));
        SeedQuote(r8, 22m, now.AddHours(-3), now.AddHours(-23), "Olivia Brown");
        SeedMove(r8, Status.Accepted, now.AddHours(-22), "Quote v1 accepted", "Jane Citizen");
        SeedMove(r8, Status.Scheduled, now.AddHours(-21), "Allocated to Ben Nguyen", "Olivia Brown");
        r8.StaffId = "S003";
    }

    private RequestRecord SeedRequest(string customerId, DateTime loggedAt, string loggedBy, AddressInput pickup, ContactInput pickupContact, DateTime ready, AddressInput destination, ContactInput recipient, string? notes)
    {
        var r = new RequestRecord(NewReference(), customerId, loggedAt, loggedBy, pickup, pickupContact, ready, destination, recipient, notes);
        r.Events.Add(new EventView(loggedAt, null, Status.Requested, loggedBy, "Request logged"));
        _requests.Add(r);
        return r;
    }

    private void SeedQuote(RequestRecord r, decimal price, DateTime pickupStart, DateTime issuedAt, string by, double pickupHours = 2, double deliveryStartHours = 2, double deliveryEndHours = 5)
    {
        var q = new QuoteRecord(1, new TimeWindowView(pickupStart, pickupStart.AddHours(pickupHours)), new TimeWindowView(pickupStart.AddHours(deliveryStartHours), pickupStart.AddHours(deliveryEndHours)), issuedAt)
        {
            Price = price,
            Outcome = "Issued, awaiting decision",
        };
        r.Quotes.Add(q);
        SeedMove(r, Status.Quoted, issuedAt, $"Quote v1 issued at {Display.Money(price)}", by);
    }

    private void SeedMove(RequestRecord r, Status next, DateTime at, string note, string actor)
    {
        if (!_lifecycle.CanTransition(r.Status, next))
        {
            throw new InvalidOperationException(_lifecycle.ExplainRefusal(r.Status, next));
        }

        if (next == Status.Accepted && r.CurrentQuote is not null)
        {
            r.CurrentQuote.Outcome = "Accepted by customer";
        }

        r.Events.Add(new EventView(at, r.Status, next, actor, note));
        r.Status = next;
    }

    // ---------------------------------------------------------------- in-memory records

    private sealed record CustomerRecord(string Id, string Name, string Phone, string? Email);

    private sealed record UserRecord(string Username, string PasswordHash, string DisplayName, IReadOnlySet<UserRole> Roles, string? CustomerId, string? StaffId);

    private sealed record StaffRecord(string Id, string Name, bool IsDelivery, TimeOnly From, TimeOnly To)
    {
        public bool Active { get; init; } = true;
    }

    private sealed class ParcelRecord
    {
        public ParcelRecord(string id, SizeBand size, ParcelHandling flags, string? description)
        {
            Id = id;
            Size = size;
            Flags = flags;
            Description = description;
        }

        public string Id { get; }

        public SizeBand Size { get; }

        public ParcelHandling Flags { get; }

        public string? Description { get; }

        public bool Collected { get; set; }
    }

    private sealed class QuoteRecord
    {
        public QuoteRecord(int version, TimeWindowView pickupWindow, TimeWindowView deliveryWindow, DateTime issuedAt)
        {
            Version = version;
            PickupWindow = pickupWindow;
            DeliveryWindow = deliveryWindow;
            IssuedAt = issuedAt;
        }

        public int Version { get; }

        public decimal? Price { get; set; }

        public string? AdjustReason { get; init; }

        public TimeWindowView PickupWindow { get; }

        public TimeWindowView DeliveryWindow { get; }

        public DateTime IssuedAt { get; set; }

        public DateTime ValidUntil => IssuedAt + QuoteValidity;

        public bool AwaitingOwner { get; set; }

        public string Outcome { get; set; } = string.Empty;
    }

    private sealed class RequestRecord
    {
        public RequestRecord(string reference, string customerId, DateTime loggedAt, string loggedBy, AddressInput pickup, ContactInput pickupContact, DateTime readyTime, AddressInput destination, ContactInput recipient, string? notes)
        {
            Reference = reference;
            CustomerId = customerId;
            LoggedAt = loggedAt;
            LoggedBy = loggedBy;
            Pickup = pickup;
            PickupContact = pickupContact;
            ReadyTime = readyTime;
            Destination = destination;
            Recipient = recipient;
            Notes = notes;
        }

        public string Reference { get; }

        public string CustomerId { get; }

        public DateTime LoggedAt { get; }

        public string LoggedBy { get; }

        public AddressInput Pickup { get; }

        public ContactInput PickupContact { get; }

        public DateTime ReadyTime { get; }

        public AddressInput Destination { get; }

        public ContactInput Recipient { get; }

        public string? Notes { get; }

        public Status Status { get; set; } = Status.Requested;

        public List<ParcelRecord> Parcels { get; } = new();

        public List<QuoteRecord> Quotes { get; } = new();

        public QuoteRecord? CurrentQuote => Quotes.Count == 0 ? null : Quotes[^1];

        public string? StaffId { get; set; }

        public List<PaymentView> Payments { get; } = new();

        public List<EventView> Events { get; } = new();
    }
}
