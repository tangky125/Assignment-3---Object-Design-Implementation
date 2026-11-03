using System.Globalization;
using Dms.Api.Contracts;
using Dms.Api.Validation;
using Dms.Domain;

namespace Dms.Api.Endpoints;

/// <summary>Area 1 (customers, logging requests) and tracking.</summary>
public static class RequestEndpoints
{
    private const int MaxParcels = 10;

    /// <summary>Maps the endpoints.</summary>
    public static void MapRequestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/customers", (string? phone, string? name, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.RequireOffice(context, out _) is { } denied)
            {
                return denied;
            }

            string? normalPhone = string.IsNullOrWhiteSpace(phone) ? null : InputRules.NormalisePhone(phone);
            if (phone is not null && phone.Length > 0 && normalPhone is null)
            {
                return ApiAccess.BadRequest("Enter an Australian phone number with 10 digits, e.g. 0412 345 678.");
            }

            string? trimmedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            return normalPhone is null && trimmedName is null
                ? ApiAccess.BadRequest("Enter a phone number or a name to search.")
                : Results.Ok(services.Requests.FindCustomers(normalPhone, trimmedName));
        });

        app.MapPost("/api/customers", (NewCustomerBody body, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.RequireOffice(context, out _) is { } denied)
            {
                return denied;
            }

            string? error = InputRules.Name(body.Name ?? string.Empty)
                ?? InputRules.AustralianPhone(body.Phone ?? string.Empty)
                ?? InputRules.OptionalEmail(body.Email ?? string.Empty);
            return error is not null
                ? ApiAccess.BadRequest(error)
                : Results.Ok(services.Requests.RegisterCustomer(body.Name!.Trim(), InputRules.NormalisePhone(body.Phone!)!, string.IsNullOrWhiteSpace(body.Email) ? null : body.Email.Trim()));
        });

        app.MapGet("/api/customers/{id}", (string id, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.RequireOfficeOrCustomer(context, id, out _) is { } denied)
            {
                return denied;
            }

            return services.Requests.GetCustomer(id) is { } customer ? Results.Ok(customer) : ApiAccess.NotFound("No such customer.");
        });

        app.MapGet("/api/customers/{id}/recent-addresses", (string id, HttpContext context, ApiAccess access, DmsServices services) =>
            access.RequireOfficeOrCustomer(context, id, out _) ?? Results.Ok(services.Requests.RecentAddressPairs(id)));

        app.MapGet("/api/customers/{id}/requests", (string id, HttpContext context, ApiAccess access, DmsServices services) =>
            access.RequireOfficeOrCustomer(context, id, out _) ?? Results.Ok(services.Quotes.RequestsForCustomer(id)));

        app.MapPost("/api/requests/check-pickup", (AddressInput address, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.Require(context, out _, UserRole.Customer, UserRole.OfficeStaff, UserRole.Owner) is { } denied)
            {
                return denied;
            }

            string? error = AddressError(address);
            return error is not null ? ApiAccess.BadRequest(error) : Results.Ok(services.Requests.CheckPickupAddress(address));
        });

        app.MapPost("/api/requests", (RequestDraft draft, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.Require(context, out UserSession session, UserRole.Customer, UserRole.OfficeStaff, UserRole.Owner) is { } denied)
            {
                return denied;
            }

            bool office = session.Has(UserRole.OfficeStaff) || session.Has(UserRole.Owner);
            if (!office && draft.CustomerId != session.CustomerId)
            {
                return ApiAccess.BadRequest("Customers can only log requests for themselves.");
            }

            string? error = DraftError(draft);
            return error is not null ? ApiAccess.BadRequest(error) : Results.Ok(services.Requests.SubmitRequest(Normalise(draft), session));
        });

        app.MapGet("/api/requests/{reference}", (string reference, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            string id = InputRules.NormaliseReference(reference);
            if (InputRules.Reference(id) is { } formatError)
            {
                return ApiAccess.BadRequest(formatError);
            }

            UserSession? viewer = access.Caller(context);
            ViewerLevel level = services.Tracking.LevelFor(viewer, id);
            RequestDetail? detail = services.Tracking.Find(id);
            if (detail is null || level == ViewerLevel.None)
            {
                return ApiAccess.NotFound($"No delivery you can view has the reference {id}. Check the number and try again.");
            }

            return Results.Ok(level switch
            {
                ViewerLevel.Recipient => new TrackingResponse(level, null, ForRecipient(detail, DateTime.Now)),
                ViewerLevel.Customer => new TrackingResponse(level, ForCustomer(detail), null),
                _ => new TrackingResponse(level, detail, null),
            });
        });

        app.MapGet("/api/requests/{reference}/quotes", (string reference, HttpContext context, ApiAccess access, DmsServices services) =>
            access.RequireOfficeOrOwnRequest(context, reference, out _) ?? Results.Ok(services.Quotes.QuoteHistory(reference)));

        app.MapPost("/api/requests/{reference}/accept", (string reference, HttpContext context, ApiAccess access, DmsServices services) =>
            access.RequireOfficeOrOwnRequest(context, reference, out UserSession session) ?? Results.Ok(services.Quotes.Accept(reference, session)));

        app.MapPost("/api/requests/{reference}/reject", (string reference, ReasonBody body, HttpContext context, ApiAccess access, DmsServices services) =>
            access.RequireOfficeOrOwnRequest(context, reference, out UserSession session)
            ?? WithReason(body, reason => services.Quotes.Reject(reference, reason, session)));

        app.MapPost("/api/requests/{reference}/revise", (string reference, ReasonBody body, HttpContext context, ApiAccess access, DmsServices services) =>
            access.RequireOfficeOrOwnRequest(context, reference, out UserSession session)
            ?? WithReason(body, reason => services.Quotes.RequestRevision(reference, reason, session)));

        app.MapPost("/api/requests/{reference}/cancel", (string reference, ReasonBody body, HttpContext context, ApiAccess access, DmsServices services) =>
            access.RequireOfficeOrOwnRequest(context, reference, out UserSession session)
            ?? WithReason(body, reason => services.Requests.Cancel(reference, reason, session)));

        app.MapGet("/api/search", (string? reference, string? name, string? phone, string? date, HttpContext context, ApiAccess access, DmsServices services) =>
        {
            if (access.Require(context, out _, UserRole.OfficeStaff, UserRole.Owner, UserRole.DeliveryStaff) is { } denied)
            {
                return denied;
            }

            DateOnly? day = null;
            if (!string.IsNullOrWhiteSpace(date))
            {
                if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed))
                {
                    return ApiAccess.BadRequest("Enter the date as a calendar date.");
                }

                day = parsed;
            }

            string? normalPhone = string.IsNullOrWhiteSpace(phone) ? null : InputRules.NormalisePhone(phone);
            if (!string.IsNullOrWhiteSpace(phone) && normalPhone is null)
            {
                return ApiAccess.BadRequest("Enter an Australian phone number with 10 digits, e.g. 0412 345 678.");
            }

            string? normalReference = string.IsNullOrWhiteSpace(reference) ? null : InputRules.NormaliseReference(reference);
            string? normalName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            return Results.Ok(services.Tracking.Search(normalReference, normalName, normalPhone, day));
        });

        app.MapGet("/api/open-requests", (HttpContext context, ApiAccess access, DmsServices services) =>
            access.Require(context, out _, UserRole.OfficeStaff, UserRole.Owner, UserRole.DeliveryStaff) ?? Results.Ok(services.Tracking.OpenRequests()));
    }

    private static IResult WithReason(ReasonBody body, Func<string, OperationResult> action)
    {
        string reason = body.Reason?.Trim() ?? string.Empty;
        return InputRules.Reason(reason) is { } error ? ApiAccess.BadRequest(error) : Results.Ok(action(reason));
    }

    private static string? AddressError(AddressInput? a) =>
        a is null
            ? "The address is missing."
            : InputRules.Street(a.Street ?? string.Empty) ?? InputRules.Suburb(a.Suburb ?? string.Empty) ?? InputRules.Postcode(a.Postcode ?? string.Empty);

    private static string? ContactError(ContactInput? c) =>
        c is null ? "The contact is missing." : InputRules.Name(c.Name ?? string.Empty) ?? InputRules.AustralianPhone(c.Phone ?? string.Empty);

    private static string? DraftError(RequestDraft d)
    {
        if (d.Parcels is null || d.Parcels.Count is 0 or > MaxParcels)
        {
            return $"A request needs between 1 and {MaxParcels} parcels.";
        }

        if (d.Parcels.Any(p => p.Description is { Length: > 40 }))
        {
            return "Parcel descriptions can be at most 40 characters.";
        }

        if (d.Notes is { Length: > 200 })
        {
            return "Notes can be at most 200 characters.";
        }

        if (d.ReadyTime == default)
        {
            return "Enter when the parcels will be ready.";
        }

        return AddressError(d.Pickup) ?? ContactError(d.PickupContact) ?? AddressError(d.Destination) ?? ContactError(d.Recipient);
    }

    private static RequestDraft Normalise(RequestDraft d) => d with
    {
        PickupContact = d.PickupContact with { Phone = InputRules.NormalisePhone(d.PickupContact.Phone)! },
        Recipient = d.Recipient with { Phone = InputRules.NormalisePhone(d.Recipient.Phone)! },
        Notes = string.IsNullOrWhiteSpace(d.Notes) ? null : d.Notes.Trim(),
    };

    private static RecipientView ForRecipient(RequestDetail d, DateTime now)
    {
        TimeWindowView? expected = d.CurrentQuote?.DeliveryWindow;
        bool late = d.CurrentQuote is { } q
            && ((d.Status == Status.Scheduled && now > q.PickupWindow.End) || (d.Status == Status.Collected && now > q.DeliveryWindow.End));
        return new RecipientView(d.Reference, d.Status, expected, d.Destination.Suburb, late);
    }

    private static RequestDetail ForCustomer(RequestDetail d) => d with
    {
        Notes = null,
        AllocatedStaffId = null,
        History = d.History.Select(e => e with { Actor = string.Empty }).ToList(),
    };
}
