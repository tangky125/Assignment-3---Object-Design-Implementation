using System.Text.Json.Serialization;
using Dms.Api;
using Dms.Api.Contracts;
using Dms.Api.Controllers;
using Dms.Api.Demo;
using Dms.Api.Endpoints;
using Dms.Domain;

// Boot point of the Delivery Management System.
const string Url = "http://localhost:5080";

// Self-check harness: dotnet run --selftest runs the domain checks, prints PASS/FAIL per line and exits.
if (args.Contains("--selftest", StringComparer.Ordinal))
{
    Dms.Domain.SelfTest.Run();
    return;
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Url);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// TEMPORARY BOOTSTRAP FOR TESTING AREA 4.
// DemoBackend still answers Areas 1-3, tracking and sign-in; the real DeliveryServiceAdapter
// answers Area 4 over the locked Domain. Sign in as "ben" (password demo123) and open DMS-2026-0006.
DmsServices demo = DemoBackend.Create(() => DateTime.Now);
IDeliveryRegistry registry = new TestDeliveryRegistry(demo);
DmsServices services = demo with { Deliveries = new DeliveryServiceAdapter(registry, () => DateTimeOffset.Now) };

builder.Services.AddSingleton(services);
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<ApiAccess>();

WebApplication app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapAuthEndpoints(DemoBackend.AccountsHelp);
app.MapRequestEndpoints();
app.MapQuoteEndpoints();
app.MapAllocationEndpoints();
app.MapJobEndpoints();
app.MapDeliveryEndpoints();
app.MapFallbackToFile("index.html");

app.Lifetime.ApplicationStarted.Register(() =>
    Console.WriteLine($"Delivery Management System is running. Open {Url} in your browser (Ctrl+C to stop)."));

app.Run();

/// <summary>
/// Test seam for Area 4: hands the adapter one real <see cref="Delivery"/> in the Scheduled state,
/// built from the seeded request DMS-2026-0006 so the job page's access checks still pass.
/// Read models and the other areas keep coming from the demo backend until they are real.
/// </summary>
file sealed class TestDeliveryRegistry : IDeliveryRegistry
{
    private const string TestReference = "DMS-2026-0006";
    private const string TestStaffId = "S003";
    private const decimal TestAmountDue = 34m;

    private readonly DmsServices _demo;
    private readonly DeliveryContext _test;

    /// <summary>Builds the one test delivery and keeps the demo backend for read models.</summary>
    /// <param name="demo">The demo services, still used for Areas 1-3, tracking and sign-in.</param>
    public TestDeliveryRegistry(DmsServices demo)
    {
        _demo = demo;

        DeliveryRequest request = new(TestReference);
        request.Parcels.Add(new Parcel("P1", "Small"));
        request.Parcels.Add(new Parcel("P2", "Medium"));

        Delivery delivery = new(request, new StatusLifecycle());
        delivery.RecordAllocation(
            new DeliveryStaffProfile(TestStaffId, "Ben Nguyen"),
            Guid.NewGuid(),
            "office",
            DateTimeOffset.Now);

        _test = new DeliveryContext(delivery, request, TestAmountDue);
    }

    /// <inheritdoc/>
    public DeliveryContext? Find(string reference) =>
        string.Equals(reference, TestReference, StringComparison.OrdinalIgnoreCase) ? _test : null;

    /// <inheritdoc/>
    public IReadOnlyList<RequestSummary> JobsFor(string staffId) => _demo.Deliveries.JobsFor(staffId);

    /// <inheritdoc/>
    public RequestDetail? Detail(string reference) => _demo.Tracking.Find(reference);
}
