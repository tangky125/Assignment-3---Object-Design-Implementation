using Dms.Api.Contracts;
using Dms.Domain;

namespace Dms.Api;

// Shapes of the JSON bodies the web UI sends and receives (camelCase on the wire).

/// <summary>Sign-in details.</summary>
public sealed record SignInBody(string? Username, string? Password);

/// <summary>The signed-in user as the web UI sees it.</summary>
public sealed record SessionView(string Username, string DisplayName, IReadOnlyList<UserRole> Roles, string? CustomerId, string? StaffId)
{
    /// <summary>Builds the view from a session.</summary>
    public static SessionView From(UserSession s) =>
        new(s.Username, s.DisplayName, s.Roles.OrderBy(r => r).ToList(), s.CustomerId, s.StaffId);
}

/// <summary>Returned after a successful sign-in.</summary>
public sealed record SignInResponse(string Token, SessionView User);

/// <summary>A reason typed by the user (reject, revise, cancel, failure).</summary>
public sealed record ReasonBody(string? Reason);

/// <summary>A new customer.</summary>
public sealed record NewCustomerBody(string? Name, string? Phone, string? Email);

/// <summary>An owner's price for an out-of-area quote.</summary>
public sealed record PriceBody(decimal Price);

/// <summary>A staff member to allocate, with a reason when replacing.</summary>
public sealed record StaffBody(string? StaffId, string? Reason);

/// <summary>Ticks or unticks a parcel.</summary>
public sealed record CollectedBody(bool Collected);

/// <summary>Cash handed over by the customer.</summary>
public sealed record CashBody(decimal AmountReceived);

/// <summary>What the card terminal showed.</summary>
public sealed record CardBody(bool Approved);

/// <summary>Who received the parcels.</summary>
public sealed record DeliveredBody(string? ReceivedBy);

/// <summary>What a reference holder may see: status and expected window only.</summary>
public sealed record RecipientView(string Reference, Status Status, TimeWindowView? ExpectedDelivery, string DestinationSuburb, bool Late);

/// <summary>A tracking answer cut down to what the viewer may see.</summary>
public sealed record TrackingResponse(ViewerLevel Level, RequestDetail? Detail, RecipientView? Recipient);

/// <summary>A delivery job with the amount to collect.</summary>
public sealed record JobResponse(RequestDetail Detail, decimal AmountDue);
