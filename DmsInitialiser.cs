using Dms.Api.Contracts;
using Dms.Api.Demo;
using Dms.Domain;

namespace Dms.Api.Controllers;

/// <summary>Creates a single shared graph of services and registers persistent state.</summary>
public static class DmsInitialiser
{
    public static (DmsServices Services, JsonDeliveryRegistry Registry, JsonApplicationStore Store, DemoBackend Backend)
        Create(string contentRoot)
    {
        DmsServices initial = DemoBackend.Create(() => DateTime.Now);
        var backend = (DemoBackend)initial.Tracking;
        var store = new JsonApplicationStore(Path.Combine(contentRoot, "Data", "dms-state.json"));
        store.Load(backend);
        StatusLifecycle lifecycle = initial.Lifecycle;
        SystemConfiguration configuration = SystemConfiguration.Default;
        var quotes = new DemoQuoteRegistry(initial, lifecycle, configuration);
        var deliveries = new JsonDeliveryRegistry(backend, quotes, lifecycle);
        DmsServices services = initial with
        {
            Quotes = new QuoteServiceAdapter(quotes, new ServiceArea(configuration), () => DateTimeOffset.Now),
            Allocation = new AllocationCoordinator(backend, new StaffRegister(backend.StaffDirectory, backend), deliveries),
            Deliveries = new DeliveryServiceAdapter(deliveries, () => DateTimeOffset.Now),
        };
        return (services, deliveries, store, backend);
    }
}
