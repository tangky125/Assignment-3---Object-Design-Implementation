using Dms.Api.Contracts;
using Dms.Api.Validation;

namespace Dms.Api.Endpoints;

/// <summary>Area 3: allocate delivery staff.</summary>
public static class AllocationEndpoints
{
    /// <summary>Maps the endpoints.</summary>
    public static void MapAllocationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/allocation/waiting", (HttpContext context, ApiAccess access, DmsServices services) =>
            access.RequireOffice(context, out _) ?? Results.Ok(services.Allocation.AwaitingAllocation()));

        app.MapGet("/api/allocation/scheduled", (HttpContext context, ApiAccess access, DmsServices services) =>
            access.RequireOffice(context, out _) ?? Results.Ok(services.Allocation.ScheduledDeliveries()));

        app.MapGet("/api/allocation/{reference}/staff", (string reference, HttpContext context, ApiAccess access, DmsServices services) =>
            access.RequireOffice(context, out _) ?? Results.Ok(services.Allocation.StaffOptions(reference)));

        app.MapPost("/api/allocation/{reference}", (string reference, StaffBody body, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.RequireOffice(context, out UserSession session) is { } denied)
            {
                return denied;
            }

            return string.IsNullOrWhiteSpace(body.StaffId)
                ? ApiAccess.BadRequest("Choose a staff member.")
                : Results.Ok(services.Allocation.Allocate(reference, body.StaffId, session));
        });

        app.MapPost("/api/allocation/{reference}/replace", (string reference, StaffBody body, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.RequireOffice(context, out UserSession session) is { } denied)
            {
                return denied;
            }

            if (string.IsNullOrWhiteSpace(body.StaffId))
            {
                return ApiAccess.BadRequest("Choose a staff member.");
            }

            string reason = body.Reason?.Trim() ?? string.Empty;
            return InputRules.Reason(reason) is { } error
                ? ApiAccess.BadRequest(error)
                : Results.Ok(services.Allocation.Replace(reference, body.StaffId, reason, session));
        });
    }
}
