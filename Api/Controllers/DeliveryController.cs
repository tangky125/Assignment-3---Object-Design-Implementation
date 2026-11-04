using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dms.Api.Contracts;
using Dms.Api.Validation;
using Dms.Domain;

namespace Dms.Api.Controllers;

/// <summary>
/// The domain objects and the money behind one request reference. The Domain layer keeps no
/// storage, so the bootstrap supplies this through <see cref="IDeliveryRegistry"/>.
/// </summary>
/// <param name="Delivery">The locked Area-4 aggregate that owns status, payments and history.</param>
/// <param name="Request">The request whose parcels are ticked off at pickup.</param>
/// <param name="AmountDue">The accepted quote price, owned by Area 2.</param>
public sealed record DeliveryContext(Delivery Delivery, DeliveryRequest Request, decimal AmountDue);

/// <summary>
/// Gives the adapter the domain state for a reference. Implemented once by the bootstrap
/// (DmsInitialiser) over whatever storage Area 1 and Area 2 use; it never changes the Domain.
/// </summary>
public interface IDeliveryRegistry
{
    /// <summary>The domain objects for a reference, or null when it is unknown.</summary>
    DeliveryContext? Find(string reference);

    /// <summary>Open jobs allocated to one delivery staff member.</summary>
    IReadOnlyList<RequestSummary> JobsFor(string staffId);

    /// <summary>The read model for one reference, or null when it is unknown.</summary>
    RequestDetail? Detail(string reference);
}

/// <summary>
/// Area 4 adapter: translates the web UI's delivery actions into commands on the locked
/// <see cref="Delivery"/> aggregate. Status changes only happen through the domain, and every
/// command carries a replay key (DA9). Refusals are returned as data so the Vue UI can show them.
/// </summary>
public sealed class DeliveryServiceAdapter : IDeliveryService
{
    private readonly IDeliveryRegistry _registry;
    private readonly Func<DateTimeOffset> _clock;
    private readonly IHttpContextAccessor? _http;

    /// <summary>Creates the adapter.</summary>
    /// <param name="registry">Supplies the domain state for a reference.</param>
    /// <param name="clock">Supplies the time sent into the domain; the domain never reads a clock.</param>
    /// <param name="http">Supplies the Idempotency-Key header when the caller sends one.</param>
    public DeliveryServiceAdapter(
        IDeliveryRegistry registry,
        Func<DateTimeOffset>? clock = null,
        IHttpContextAccessor? http = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _clock = clock ?? (() => DateTimeOffset.Now);
        _http = http;
    }

    private DateTimeOffset Now => _clock();

    /// <inheritdoc/>
    public IReadOnlyList<RequestSummary> JobsFor(string staffId)
    {
        ArgumentNullException.ThrowIfNull(staffId);
        return _registry.JobsFor(staffId);
    }

    /// <inheritdoc/>
    public decimal AmountDue(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return _registry.Find(reference)?.AmountDue ?? 0m;
    }

    /// <inheritdoc/>
    public OperationResult SetParcelCollected(string reference, string parcelId, bool collected, UserSession by)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(parcelId);
        ArgumentNullException.ThrowIfNull(by);

        if (Lookup(reference, out DeliveryContext? context) is { } notFound)
        {
            return notFound;
        }

        Parcel? parcel = context.Request.Parcels.FirstOrDefault(p => string.Equals(p.Label, parcelId, StringComparison.Ordinal));
        if (parcel is null)
        {
            return OperationResult.Fail($"Request {reference} has no parcel {parcelId}.");
        }

        if (parcel.IsCollected)
        {
            return OperationResult.Ok($"Parcel {parcel.Label} was already ticked as collected.");
        }

        if (!collected)
        {
            // The locked Domain only ever marks a parcel collected; it has no un-tick command.
            return OperationResult.Fail("Parcels cannot be un-ticked once collected; tick the rest or record a failure instead.");
        }

