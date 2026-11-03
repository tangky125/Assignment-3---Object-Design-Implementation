# Payment stack — interpretation decisions (Ky)

Area 4 (`IDeliveryService`: Delivery, Payment, PaymentMethod, Cash/CardPayment, PaymentGateway).
Decisions marked **(agreed)** were confirmed by the domain owner on 11/03/2026.

## Domain rules

- A2 CRC Card 3 needs a settled-or-not payment; implementation keeps `PaymentOutcome` at two members and represents "pending" as `Outcome == null` because the brief fixes the enum at Approved/Declined.
- A6/DA5 forbid retaining card data; `CardPayment` holds only a token and nulls it after the gateway call because a serialisable payment record must never leak card details.
- A2 names a real card provider; implementation uses a deterministic `PaymentGateway` simulator (tokens starting "0000" decline) because the assignment needs no vendor account and an Adapter swap changes nothing in the domain.
- A5 says payment is taken at pickup; **(agreed)** `Delivery.RecordPaymentAttempt` accepts attempts while the status is `Scheduled` or `Collected` because the Vue job page takes payment before `confirm-collection`, and both states are still on site at the pickup address.
- DA4 allows one retained payment; **(agreed)** declined attempts stay in the history forever and `WasSuccessful` alone gates further attempts, so a customer can retry a failed card and the audit trail keeps the failure.
- **(agreed)** The outcome text stays "Approved" / "Declined" because that is what the payments table shows and what the domain already records.
- A2 leaves `DeliveryEvent` to Ky; the file was empty while `Delivery.cs` already constructs it with six positional arguments, so a minimal record was added with that exact shape (existing file wins).
- **(agreed)** `Delivery` passes the caller's `at` into `new Payment(...)` so `Payment.AttemptedAt` is always set and `PaymentView.At` can never show year 0001.
- Four compile-only stubs were added (Parcel, DeliveryRequest, DeliveryStaffProfile, StatusObserver), each with a `// STUB` header naming its owner, because the build cannot run until Reuben's and Maleesha's classes land.
- No `Console.WriteLine` appears in any domain class; `SelfTest` is the sole printing class because user-facing wording belongs to Reuben's UI layer.
- Domain classes never read the clock; `SelfTest` passes one fixed `DateTimeOffset` constant so the assertions stay deterministic.
- `Payment.Summary` formats money as `"$#,##0.00"` with the invariant culture (and no `IFormatProvider` warning) because `Api/Display.Money` sends the UI exactly that shape.
- **(agreed)** `Delivery.cs` was reformatted with braces to clear the 12 `IDE0011` warnings the team `.editorconfig` raised; behaviour is unchanged apart from the payment window above.

## Compatibility with the new Vue + API layer (pulled 606700d)

- The UI shows `PaymentView.Method` verbatim; implementation keeps `Payment.MethodName` as exactly `"Cash"` / `"Card"` because the Vue job page and payments table print that string.
- The UI colours the payments table from `PaymentView.Successful` and prints `PaymentView.Outcome`; implementation maps them from `WasSuccessful` and `Outcome`, using "Approved"/"Declined" (the demo's cash row says "Received", which is wording, not a rule).
- `PaymentView.At` is a `DateTime` while the domain uses `DateTimeOffset`; the adapter converts (`.DateTime`) because the domain must keep the offset for storage and audit.
- `PaymentOutcomeView.ChangeDue` has no domain equivalent; the adapter computes `amountReceived - Payment.Amount` because `Payment` deliberately records the amount taken, not what the customer handed over.
- API commands carry no `Guid eventId`; the adapter must mint one per call (or derive a deterministic one from reference + action) because domain replay protection is keyed on `eventId` (DA9).
- `IDeliveryService.TakeCash`/`TakeCard` are refused rather than thrown, per the integration guide; the adapter converts the domain's `InvalidOperationException` refusals into `PaymentOutcomeView(false, message, null)` because the UI shows refusals in a red box.
- The stub `Parcel` only ticks; `IDeliveryService.SetParcelCollected(..., false)` needs un-ticking, which `Delivery` has no command for, so un-ticking stays an open gap rather than being invented here.
- `StatusObserver` keeps its unprefixed name with a scoped CA1715/IDE1006 suppression because the team `.editorconfig` starts interfaces with `I` but `Delivery.cs` already references this name.
- `PaymentGateway` keeps one instance field (the provider name used in its messages) so the analyzer does not demand a static method, and because a vendor name is configuration a real adapter would carry.
- **(agreed)** `dotnet run --selftest` runs `SelfTest.Run()` and exits without starting the web host, so the 30 checks are evidence for the report.
