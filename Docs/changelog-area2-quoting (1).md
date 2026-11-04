# Area 2: quoting and the customer's decision — interpretation decisions

Area 2 (`IQuoteService`: A1 Task 2 Prepare Quote, Task 3 Accept or Reject). A2 classes:
Quote, ServiceArea, PricingPolicy, ZonePricingPolicy, OwnerApprovedPricingPolicy, SystemConfiguration,
plus QuoteNegotiator (extracted, see below). Files: `Quoting/` (domain, namespace `Dms.Domain`),
`Api/Controllers/QuoteServiceAdapter.cs` (adapter), `Api/Demo/DemoQuoteRegistry.cs` (temporary bridge).

## Domain rules

- A2 hand-over note "if DeliveryRequest grows, extract quote negotiation"; implementation adds `QuoteNegotiator`, which owns quoting state per request (status until acceptance, every quote version, history), because `DeliveryRequest` is still a stub and A2 already counted 11 responsibilities on it.
- A2 CRC DeliveryRequest "Knows its current status" before a delivery exists; `QuoteNegotiator` holds the status for Requested → Quoted → Accepted / Rejected, and from acceptance the `Delivery` owns it (birth state Accepted, contract rule 3), so the status is never held in two places at once.
- A2 CRC DeliveryRequest "Creates its delivery when a quote is accepted"; `QuoteNegotiator.Accept` creates `new Delivery(request, lifecycle)` and hands it to the registry for Areas 3 and 4.
- Contract rule 2: every status change goes through `StatusLifecycle`; `QuoteNegotiator` checks the move **before** changing anything (`RequireMove`) and writes the status in one private `Move`, so a refused command leaves no trace.
- Contract rules 4 and 5 (DA9, no clock): every public command takes a `Guid eventId` and the caller's `DateTimeOffset`; a repeated id is ignored, and a replayed issue returns the quote already issued.
- A2 S3 "quote expiry is evaluated when the quote is next consulted, not by a timer"; `Quote.IsExpired(at)` and `QuoteNegotiator.ExpireIfDue` implement it. The adapter consults it when the office opens the quote queue and when a customer tries to accept, so an expired request reappears in the queue (A1 Task 2 variant 5a) and an expired quote cannot be accepted (Task 3 variant 1a).
- A1 A10 "quotes are valid for 24 hours"; `SystemConfiguration.QuoteValidity`. An out-of-area quote's validity starts when the owner releases it, not when office staff send it for pricing, because the customer could not act on it before then.
- A2 DA11: the pickup must be in the service area; `ServiceArea.SelectPolicy` refuses an out-of-area pickup and selects `OwnerApprovedPricingPolicy` for an out-of-area destination (Strategy: the request never knows which policy priced it).
- A2 DA6 "owners configure pricing rules … rule editing is out of scope"; rates live in `SystemConfiguration.Default` (zones A/B/C $15/$22/$30, extra parcel $4, Medium $5, Large $12, fragile $5, signature $2, keep upright $3, express ×1.5 within 2 hours). The dearer zone of pickup and destination sets the base rate.
- A1 Task 2 subtask 3 "office staff may adjust it, and the system records the adjusted figure and the reason"; `Quote` keeps the proposed and adjusted price separately and refuses an adjustment without a reason. Re-entering the proposed price is not treated as an adjustment.
- A1 Task 2 variant 3a "both versions are retained"; every version stays on the negotiator and a new version records the one it revises (`Quote.Revises`).
- A1 Task 3 subtask 2 "whether the decision was made by the customer or recorded by office staff"; `Quote.DecisionRecordedByStaff`, shown as "Accepted (recorded by office staff)".
- A1 Task 2 subtask 4 "derives the windows from the current availability pool"; the domain proposes windows from the ready time (rounded up to the half hour; pickup 2 h, delivery from +1 h for 3 h). Checking them against staff availability belongs to Area 3's `AllocationCoordinator` and is left as a hand-off: allocation re-checks availability when a staff member is chosen.
- `TimeWindow` (A2 data holder) is a record that refuses an end before its start; `Overlaps` is provided for Area 3.
- `QuoteNegotiator.Restore` and `Quote.Restore` implement A2 bootstrap stage 3 (restore records at start-up); the bridge uses them to rebuild state from stored records.
- No `Console.WriteLine` in domain classes; `QuotingSelfTest` is the only printing class in `Quoting/`, like Ky's `SelfTest`.
- Domain messages format dates and money with the invariant culture so they read the same on every computer.

## Adapter and wiring

- `QuoteServiceAdapter : IQuoteService` mirrors Ky's `DeliveryServiceAdapter`: refusals (`InvalidOperationException`, `ArgumentException`) become `OperationResult.Fail` with the domain's message, and every command gets a replay key from the `Idempotency-Key` header or a deterministic hash. The hash includes the **quote version** (e.g. `issue-v2`), so issuing version 2 a minute after version 1 is a new command, while a double-clicked button is still a replay.
- `IQuoteRegistry` is the storage seam (like `IDeliveryRegistry`): `Find` gives the negotiator and the read model, `Saved` is called after every successful command, and `DeliveryCreated` hands accepted deliveries on.
- Area 1 (requests, customers) is still the demo backend, so `DemoQuoteRegistry` restores one negotiator per request from the demo's records and, through `DemoBackend.MirrorQuoteState`, copies every Area 2 result (status, quote versions, history) back into the demo's records. That keeps tracking, the customer's request list and the allocation queue consistent with the real domain. When Area 1 is real, replace the bridge with the RequestRegister and storage; the adapter does not change.
- The UI and the API routes did not change: `/api/quotes/*` and `/api/requests/{ref}/accept|reject|revise|quotes` now reach the real domain through `DmsServices.Quotes`.

## Hand-offs and gaps

- **Area 3:** accepted deliveries are kept in `DemoQuoteRegistry.AcceptedDeliveries` (the `Delivery` object plus the amount due). The real `AllocationCoordinator` should take them from there and call `Delivery.RecordAllocation`.
- **Area 4:** `TestDeliveryRegistry` still serves only the seeded `DMS-2026-0006`; once Area 3 is real it can look deliveries up in the same place.
- **Notifications:** A2 has DeliveryRequest notify observers on status changes; Ky's `StatusObserver` takes a `Delivery`, so the negotiator has no observer list yet. Messages still say the customer was notified (wording kept from the demo).
- **Cancellation** before acceptance is not a legal move in the current lifecycle table (Requested/Quoted → Cancelled); after acceptance it is `Delivery.ApplyCustomerCancellation` (Ky's), still answered by the demo.

## Evidence

- `dotnet run --selftest`: 77 checks (30 Area 4 + 47 Area 2), PASS/FAIL per line.
- Browser walkthrough of every Area 2 flow (14 screenshots in `Docs/Screenshots/Area2/`): expired quote refused; queue oldest first; real proposal $36.00 with breakdown; wrong price / missing reason rejected; undo; issue v1; customer asks for revision; v2 at an adjusted price; acceptance with versions and history; accepted request appears in the allocation queue; rejection closes the request; out-of-area quote held, owner prices it at $75.00; office records the customer's phone acceptance.
