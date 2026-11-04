using System.Text.Json.Serialization;
using Dms.Domain;

namespace Dms.Api.Contracts;

// Read-only data passed between the web UI (through the API) and the business layer.
// The UI never holds domain objects directly, so domain classes can change without breaking screens.

/// <summary>Declared parcel size band (A1 Task 1 subtask 4: sizes, not exact weights).</summary>
public enum SizeBand
{
    Small,
    Medium,
    Large,
}

/// <summary>
/// Handling requirements that appear on the delivery staff member's job view.
/// Sent to the web UI as a number so several can be combined (1 + 2 = fragile and signature).
/// </summary>
[Flags]
public enum ParcelHandling
{
    None = 0,
    Fragile = 1,
    SignatureRequired = 2,
    KeepUpright = 4,
}

/// <summary>How much of a request a viewer may see (A2 AccessPolicy detail levels).</summary>
public enum ViewerLevel
{
    /// <summary>May not see the request at all.</summary>
    None,

    /// <summary>Holds the reference only: status and expected window, no price or customer detail.</summary>
    Recipient,

    /// <summary>The customer who placed it: everything except internal notes and staff detail.</summary>
    Customer,

    /// <summary>Office staff, delivery staff and owners: full detail.</summary>
    Staff,
}

/// <summary>A street address in Victoria.</summary>
public sealed record AddressInput(string Street, string Suburb, string Postcode)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Street}, {Suburb} VIC {Postcode}";
}

/// <summary>A person's name and phone number.</summary>
public sealed record ContactInput(string Name, string Phone)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Name} ({Phone})";
}

/// <summary>One parcel as entered by the user.</summary>
public sealed record ParcelInput(
    SizeBand Size,
    [property: JsonConverter(typeof(JsonNumberEnumConverter<ParcelHandling>))] ParcelHandling Flags,
    string? Description);

/// <summary>A complete delivery request ready to submit (A1 Task 1).</summary>
public sealed record RequestDraft(
    string CustomerId,
    AddressInput Pickup,
    ContactInput PickupContact,
    DateTime ReadyTime,
    AddressInput Destination,
    ContactInput Recipient,
    IReadOnlyList<ParcelInput> Parcels,
    string? Notes);

/// <summary>A customer as listed in search results.</summary>
public sealed record CustomerSummary(string CustomerId, string Name, string Phone, string? Email);

/// <summary>A previously used pickup and destination pair (A1 Task 1 variant 1b).</summary>
public sealed record AddressPair(AddressInput Pickup, AddressInput Destination, ContactInput Recipient);

/// <summary>A start and end time.</summary>
public sealed record TimeWindowView(DateTime Start, DateTime End);

/// <summary>One line in a list of requests.</summary>
/// <param name="Tag">Why the row is in a work queue (see <see cref="QueueTags"/>); null elsewhere.</param>
public sealed record RequestSummary(
    string Reference,
    string CustomerName,
    Status Status,
    DateTime LoggedAt,
    string PickupSuburb,
    string DestinationSuburb,
    int ParcelCount,
    TimeWindowView? PickupWindow,
    string? Note,
    string? Tag = null);

/// <summary>The tags shown on rows of the quote queue and of a customer's request list.</summary>
public static class QueueTags
{
    /// <summary>Never quoted.</summary>
    public const string New = "New";

    /// <summary>The customer asked for a revised quote.</summary>
    public const string Revision = "Revision";

    /// <summary>The last quote expired without a decision.</summary>
    public const string Expired = "Expired";

    /// <summary>On a customer's list: a quote is waiting for their decision.</summary>
    public const string QuoteReady = "QuoteReady";

    /// <summary>On a customer's list: a quote waiting for their decision expires within a few hours.</summary>
    public const string ExpiresSoon = "ExpiresSoon";
}

/// <summary>One parcel on a request.</summary>
public sealed record ParcelView(
    string ParcelId,
    SizeBand Size,
    [property: JsonConverter(typeof(JsonNumberEnumConverter<ParcelHandling>))] ParcelHandling Flags,
    string? Description,
    bool Collected);

/// <summary>A quote version as shown to users.</summary>
/// <param name="Decision">Pending, Accepted, Rejected, RevisionRequested or Expired.</param>
/// <param name="DecisionReason">The customer's reason for a rejection or a revision request.</param>
/// <param name="ConfirmedVia">How the customer gave a decision that office staff recorded.</param>
public sealed record QuoteView(
    string Reference,
    int Version,
    decimal? Price,
    string? AdjustReason,
    TimeWindowView PickupWindow,
    TimeWindowView DeliveryWindow,
    DateTime IssuedAt,
    DateTime ValidUntil,
    bool IsExpired,
    bool AwaitingOwnerApproval,
    string Outcome,
    string? Decision = null,
    string? DecisionReason = null,
    string? ConfirmedVia = null);

/// <summary>A payment attempt (no card data is ever held: A1 A6).</summary>
public sealed record PaymentView(DateTime At, string Method, decimal Amount, bool Successful, string Outcome);

/// <summary>A timestamped entry in a request's history.</summary>
public sealed record EventView(DateTime At, Status? From, Status To, string Actor, string Note);

/// <summary>Everything about one request, for detail and tracking screens.</summary>
public sealed record RequestDetail(
    string Reference,
    string CustomerId,
    string CustomerName,
    string CustomerPhone,
    Status Status,
    DateTime LoggedAt,
    string LoggedBy,
    AddressInput Pickup,
    ContactInput PickupContact,
    DateTime ReadyTime,
    AddressInput Destination,
    ContactInput Recipient,
    IReadOnlyList<ParcelView> Parcels,
    string? Notes,
    QuoteView? CurrentQuote,
    string? AllocatedStaffId,
    string? AllocatedStaffName,
    IReadOnlyList<PaymentView> Payments,
    IReadOnlyList<EventView> History);

/// <summary>A proposed quote before office staff issue it (A1 Task 2 subtasks 2-4).</summary>
public sealed record QuoteProposal(
    string Reference,
    decimal? ProposedPrice,
    bool NeedsOwnerApproval,
    TimeWindowView PickupWindow,
    TimeWindowView DeliveryWindow,
    IReadOnlyList<string> PriceBreakdown,
    IReadOnlyList<string> Warnings);

/// <summary>What office staff decided to issue.</summary>
public sealed record QuoteInput(
    decimal? AdjustedPrice,
    string? AdjustReason,
    TimeWindowView PickupWindow,
    TimeWindowView DeliveryWindow);

/// <summary>A delivery staff member and whether they can take a given job (A1 Task 4 subtask 2).</summary>
public sealed record StaffOption(string StaffId, string Name, bool Eligible, string Reason);

/// <summary>Result of a payment attempt at pickup.</summary>
public sealed record PaymentOutcomeView(bool Successful, string Message, decimal? ChangeDue);
