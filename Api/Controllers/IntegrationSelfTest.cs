using Dms.Api.Contracts;
using Dms.Domain;

namespace Dms.Api.Controllers;

/// <summary>Repeatable end-to-end service test with an isolated temporary JSON file.</summary>
public static class IntegrationSelfTest
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "dms-area3-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (services, registry, store, backend) = DmsInitialiser.Create(root);
            UserSession office = services.Auth.SignIn("olivia", "demo123")
                ?? throw new InvalidOperationException("Office login failed.");
            // Seeded references use the year of the current clock, not a fixed year.
            string job = services.Allocation.AwaitingAllocation()
                .First(x => x.Reference.EndsWith("-0004", StringComparison.Ordinal)).Reference;
            StaffOption staff = services.Allocation.StaffOptions(job)
                .First(x => x.Eligible);
            UserSession customer = services.Auth.SignIn("jane", "demo123")
                ?? throw new InvalidOperationException("Customer login failed.");
            Expect(!services.Allocation.Allocate(job, staff.StaffId, customer).Succeeded,
                "customer role was allowed to allocate staff");
            OperationResult<CustomerSummary> registered = services.Requests.RegisterCustomer(
                "Integration Customer", "0400 222 333", "integration@example.com");
            Expect(registered.Succeeded && registered.Value is not null, "customer registration failed");
            var pickup = new AddressInput("10 Swanston Street", "Melbourne", "3000");
            var contact = new ContactInput("Integration Customer", "0400 222 333");
            var newDraft = new RequestDraft(registered.Value!.CustomerId, pickup, contact,
                DateTime.Now.AddDays(2), new AddressInput("5 Collins Street", "Melbourne", "3000"),
                contact, new[] { new ParcelInput(SizeBand.Small, ParcelHandling.None, "Test parcel") }, null);
            OperationResult<string> newRequest = services.Requests.SubmitRequest(newDraft, office);
            Expect(newRequest.Succeeded && newRequest.Value is not null, "new request submission failed");
            Expect(services.Allocation.Allocate(job, staff.StaffId, office).Succeeded, "allocation failed");
            registry.SyncAll();
            store.Save(backend);

            var (restored, restoredRegistry, restoredStore, restoredBackend) = DmsInitialiser.Create(root);
            Expect(restored.Tracking.Find(newRequest.Value!) is not null,
                "new request was not restored");
            Expect(restored.Requests.GetCustomer(registered.Value!.CustomerId) is not null,
                "new customer was not restored");
            RequestDetail allocated = restored.Tracking.Find(job)
                ?? throw new InvalidOperationException("Job missing after restart.");
            Expect(allocated.Status == Status.Scheduled, "scheduled status was not restored");
            Expect(allocated.AllocatedStaffId == staff.StaffId, "staff assignment was not restored");
            UserSession driver = restored.Auth.SignIn(
                staff.StaffId == "S003" ? "ben" : staff.StaffId == "S004" ? "ava" : "raj", "demo123")
                ?? throw new InvalidOperationException("Driver login failed.");
            Expect(restored.Deliveries.JobsFor(staff.StaffId).Any(x => x.Reference == job),
                "job was missing from the driver's queue");
            decimal amount = restored.Deliveries.AmountDue(job);
            // A5: the customer pays at pickup, so nothing is ticked before the money is in.
            if (amount > 0m)
            {
                Expect(!restored.Deliveries.SetParcelCollected(job, allocated.Parcels[0].ParcelId, true, driver).Succeeded,
                    "a parcel was ticked before the customer paid");
            }

            Expect(restored.Deliveries.TakeCash(job, amount, driver).Successful, "cash attempt failed");
            foreach (ParcelView parcel in allocated.Parcels)
                Expect(restored.Deliveries.SetParcelCollected(job, parcel.ParcelId, true, driver).Succeeded,
                    "parcel collection failed");
            Expect(restored.Deliveries.RecordDelivered(job, "Recipient", driver).Succeeded,
                "handover failed");
            restoredRegistry.SyncAll();
            restoredStore.Save(restoredBackend);

            var (again, _, _, _) = DmsInitialiser.Create(root);
            RequestDetail completed = again.Tracking.Find(job)
                ?? throw new InvalidOperationException("Completed job missing after restart.");
            Expect(completed.Status == Status.Delivered, "delivery completion was not restored");
            Expect(completed.Parcels.All(x => x.Collected), "parcel flags were not restored");
            Expect(completed.Payments.Any(x => x.Successful), "payment was not restored");
            Console.WriteLine("PASS: allocation, driver queue, collection, payment, delivery and JSON restart");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
    }
}
