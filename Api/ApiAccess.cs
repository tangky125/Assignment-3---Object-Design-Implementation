using Dms.Api.Contracts;

namespace Dms.Api;

/// <summary>Plain-language error body returned with 400, 401, 403 and 404 responses.</summary>
/// <param name="Message">What went wrong and what to do.</param>
public sealed record ApiError(string Message);

/// <summary>
/// Who may call what. Every endpoint asks here first, so the rules are in one place
/// (the API-side counterpart of the A2 AccessPolicy).
/// </summary>
public sealed class ApiAccess
{
    private readonly SessionStore _sessions;
    private readonly DmsServices _services;

    /// <summary>Creates the access checker.</summary>
    public ApiAccess(SessionStore sessions, DmsServices services)
    {
        _sessions = sessions;
        _services = services;
    }

    /// <summary>The caller's session, or null for an anonymous caller.</summary>
    public UserSession? Caller(HttpContext context) => _sessions.Find(context);

    /// <summary>Requires a signed-in user holding any of <paramref name="roles"/>; returns an error result otherwise.</summary>
    public IResult? Require(HttpContext context, out UserSession session, params UserRole[] roles)
    {
        UserSession? found = _sessions.Find(context);
        session = found!;
        if (found is null)
        {
            return Results.Json(new ApiError("Please sign in first."), statusCode: StatusCodes.Status401Unauthorized);
        }

        return roles.Length == 0 || roles.Any(found.Has)
            ? null
            : Results.Json(new ApiError("Your account is not allowed to do this."), statusCode: StatusCodes.Status403Forbidden);
    }

    /// <summary>Requires office staff or an owner.</summary>
    public IResult? RequireOffice(HttpContext context, out UserSession session) =>
        Require(context, out session, UserRole.OfficeStaff, UserRole.Owner);

    /// <summary>Requires office staff/owner, or the customer the record belongs to.</summary>
    public IResult? RequireOfficeOrCustomer(HttpContext context, string customerId, out UserSession session)
    {
        IResult? denied = Require(context, out session, UserRole.OfficeStaff, UserRole.Owner, UserRole.Customer);
        if (denied is not null)
        {
            return denied;
        }

        bool office = session.Has(UserRole.OfficeStaff) || session.Has(UserRole.Owner);
        return office || session.CustomerId == customerId ? null : NotFound("No such customer.");
    }

    /// <summary>Requires office staff/owner, or the customer who placed the request.</summary>
    public IResult? RequireOfficeOrOwnRequest(HttpContext context, string reference, out UserSession session)
    {
        RequestDetail? detail = _services.Tracking.Find(reference);
        if (detail is null)
        {
            session = null!;
            return NotFound($"No request has the reference {reference}.");
        }

        return RequireOfficeOrCustomer(context, detail.CustomerId, out session);
    }

    /// <summary>Requires the delivery staff member the job is allocated to.</summary>
    public IResult? RequireOwnJob(HttpContext context, string reference, out UserSession session)
    {
        IResult? denied = Require(context, out session, UserRole.DeliveryStaff);
        if (denied is not null)
        {
            return denied;
        }

        RequestDetail? job = _services.Tracking.Find(reference);
        return job is not null && job.AllocatedStaffId == session.StaffId
            ? null
            : NotFound($"{reference} is not one of your jobs.");
    }

    /// <summary>A 400 response with a message.</summary>
    public static IResult BadRequest(string message) => Results.BadRequest(new ApiError(message));

    /// <summary>A 404 response with a message.</summary>
    public static IResult NotFound(string message) => Results.NotFound(new ApiError(message));
}
