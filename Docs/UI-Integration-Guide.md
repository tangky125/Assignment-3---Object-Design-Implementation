# UI integration guide (for the whole team)

The user interface is a **Vue 3 + TypeScript** web app (`WebUi/`). It runs in the browser and talks
to the C# program over HTTP (`/api/...`). The C# side has three parts:

| Folder | What it is | Who changes it |
|---|---|---|
| `Api/Endpoints/` | The web API the Vue app calls (routes, input checks, who may call what) | UI owner |
| `Api/Contracts/Services.cs` | **One interface per business area** — what the API needs from the domain | Each area owner implements one |
| `Api/Demo/DemoBackend.cs` | **Temporary** in-memory stand-in so the UI works before the domain classes exist | Delete when the real services are wired |

The UI never touches domain classes directly, so you can name and shape your classes however you like.
Only `Domain/Status` and `Domain/StatusLifecycle` are shared (the API publishes the lifecycle table at
`GET /api/lifecycle` so the UI only offers legal actions).

## Who implements what

| Interface | Area | Suggested owner | Wraps these A2 classes |
|---|---|---|---|
| `IRequestService` | 1 Log request | Requests owner | CustomerDirectory, RequestRegister, DeliveryRequest, Parcel, ServiceArea |
| `IQuoteService` | 2 Quote + decision | Clarice | Quote, ServiceArea, PricingPolicy (+2), QuoteNegotiator |
| `IAllocationService` | 3 Allocate staff | Maleesha | AllocationCoordinator, StaffRegister, DeliveryStaffProfile, AvailabilitySchedule |
| `IDeliveryService` | 4 Execute + payment | Ky | Delivery, Payment, PaymentMethod, Cash/CardPayment, PaymentGateway |
| `ITrackingService` | Tracking | AccessPolicy owner | RequestRegister, AccessPolicy |
| `IAuthService` | Sign-in | AccessPolicy owner | UserAccount / AccessPolicy |

Suggested pattern: one small adapter class in your own folder, e.g.
`Quoting/QuoteServiceAdapter.cs : IQuoteService`, that calls your classes and converts results into
the records in `Api/Contracts/Views.cs`.

## Rules the UI relies on

1. **Refusals are results, not crashes.** When a business rule says no (expired quote, staff member
   busy, cash short), return `OperationResult.Fail("plain message saying what to do")`. The web page
   shows it in a red "Error:" box and lets the user try again. Only throw for real bugs.
2. **Write messages for the user**, e.g. "Postcode 2000 is outside our Greater Melbourne service area."
   (A1 design-level requirement: actionable error messages).
3. **Every status change goes through `StatusLifecycle.CanTransition`** (Ky's contract rule).
4. **Return the records in `Views.cs`**, never your live objects.
5. **No card data.** `IDeliveryService.TakeCard` receives only approved/declined.

## Expected behaviour

`Api/Demo/DemoBackend.cs` is a working example of every method. Read the matching method there before
writing yours.

| Method | Used by screen | Must do |
|---|---|---|
| `FindCustomers` | Customer search | Exact phone match **or** close name match (typos allowed) |
| `CheckPickupAddress` | Log request, step 2 | Fail if the pickup postcode is outside the service area |
| `SubmitRequest` | Log request, submit | Save with status Requested, return the new reference |
| `AwaitingQuote` | Prepare quotes | Requested and not held for an owner, **oldest first** |
| `Propose` | Prepare a quote | Price + breakdown lines, windows, warnings; `NeedsOwnerApproval` for out-of-area |
| `IssueQuote` | Prepare a quote | New version; Requested → Quoted, or hold for owner |
| `Accept` | Request page | Fail if expired (and move Quoted → Requested); else Quoted → Accepted + create Delivery |
| `RequestRevision` | Request page | Quoted → Requested; keep the old version |
| `StaffOptions` | Allocate | Every active delivery staff member with `Eligible` + a reason |
| `Allocate` / `Replace` | Allocate | Re-check eligibility when saving; Allocate moves to Scheduled |
| `TakeCash` / `TakeCard` | Job page | At most one successful payment; declined attempts kept |
| `ConfirmCollection` | Job page | Fail until every parcel is ticked and paid; then Collected |
| `RecordFailure` | Job page | → Delivery Failed; release the staff member (back in the allocation queue) |
| `LevelFor` | Tracking | null viewer = Recipient; own customer = Customer; staff = Staff; else None |

## Switching from the stand-in to the real classes (bootstrap owner)

In `program.cs` replace

```csharp
DmsServices services = DemoBackend.Create(() => DateTime.Now);
```

with the `DmsServices` built by `DmsInitialiser`, e.g.

```csharp
DmsServices services = new DmsInitialiser(dataFolder: "Data").Build();
// Build() returns new DmsServices(auth, requests, quotes, allocation, deliveries, tracking, lifecycle)
```

and `app.MapAuthEndpoints(DemoBackend.AccountsHelp)` with `app.MapAuthEndpoints(Array.Empty<string>())`
(or your own help lines). Nothing in `WebUi/` changes.

## Notes for Ky (StatusLifecycle)

Compared with A2 Figure 1, the current table:

- has **no Scheduled → Accepted** (A2 "allocation removed"), so the UI offers *Replace staff* but not *Remove*;
- has **no Scheduled → Cancelled** (A2 had it), but **does** have **Collected → Cancelled** (A2 did not),
  so a customer can cancel after the parcels have been collected but not before;
- sends **Delivery Failed → Scheduled** directly (A2: → Accepted).

Each difference from A2 needs a justification in the report (Section I, dynamic changes).
