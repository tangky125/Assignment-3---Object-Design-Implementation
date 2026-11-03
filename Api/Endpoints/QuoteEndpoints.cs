using Dms.Api.Contracts;

namespace Dms.Api.Endpoints;

/// <summary>Area 2: the office quote desk and owner approval.</summary>
public static class QuoteEndpoints
{
    private const decimal MaxPrice = 10_000m;

    /// <summary>Maps the endpoints.</summary>
    public static void MapQuoteEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/quotes/queue", (HttpContext context, ApiAccess access, DmsServices services) =>
            access.RequireOffice(context, out _) ?? Results.Ok(services.Quotes.AwaitingQuote()));

        app.MapGet("/api/quotes/{reference}/proposal", (string reference, HttpContext context, ApiAccess access, DmsServices services) =>
            access.RequireOffice(context, out _) ?? Results.Ok(services.Quotes.Propose(reference)));

        app.MapPost("/api/quotes/{reference}", (string reference, QuoteInput input, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.RequireOffice(context, out UserSession session) is { } denied)
            {
                return denied;
            }

            if (input.PickupWindow is null || input.DeliveryWindow is null)
            {
                return ApiAccess.BadRequest("Both the pickup and the delivery window are needed.");
            }

            if (input.AdjustedPrice is { } price && (price < 1m || price > MaxPrice))
            {
                return ApiAccess.BadRequest($"The price must be between {Display.Money(1m)} and {Display.Money(MaxPrice)}.");
            }

            return Results.Ok(services.Quotes.IssueQuote(reference, input, session));
        });

        app.MapGet("/api/quotes/owner-queue", (HttpContext context, ApiAccess access, DmsServices services) =>
            access.Require(context, out _, UserRole.Owner) ?? Results.Ok(services.Quotes.AwaitingOwnerApproval()));

        app.MapPost("/api/quotes/{reference}/release", (string reference, PriceBody body, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.Require(context, out UserSession session, UserRole.Owner) is { } denied)
            {
                return denied;
            }

            return body.Price < 1m || body.Price > MaxPrice
                ? ApiAccess.BadRequest($"The price must be between {Display.Money(1m)} and {Display.Money(MaxPrice)}.")
                : Results.Ok(services.Quotes.ReleaseOwnerPrice(reference, decimal.Round(body.Price, 2), session));
        });
    }
}
