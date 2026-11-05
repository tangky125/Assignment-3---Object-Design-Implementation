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
    Dms.Domain.QuotingSelfTest.Run();
    return;
}

if (args.Contains("--integration-selftest", StringComparer.Ordinal))
{
    IntegrationSelfTest.Run();
    return;
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Url);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Areas 1 and authentication still use the temporary in-memory service. The
// initialiser replaces the Area 3 demo allocation handler and the Area 4 test registry.
var (services, registry, store, backend) = DmsInitialiser.Create(builder.Environment.ContentRootPath);

builder.Services.AddSingleton(services);
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<ApiAccess>();

WebApplication app = builder.Build();
// Commit all successful business mutations before completing the request.
// Both domain delivery state and the UI read models are persisted together.
app.Use(async (context, next) =>
{
    await next(context);
    if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments("/api"))
    {
        registry.SyncAll();
        store.Save(backend);
    }
});
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