        try
        {
            context.Delivery.RecordParcelCollected(EventId(reference, $"collect-{parcel.Label}", by), Actor(by), Now, parcel);
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail(ex.Message);
        }

        int ticked = context.Request.Parcels.Count(p => p.IsCollected);
        return OperationResult.Ok($"Parcel {parcel.Label} ticked as collected ({ticked} of {context.Request.Parcels.Count}).");
    }

    /// <inheritdoc/>
    public PaymentOutcomeView TakeCash(string reference, decimal amountReceived, UserSession by)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(by);

        if (Lookup(reference, out DeliveryContext? context) is { } notFound)
        {
            return new PaymentOutcomeView(false, notFound.Message, null);
        }

        decimal due = context.AmountDue;
        if (amountReceived < due)
        {
            return new PaymentOutcomeView(
                false,
                $"Cash received ({Display.Money(amountReceived)}) is less than the amount due ({Display.Money(due)}). Collect the full amount or use a card.",
                null);
        }

        Payment? payment = TryPayment(reference, context, "cash", by, new CashPayment(), due, out string refusal);
        return payment is null
            ? new PaymentOutcomeView(false, refusal, null)
            : new PaymentOutcomeView(
                payment.WasSuccessful,
                $"Cash payment of {Display.Money(payment.Amount)} recorded.",
                amountReceived - payment.Amount);
    }

    /// <inheritdoc/>
    public PaymentOutcomeView TakeCard(string reference, bool approved, UserSession by)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(by);

        if (Lookup(reference, out DeliveryContext? context) is { } notFound)
        {
            return new PaymentOutcomeView(false, notFound.Message, null);
        }

        // The domain's gateway declines tokens starting "0000", so the terminal's answer maps to a token.
        string token = approved ? "4242424242424242" : "0000000000000000";
        Payment? payment = TryPayment(reference, context, "card", by, new CardPayment(new PaymentGateway(), token), context.AmountDue, out string refusal);
        if (payment is null)
        {
            return new PaymentOutcomeView(false, refusal, null);
        }

        return new PaymentOutcomeView(
            payment.WasSuccessful,
            payment.WasSuccessful
                ? $"Card payment of {Display.Money(payment.Amount)} approved."
                : $"Card payment declined. The attempt is kept on record. {payment.OutcomeNote}",
            null);
    }

    /// <inheritdoc/>
    public OperationResult ConfirmCollection(string reference, UserSession by)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(by);

        if (Lookup(reference, out DeliveryContext? context) is { } notFound)
        {
            return notFound;
        }

        Parcel? missing = context.Request.Parcels.FirstOrDefault(p => !p.IsCollected);
        if (missing is not null)
        {
            return OperationResult.Fail($"Parcel {missing.Label} is not ticked as collected. Tick every parcel before confirming pickup.");
        }

        return context.Delivery.Payments.Any(p => p.WasSuccessful)
            ? OperationResult.Ok($"Pickup confirmed. {reference} is now {context.Delivery.Status}. The customer has been notified.")
            : OperationResult.Fail("Payment has not been taken. Payment is taken at pickup, before the parcels leave.");
    }

    /// <inheritdoc/>
    public OperationResult RecordDelivered(string reference, string receivedBy, UserSession by)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(by);

        if (Lookup(reference, out DeliveryContext? context) is { } notFound)
        {
            return notFound;
        }

        try
        {
            context.Delivery.RecordSuccessfulHandover(EventId(reference, "delivered", by), Actor(by), Now, receivedBy ?? string.Empty);
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail(ex.Message);
        }

        return OperationResult.Ok($"Delivery completed. {reference} is now Delivered. The customer has been notified.");
    }

    /// <inheritdoc/>
    public OperationResult RecordFailure(string reference, string reason, UserSession by)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(by);

        if (Lookup(reference, out DeliveryContext? context) is { } notFound)
        {
            return notFound;
        }

        try
        {
            context.Delivery.RecordFailedAttempt(EventId(reference, "failure", by), Actor(by), Now, reason ?? string.Empty);
        }
        catch (ArgumentException ex)
        {
            return OperationResult.Fail(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail(ex.Message);
        }

        return OperationResult.Ok($"Failure recorded: {reason}. {reference} is back in the allocation queue for office staff to reschedule.");
    }

    /// <summary>Maps a delivery's payments to the UI's view, newest first.</summary>
    /// <param name="reference">The request reference.</param>
    /// <returns>One view per attempt; declined attempts stay for audit (DA4).</returns>
    public IReadOnlyList<PaymentView> PaymentHistory(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        DeliveryContext? context = _registry.Find(reference);
        return context is null
            ? Array.Empty<PaymentView>()
            : context.Delivery.Payments
                .OrderByDescending(p => p.AttemptedAt)
                .Select(p => new PaymentView(
                    p.AttemptedAt.DateTime,
                    p.MethodName,
                    p.Amount,
                    p.WasSuccessful,
                    p.Outcome?.ToString() ?? "Pending"))
                .ToList();
    }

    /// <summary>Maps a delivery's history to the UI's view, oldest first.</summary>
    /// <param name="reference">The request reference.</param>
    /// <returns>The domain's own audit trail, without exposing domain objects.</returns>
    public IReadOnlyList<EventView> StatusHistory(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        DeliveryContext? context = _registry.Find(reference);
        return context is null
            ? Array.Empty<EventView>()
            : context.Delivery.History
                .Select(e => new EventView(e.OccurredAt.DateTime, e.From, e.To ?? context.Delivery.Status, e.ActorId, e.Note))
                .ToList();
    }

    private OperationResult? Lookup(string reference, out DeliveryContext context)
    {
        context = _registry.Find(reference)!;
        return context is null ? OperationResult.Fail($"No request has the reference {reference}.") : null;
    }

    private static string Actor(UserSession by) => by.StaffId ?? by.Username;

    private Payment? TryPayment(string reference, DeliveryContext context, string action, UserSession by, PaymentMethod method, decimal amount, out string refusal)
    {
        refusal = string.Empty;
        try
        {
            return context.Delivery.RecordPaymentAttempt(EventId(reference, action, by), Actor(by), Now, method, amount);
        }
        catch (InvalidOperationException ex)
        {
            refusal = ex.Message;
            return null;
        }
        catch (ArgumentException ex)
        {
            refusal = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Builds the replay key (DA9) for one command. The Idempotency-Key header is honoured when the
    /// caller sends a valid Guid, combined with the action so one header cannot collide across two
    /// different commands (e.g. two parcels ticked in the same minute). Otherwise a deterministic Guid
    /// comes from reference + action + actor + minute, so a double-submitted POST in the same minute
    /// collapses onto the Domain's replay memory.
    /// </summary>
    private Guid EventId(string reference, string action, UserSession by)
    {
        string? header = _http?.HttpContext?.Request.Headers["Idempotency-Key"].FirstOrDefault();
        string seed = string.IsNullOrWhiteSpace(header)
            ? string.Create(CultureInfo.InvariantCulture, $"{reference}|{action}|{Actor(by)}|{Now.ToUnixTimeSeconds() / 60}")
            : string.Create(CultureInfo.InvariantCulture, $"{header.Trim()}|{action}");
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash.AsSpan(0, 16));
    }
}

