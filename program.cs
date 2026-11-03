using System.Text.Json.Serialization;
using Dms.Api;
using Dms.Api.Contracts;
using Dms.Api.Demo;
using Dms.Api.Endpoints;

// Boot point of the Delivery Management System.
//
//   1. cd WebUi && npm install && npm run build    builds the Vue + TypeScript UI into wwwroot/
//   2. dotnet run                                  starts the API and serves the UI
//   3. open http://localhost:5080
//
// Bootstrap owner: replace DemoBackend.Create(...) with the DmsServices built by DmsInitialiser
// once the domain classes are ready. Nothing else in this file needs to change.
const string Url = "http://localhost:5080";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Url);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

DmsServices services = DemoBackend.Create(() => DateTime.Now);
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
app.MapFallbackToFile("index.html");

app.Lifetime.ApplicationStarted.Register(() =>
    Console.WriteLine($"Delivery Management System is running. Open {Url} in your browser (Ctrl+C to stop)."));
app.Run();
