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

## Usability changes after team testing

Raised while testing the Area 2 flows as office staff:

- **Why a request is in the quote queue.** `AwaitingQuote` now tags every row `New` (never quoted), `Revision` (the customer asked for a revised quote) or `Expired` (the last quote ran out with no decision), and the note gives the detail, e.g. "Revision asked: price too high". The tag travels in a new optional `RequestSummary.Tag`; rows still come oldest first.
- **The previous quote on the prepare page.** When a request comes back after a revision or an expiry, *Prepare a quote* shows the previous version's price and the customer's reason above the price field. `QuoteView` gained optional `Decision`, `DecisionReason` and `ConfirmedVia` so the UI does not have to parse the outcome text.
- **Work counts.** The navigation shows how many requests are waiting (Quotes, Owner approvals) and the main menu summarises the queue ("3 requests waiting for a quote: 1 new, 1 revision asked, 1 expired."). Counts come from the same queues (`WebUi/src/session/workCounts.ts`) and refresh on every page change and once a minute; screen readers hear "Quotes, 3 waiting".
- **How the customer confirmed.** A1 Task 3 subtask 2 records whether the customer or office staff made the decision. When office staff record one, they must now also say how the customer gave it (phone call, email, in person, or other with a description, plus who and when). `Quote.RecordDecision` refuses a staff-recorded decision without it, `Quote.DecisionConfirmedVia` keeps it, and it appears in the quote outcome and the history ("recorded by office staff; confirmed by phone call: Jane Citizen, 10:15"). Customers deciding online are never asked. `IQuoteService.Accept/Reject/RequestRevision` take the extra `confirmedVia` argument, and the endpoints read it from the request body.
- **Replay fix (DA9).** The negotiator used to remember a command's replay key before checking it, so a refused command (e.g. a missing reason) that was corrected and resent within the same minute was silently ignored. It now remembers the key only once the command succeeds; a self-test check covers it.

Evidence: `--selftest` 82 checks (30 Area 4 + 52 Area 2); UI type-check, lint and 17 UI tests pass; a browser walkthrough of the four changes (screenshots 15-21 in `Docs/Screenshots/Area2/`) and a rerun of the original 14-step walkthrough, both with no failures and no console errors.

## Customer alerts and status colours

- **Telling the customer a quote is ready.** The system does not send SMS or email, so the customer's own screens now make it obvious. `RequestsForCustomer` applies lazy expiry, then tags a request whose quote is waiting for the customer's decision `QuoteReady`, or `ExpiresSoon` within 4 hours of the deadline (`QuoteServiceAdapter.ExpiresSoonWithin`), with the price and the deadline as the note. The customer's navigation shows **My requests** with a count, and the main-menu tile says e.g. "1 quote ready for you to decide: DMS-2026-0001, $36.00, decide by Thu 05/11/2026 17:00." Office staff see the same tags when they open a customer's requests from *Customer decisions*. A notifications panel was considered and left out as out of scope.
- **A quote that lapsed before the customer looked.** Because the customer's list now applies lazy expiry, an expired quote is back in the office queue before the customer opens it (A1 Task 2 variant 5a). Their list tags it `Expired` ("Quote v1 expired; a new quote is being prepared"), and the request page explains that it expired without a decision and can no longer be accepted. A page left open from before the expiry is still refused by the server with the expiry message (Task 3 variant 1a), not the lifecycle wording.
- **Status colours.** Blue waits on the office (Requested), orange waits on the customer (Quoted), green is agreed (Accepted), teal is in progress (Scheduled, Collected), solid green with a tick is finished (Delivered), red is a problem (Delivery failed) and grey is closed (Rejected, Cancelled). The badge text always names the status, so colour is never the only signal, and every badge meets WCAG AA contrast (5.7:1 or better).

Evidence: UI type-check, lint and 17 UI tests pass; `--selftest` 82/0; browser walkthroughs (customer alerts, office usability, and the original 14 steps with step 1 updated for the lapsed-quote message) with no failures and no console errors. Screenshots 22-24 in `Docs/Screenshots/Area2/`.
