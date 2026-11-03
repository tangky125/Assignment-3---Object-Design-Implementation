using Dms.Api.Contracts;
using Dms.Domain;

namespace Dms.Api.Endpoints;

/// <summary>Sign-in, sign-out and shared reference data.</summary>
public static class AuthEndpoints
{
    /// <summary>Maps the endpoints.</summary>
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app, IReadOnlyList<string> demoAccounts)
    {
        app.MapPost("/api/auth/sign-in", (SignInBody body, DmsServices services, SessionStore sessions) =>
        {
            if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrEmpty(body.Password))
            {
                return ApiAccess.BadRequest("Enter both your username and your password.");
            }

            UserSession? session = services.Auth.SignIn(body.Username, body.Password);
            return session is null
                ? Results.Json(new ApiError("Username or password is incorrect. Check both and try again."), statusCode: StatusCodes.Status401Unauthorized)
                : Results.Ok(new SignInResponse(sessions.Start(session), SessionView.From(session)));
        });

        app.MapPost("/api/auth/sign-out", (HttpContext context, SessionStore sessions) =>
        {
            sessions.End(context);
            return Results.NoContent();
        });

        app.MapGet("/api/auth/me", (HttpContext context, ApiAccess access) =>
            access.Caller(context) is { } session
                ? Results.Ok(SessionView.From(session))
                : Results.Json(new ApiError("Not signed in."), statusCode: StatusCodes.Status401Unauthorized));

        // The legal status moves, so the UI only offers actions the StatusLifecycle allows.
        app.MapGet("/api/lifecycle", (DmsServices services) =>
            Enum.GetValues<Status>().ToDictionary(
                from => from.ToString(),
                from => Enum.GetValues<Status>().Where(to => services.Lifecycle.CanTransition(from, to)).ToList()));

        app.MapGet("/api/help/accounts", () => demoAccounts);
    }
}