/// <summary>
/// Strict-HTTP surface over <see cref="DeliveryServiceAdapter"/>: the same Domain commands, but a
/// refusal comes back as 400 with <see cref="ApiError"/> so a client sees the validation message.
/// Routed under /api/delivery so it never collides with the Vue UI's /api/jobs routes.
/// </summary>
public static class DeliveryController
{
    /// <summary>Maps the delivery endpoints.</summary>
    /// <param name="app">The endpoint route builder.</param>
    public static void MapDeliveryEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/api/delivery/{reference}/parcels/{parcelId}",
            (string reference, string parcelId, CollectedBody body, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.RequireOwnJob(context, reference, out UserSession session) is { } denied)
            {
                return denied;
            }

            OperationResult result = Adapter(services).SetParcelCollected(reference, parcelId, body.Collected, session);
            return result.Succeeded ? Results.Ok(result) : ApiAccess.BadRequest(result.Message);
        });

        app.MapPost("/api/delivery/{reference}/payments/cash",
            (string reference, CashBody body, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.RequireOwnJob(context, reference, out UserSession session) is { } denied)
            {
                return denied;
            }

            if (body.AmountReceived <= 0m || body.AmountReceived > 10_000m)
            {
                return ApiAccess.BadRequest("Enter the cash received, in dollars.");
            }

            PaymentOutcomeView outcome = Adapter(services).TakeCash(reference, decimal.Round(body.AmountReceived, 2), session);
            return outcome.Successful ? Results.Ok(outcome) : ApiAccess.BadRequest(outcome.Message);
        });

        app.MapPost("/api/delivery/{reference}/payments/card",
            (string reference, CardBody body, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.RequireOwnJob(context, reference, out UserSession session) is { } denied)
            {
                return denied;
            }

            PaymentOutcomeView outcome = Adapter(services).TakeCard(reference, body.Approved, session);
            return outcome.Successful ? Results.Ok(outcome) : ApiAccess.BadRequest(outcome.Message);
        });

        app.MapPost("/api/delivery/{reference}/confirm-collection",
            (string reference, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.RequireOwnJob(context, reference, out UserSession session) is { } denied)
            {
                return denied;
            }

            OperationResult result = Adapter(services).ConfirmCollection(reference, session);
            return result.Succeeded ? Results.Ok(result) : ApiAccess.BadRequest(result.Message);
        });

        app.MapPost("/api/delivery/{reference}/delivered",
            (string reference, DeliveredBody body, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.RequireOwnJob(context, reference, out UserSession session) is { } denied)
            {
                return denied;
            }

            string name = body.ReceivedBy?.Trim() ?? string.Empty;
            if (InputRules.Name(name) is { } error)
            {
                return ApiAccess.BadRequest(error);
            }

            OperationResult result = Adapter(services).RecordDelivered(reference, name, session);
            return result.Succeeded ? Results.Ok(result) : ApiAccess.BadRequest(result.Message);
        });

        app.MapPost("/api/delivery/{reference}/failure",
            (string reference, ReasonBody body, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.RequireOwnJob(context, reference, out UserSession session) is { } denied)
            {
                return denied;
            }

            string reason = body.Reason?.Trim() ?? string.Empty;
            if (InputRules.Reason(reason) is { } error)
            {
                return ApiAccess.BadRequest(error);
            }

            OperationResult result = Adapter(services).RecordFailure(reference, reason, session);
            return result.Succeeded ? Results.Ok(result) : ApiAccess.BadRequest(result.Message);
        });

        app.MapGet("/api/delivery/{reference}/payments",
            (string reference, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.RequireOwnJob(context, reference, out _) is { } denied)
            {
                return denied;
            }

            return Results.Ok(Adapter(services).PaymentHistory(reference));
        });

        app.MapGet("/api/delivery/{reference}/history",
            (string reference, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.RequireOwnJob(context, reference, out _) is { } denied)
            {
                return denied;
            }

            return Results.Ok(Adapter(services).StatusHistory(reference));
        });
    }

    private static DeliveryServiceAdapter Adapter(DmsServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.Deliveries as DeliveryServiceAdapter
            ?? throw new InvalidOperationException(
                "DmsServices.Deliveries must be a DeliveryServiceAdapter; the bootstrap still wires the demo backend.");
    }
}

