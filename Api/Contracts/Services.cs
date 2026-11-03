using Dms.Domain;

namespace Dms.Api.Contracts;

// The operations the web UI needs from the business layer, one interface per business area.
// The API endpoints (Api/Endpoints) call these; each area owner implements their interface over
// their own domain classes, and the bootstrap (DmsInitialiser) builds one DmsServices.
// See Docs/UI-Integration-Guide.md.

/// <summary>Sign-in (A2 AccessPolicy needs to know who the caller is).</summary>
public interface IAuthService
{
    /// <summary>Returns the session for valid credentials, or null when they are wrong.</summary>
    UserSession? SignIn(string username, string password);
}

/// <summary>Area 1: log a delivery request (A1 Task 1).</summary>
public interface IRequestService
{
    /// <summary>Finds held customers by phone and/or close name match (subtask 1).</summary>
    IReadOnlyList<CustomerSummary> FindCustomers(string? phone, string? name);

    /// <summary>Returns one customer, or null.</summary>
    CustomerSummary? GetCustomer(string customerId);

    /// <summary>Registers a new customer once no offered match was confirmed.</summary>
    OperationResult<CustomerSummary> RegisterCustomer(string name, string phone, string? email);

    /// <summary>Recent pickup/destination pairs for a repeat customer (variant 1b).</summary>
    IReadOnlyList<AddressPair> RecentAddressPairs(string customerId);

    /// <summary>Checks that a pickup address lies inside the service area (subtask 2, A2 DA11).</summary>
    OperationResult CheckPickupAddress(AddressInput pickup);

    /// <summary>Stores the request with status Requested and returns its reference (subtask 6).</summary>
    OperationResult<string> SubmitRequest(RequestDraft draft, UserSession loggedBy);

    /// <summary>Records the customer withdrawing a request, when the status lifecycle allows it.</summary>
    OperationResult Cancel(string reference, string reason, UserSession by);
}

/// <summary>Area 2: prepare a quote and record the customer's decision (A1 Tasks 2 and 3).</summary>
public interface IQuoteService
{
    /// <summary>Requests waiting for a quote, oldest first (Task 2 subtask 1).</summary>
    IReadOnlyList<RequestSummary> AwaitingQuote();

    /// <summary>Proposes a price and achievable windows for a request.</summary>
    OperationResult<QuoteProposal> Propose(string reference);

    /// <summary>Issues a quote; out-of-area quotes are held for owner approval (variant 2a).</summary>
    OperationResult IssueQuote(string reference, QuoteInput input, UserSession by);

    /// <summary>Every request a customer has placed, newest first.</summary>
    IReadOnlyList<RequestSummary> RequestsForCustomer(string customerId);

    /// <summary>Every quote version for a request, oldest first (variant 3a).</summary>
    IReadOnlyList<QuoteView> QuoteHistory(string reference);

    /// <summary>Accepts the current quote; refused once it has expired (Task 3 variant 1a).</summary>
    OperationResult Accept(string reference, UserSession by);

    /// <summary>Rejects the current quote and closes the request.</summary>
    OperationResult Reject(string reference, string reason, UserSession by);

    /// <summary>Asks for a revised quote; the request returns to the quote queue (variant 2a/2d).</summary>
    OperationResult RequestRevision(string reference, string reason, UserSession by);

    /// <summary>Quotes held for owner approval.</summary>
    IReadOnlyList<RequestSummary> AwaitingOwnerApproval();

    /// <summary>Records the owner's price and releases the quote to the customer.</summary>
    OperationResult ReleaseOwnerPrice(string reference, decimal price, UserSession owner);
}

/// <summary>Area 3: allocate delivery staff (A1 Task 4).</summary>
public interface IAllocationService
{
    /// <summary>Deliveries waiting for a staff member: Accepted, or Delivery Failed needing re-allocation.</summary>
    IReadOnlyList<RequestSummary> AwaitingAllocation();

    /// <summary>Deliveries already Scheduled (their staff member can be replaced: variant 4a).</summary>
    IReadOnlyList<RequestSummary> ScheduledDeliveries();

    /// <summary>Every active delivery staff member, with whether they can take this job and why.</summary>
    IReadOnlyList<StaffOption> StaffOptions(string reference);

    /// <summary>Allocates the job after a final availability check.</summary>
    OperationResult Allocate(string reference, string staffId, UserSession by);

    /// <summary>Replaces the staff member on a Scheduled delivery.</summary>
    OperationResult Replace(string reference, string newStaffId, string reason, UserSession by);
}

/// <summary>Area 4: execute a delivery and take payment (A1 Task 5).</summary>
public interface IDeliveryService
{
    /// <summary>Jobs allocated to a delivery staff member that are not yet closed.</summary>
    IReadOnlyList<RequestSummary> JobsFor(string staffId);

    /// <summary>The amount the customer must pay at pickup (the accepted quote).</summary>
    decimal AmountDue(string reference);

    /// <summary>Ticks or unticks one parcel at pickup (variant 4a).</summary>
    OperationResult SetParcelCollected(string reference, string parcelId, bool collected, UserSession by);

    /// <summary>Records cash taken at pickup; returns the change due.</summary>
    PaymentOutcomeView TakeCash(string reference, decimal amountReceived, UserSession by);

    /// <summary>Records the outcome of a card payment. No card details enter the system (A1 A6).</summary>
    PaymentOutcomeView TakeCard(string reference, bool approved, UserSession by);

    /// <summary>Confirms pickup: every parcel collected and payment taken.</summary>
    OperationResult ConfirmCollection(string reference, UserSession by);

    /// <summary>Records the hand-over to the recipient.</summary>
    OperationResult RecordDelivered(string reference, string receivedBy, UserSession by);

    /// <summary>Records a failed pickup or delivery with its reason.</summary>
    OperationResult RecordFailure(string reference, string reason, UserSession by);
}

/// <summary>Track a delivery (A1 Task 6).</summary>
public interface ITrackingService
{
    /// <summary>Full detail of one request, or null when the reference is unknown.</summary>
    RequestDetail? Find(string reference);

    /// <summary>Searches by any combination of reference, customer name, phone and date logged.</summary>
    IReadOnlyList<RequestSummary> Search(string? reference, string? name, string? phone, DateOnly? loggedOn);

    /// <summary>All requests that are not closed, with how long each has been open (variant 3a).</summary>
    IReadOnlyList<RequestSummary> OpenRequests();

    /// <summary>How much of a request the viewer may see. A null viewer holds the reference only.</summary>
    ViewerLevel LevelFor(UserSession? viewer, string reference);
}

/// <summary>Everything the UI needs, wired together once at start-up by the bootstrap.</summary>
public sealed record DmsServices(
    IAuthService Auth,
    IRequestService Requests,
    IQuoteService Quotes,
    IAllocationService Allocation,
    IDeliveryService Deliveries,
    ITrackingService Tracking,
    StatusLifecycle Lifecycle);
