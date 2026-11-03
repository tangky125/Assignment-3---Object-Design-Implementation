using Dms.Api.Contracts;
using Dms.Api.Validation;

namespace Dms.Api.Endpoints;

/// <summary>Area 4: delivery staff carry out their own jobs.</summary>
public static class JobEndpoints
{
    /// <summary>Maps the endpoints.</summary>
    public static void MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/jobs", (HttpContext context, ApiAccess access, DmsServices services) =>
            access.Require(context, out UserSession session, UserRole.DeliveryStaff)
            ?? Results.Ok(services.Deliveries.JobsFor(session.StaffId ?? string.Empty)));

        app.MapGet("/api/jobs/{reference}", (string reference, HttpContext context, ApiAccess access, DmsServices services) =>
            access.RequireOwnJob(context, reference, out _)
            ?? Results.Ok(new JobResponse(services.Tracking.Find(reference)!, services.Deliveries.AmountDue(reference))));

        app.MapPost("/api/jobs/{reference}/parcels/{parcelId}", (string reference, string parcelId, CollectedBody body, HttpContext context, ApiAccess access, DmsServices services) =>
            access.RequireOwnJob(context, reference, out UserSession session)
            ?? Results.Ok(services.Deliveries.SetParcelCollected(reference, parcelId, body.Collected, session)));

        app.MapPost("/api/jobs/{reference}/payments/cash", (string reference, CashBody body, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.RequireOwnJob(context, reference, out UserSession session) is { } denied)
            {
                return denied;
            }

            return body.AmountReceived <= 0m || body.AmountReceived > 10_000m
                ? ApiAccess.BadRequest("Enter the cash received, in dollars.")
                : Results.Ok(services.Deliveries.TakeCash(reference, decimal.Round(body.AmountReceived, 2), session));
        });

        app.MapPost("/api/jobs/{reference}/payments/card", (string reference, CardBody body, HttpContext context, ApiAccess access, DmsServices services) =>
            access.RequireOwnJob(context, reference, out UserSession session)
            ?? Results.Ok(services.Deliveries.TakeCard(reference, body.Approved, session)));

        app.MapPost("/api/jobs/{reference}/confirm-collection", (string reference, HttpContext context, ApiAccess access, DmsServices services) =>
            access.RequireOwnJob(context, reference, out UserSession session)
            ?? Results.Ok(services.Deliveries.ConfirmCollection(reference, session)));

        app.MapPost("/api/jobs/{reference}/delivered", (string reference, DeliveredBody body, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.RequireOwnJob(context, reference, out UserSession session) is { } denied)
            {
                return denied;
            }

            string name = body.ReceivedBy?.Trim() ?? string.Empty;
            return InputRules.Name(name) is { } error
                ? ApiAccess.BadRequest(error)
                : Results.Ok(services.Deliveries.RecordDelivered(reference, name, session));
        });

        app.MapPost("/api/jobs/{reference}/failure", (string reference, ReasonBody body, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.RequireOwnJob(context, reference, out UserSession session) is { } denied)
            {
                return denied;
            }

            string reason = body.Reason?.Trim() ?? string.Empty;
            return InputRules.Reason(reason) is { } error
                ? ApiAccess.BadRequest(error)
                : Results.Ok(services.Deliveries.RecordFailure(reference, reason, session));
        });
    }
}
