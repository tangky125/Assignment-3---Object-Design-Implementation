# Area 3 implementation and JSON integration

## Included

- `DmsInitialiser` assembles one application graph and loads the JSON snapshot.
- `StaffRegister` checks active delivery staff, shift coverage and window clashes using the current tracking data.
- `AllocationCoordinator` applies office/owner authorisation, final eligibility checks, first allocation and replacement with a mandatory reason.
- `JsonDeliveryRegistry` shares delivery aggregates with the Area 4 adapter. Area 2 creates a delivery after quote acceptance and the registry uses it when available.
- `JsonApplicationStore` saves a versioned snapshot of customers, requests, quotes, statuses, assignments, payments, parcel flags, history and event IDs to `Data/dms-state.json`, using a temporary file and atomic replacement.
- Domain restoration preserves delivery status and replay identifiers, without recharging payments.
- `program.cs` writes the snapshot after API POST requests. Existing Vue endpoints remain unchanged.

## Run

Requires .NET 8 SDK (and optionally Node 20+ for rebuilding the frontend):

```sh
dotnet build
dotnet run --selftest
dotnet run --integration-selftest
dotnet run
```

Then open http://localhost:5080 and sign in as `olivia` / `demo123` to allocate a delivery. `ben`, `ava`, and `raj` are example driver logins. The repository includes an already-built `wwwroot` frontend, so Node is not needed merely to run the web app.

## Test scenarios

1. Open allocation queue, choose an accepted delivery, inspect staff availability, allocate an eligible staff member; inspect the driver's jobs.
2. Sign in as the customer and confirm customer allocation is denied.
3. Attempt a second assignment to the same staff and verify it is refused.
4. Choose a scheduled delivery and try replacement with a reason, verifying that the existing status stays Scheduled and that the assignment changes.
5. Tick parcels, record payment, record handover; verify tracking and the receipt.
6. Restart the server, verify assignments, customer requests, quote history, parcel flags, payment history, delivery status and event history still exist.
7. Run `dotnet run --integration-selftest` to exercise an isolated test directory without changing the main data file.

## Persistence notes

The application's live JSON state is under the ignored `Data/` folder. Do not check customer information or saved payment history into Git. The seed users and staff shifts are retained for the coursework demonstration. This project is **not production-grade** authentication, payment processing, or database storage.

There are two separate legacy limitations: the old `Data/delivery-assignments.json` used by the earlier partial patch is not imported into the new `dms-state.json` snapshot automatically; if it contains valuable test allocations, migrate them through the UI or retain a backup. The API's Area 1 and Area 2 adapters still use the existing in-process read-model/quoting code, now saved as JSON. They are not converted into database-backed repositories.

## Verification status

The ZIP was produced in an environment without the .NET SDK, so no C# compilation or runtime integration test could be executed here. The included self-test must be run on a .NET 8 machine before declaring the submission verified.
