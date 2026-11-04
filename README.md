# Delivery Management System (DMS)

.NET 8 web API + Vue 3 / TypeScript UI over a locked domain layer.

## Run

    dotnet run                      # http://localhost:5080 (built UI is served from wwwroot/)
    cd WebUi && npm run build       # only needed after changing the UI

## Test inputs
Delivery Staff:
Log in as ben / pass: demo123, type ref/job DMS-2026-0006

Quoting (Area 2):
Office staff: olivia / demo123, open **Quote queue** (DMS-2026-0001 prices at $36.00; DMS-2026-0004 is out of area and goes to the owner).
Owner: sam / demo123, open **Owner approval** to price an out-of-area quote.
Customer: jane / demo123, open **My requests** to accept, reject or ask for a revision.

## Checks

| Command | Shows |
|---|---|
| `dotnet build` | Compiles: **Build succeeded, 0 warnings, 0 errors** |
| `dotnet format --verify-no-changes` | Code follows `.editorconfig` (Microsoft conventions) |
| `dotnet run --selftest` | 77 domain checks (30 Area 4 + 47 Area 2), PASS/FAIL per line, no web server started |
| `cd WebUi && npm run build` | TypeScript type-check and UI build |
| `cd WebUi && npm test` | UI tests |

## Layout

    Assignment3.csproj          net8.0 Web SDK, RootNamespace Dms
    program.cs                  boot point: DI wiring, endpoint mapping, --selftest
    Domain/                     locked business layer (no UI, no I/O, no clock)
    Quoting/                    Area 2 domain: Quote, QuoteNegotiator, ServiceArea, PricingPolicy (+Zone, OwnerApproved), SystemConfiguration, TimeWindow, QuotingSelfTest
    Api/Contracts/              one interface per business area + the records the UI sees
    Api/Endpoints/              minimal-API routes the Vue app calls (/api/...)
    Api/Controllers/            DeliveryController + DeliveryServiceAdapter (Area 4), QuoteServiceAdapter (Area 2)
    Api/Demo/DemoBackend.cs     temporary in-memory stand-in for Areas 1 and 3, tracking, sign-in
    Api/Demo/DemoQuoteRegistry.cs  bridge: restores Area 2 negotiators from demo records, mirrors results back
    Api/Validation/             InputRules: format checks with actionable messages
    WebUi/                      Vue + TypeScript source (builds into wwwroot/)
    wwwroot/                    built UI served by the API
    Data/                       JSON at runtime
    Docs/                       change log, integration guide, screenshots

## Key flows (screenshots in `Image/`)

1. **Sign in**
   ![Front page](Image/frontpage.png)
   ![Sign in page](Image/signin.png)
   Enter credentials (e.g. `ben` / `demo123`) on the landing page.

2. **Staff dashboard**
   ![Staff page](Image/staffpage.png)
   Delivery staff view shows allocated jobs and availability controls.

3. **Job list**
   ![Job list](Image/joblist.png)
   Office staff see the queue of requests awaiting quote or allocation.

4. **Execute delivery**
   ![Delivery job](Image/deliveryjob.png)
   Driver ticks parcels, takes payment (cash/card), records hand-over or failure.

5. **Track delivery**
   ![Track search](Image/track.png)
   ![Track result](Image/trackresult.png)
   Customer or office staff search by reference and view status history.

## Contract rules

1. The 9 statuses live in `Domain/status.cs` (`Status` enum). Never use strings, never invent a 10th.
2. No direct status writes: every change goes through `StatusLifecycle.CanTransition` / `ExplainRefusal`, and inside `Delivery` only through its private `Move`.
3. Birth states: a new `DeliveryRequest` starts at `Requested`; a new `Delivery` (created on quote acceptance) starts at `Accepted`.
4. Every public domain command takes a `Guid eventId`; a repeated id is ignored (DA9). A replayed `RecordPaymentAttempt` returns the recorded `Payment`.
5. Timestamps are `DateTimeOffset` parameters supplied by the caller; the domain never reads a clock.
6. No `Console.WriteLine` in domain classes — user-facing text is returned as data. Only `SelfTest` and `QuotingSelfTest` print.

## Areas and owners

| Interface | Area | Owner | Domain classes |
|---|---|---|---|
| `IRequestService` | 1 Log request | Requests owner | DeliveryRequest, Parcel, CustomerDirectory, ServiceArea |
| `IQuoteService` | 2 Quote + decision | Clarice | Quote, ServiceArea, PricingPolicy (+Zone, OwnerApproved), QuoteNegotiator, SystemConfiguration |
| `IAllocationService` | 3 Allocate staff | Maleesha | AllocationCoordinator, StaffRegister, DeliveryStaffProfile |
| `IDeliveryService` | 4 Execute + payment | Ky | Delivery, Payment, PaymentMethod, Cash/CardPayment, PaymentGateway |
| `ITrackingService`, `IAuthService` | Tracking, sign-in | AccessPolicy owner | RequestRegister, UserAccount, AccessPolicy |

## Wiring today

- `program.cs` builds one `DmsServices(Auth, Requests, Quotes, Allocation, Deliveries, Tracking, Lifecycle)`.
- Areas 1 and 3, tracking and sign-in still come from `DemoBackend`.
- **Area 2 is real** — `QuoteServiceAdapter` over `QuoteNegotiator` / `Quote` / `ServiceArea`. `DemoQuoteRegistry` restores one negotiator per request from the demo's records and mirrors every result (status, quote versions, history) back, so tracking and the allocation queue stay consistent. Accepted deliveries are kept in `DemoQuoteRegistry.AcceptedDeliveries` for Area 3. See `Docs/changelog-area2-quoting.md`.
- **Area 4 is real** — `DeliveryServiceAdapter` over the locked `Delivery` aggregate.
- `TestDeliveryRegistry` (bottom of `program.cs`) gives that adapter one seeded **Scheduled** delivery: `DMS-2026-0006`, allocated to `ben (S003)`, $34.00. Read models still come from the demo.
- Replace `demo with { Quotes = …, Deliveries = … }` with `DmsInitialiser` when the other areas are real; nothing in `WebUi/` changes.

## Endpoints

- `/api/jobs/*` — what the Vue job page calls; goes through `IDeliveryService`.
- `/api/delivery/*` — same commands on a strict-HTTP surface: refusals return **400 + `{ "message": … }`**.

## Known gaps

- Unticking a parcel is not supported: the locked `Parcel` only exposes `MarkCollected()`, so the adapter refuses it.
- Payment is accepted while the job is `Scheduled` or `Collected` (A5 pickup window); declined attempts stay in history (DA4).
- Quote windows are proposed from the ready time; checking them against staff availability is left to Area 3's allocation.
- Cancelling before acceptance is not a legal move in the lifecycle table yet.
- `Storage/` is not implemented — the demo backend keeps everything in memory.
