# Web user interface: design notes and evidence guide

Material for the report: Section I (detailed design and changes), Section IV (architecture),
Section V (code quality) and Section VI (compilation and execution evidence).

## 1. Platform and how to run

| | Used for development and testing |
|---|---|
| Server | C# 12 / .NET 8 SDK, ASP.NET Core minimal API (`Microsoft.NET.Sdk.Web`) |
| User interface | Vue 3.5 + TypeScript 5.6, Vue Router 4, built with Vite 5 (Node.js 22) |
| Tests and lint | Vitest 2 + Vue Test Utils, vue-tsc, ESLint 9 (eslint-plugin-vue, typescript-eslint), Prettier 3 |
| Browser | Any current browser (Chrome, Edge, Firefox, Safari); works on phones |
| IDE | Visual Studio Code (with the Vue - Official and C# Dev Kit extensions) or Visual Studio 2022 |

See `Docs/How-to-run.md` for step-by-step commands.

## 2. Architecture (for Section IV)

**Client–server**: the browser runs the Vue app; the C# program serves the app and a JSON web API on
`http://localhost:5080`. Connector: HTTP + JSON, with a sign-in token in the `Authorization` header.

**Layered** on the server: Presentation (Vue app) → Web API (`Api/Endpoints`, `ApiAccess`) →
Business services (`Api/Contracts` interfaces, implemented by each area) → Domain (`Domain/`, `Quoting/`,
`Staff/`) → Storage. Each layer only calls the layer below it. The Web API depends on the service
interfaces, not on domain classes (Dependency Inversion).

**MVC-style presentation** inside the Vue app: views (`src/views`) display and collect input,
reusable components (`src/components`) render shared pieces, and the typed API client (`src/api`)
is the only code that talks to the server. Business rules stay on the server; the UI only checks the
format of what is typed and asks the server's StatusLifecycle which actions are legal.

## 3. UI modules (for the class diagram)

| Module | Responsibility |
|---|---|
| `App.vue` | Branded header, role-based navigation, sign-out, footer with privacy/terms links |
| `router/index.ts` | Screens, and which roles may open each (server checks again) |
| `session/session.ts` | Signed-in user, token, roles; lifecycle table from the server |
| `session/flash.ts` | Message carried to the next screen ("Quote v1 issued") |
| `api/client.ts` | Typed `fetch` wrapper; turns error responses into plain-language messages |
| `api/dms.ts` | One function per API endpoint, grouped by business area |
| `api/types.ts` | TypeScript mirror of the C# records |
| `validation/rules.ts` | Format rules with actionable messages (names, AU phone, postcode, money, dates...) |
| `utils/format.ts` | Australian formats: `$1,027.50`, `Tue 03/11/2026 09:30`, late check |
| `components/FormField.vue` | Label + hint + error, linked to the input for screen readers |
| `components/MessageBanner.vue` | Success / Error / Note messages with words as well as colour |
| `components/ConfirmBox.vue` | "Are you sure?" before anything is saved (change of mind) |
| `components/StepList.vue` | Progress for the six-step request form |
| `components/AddressFields.vue`, `ContactFields.vue` | Reusable address and contact inputs |
| `components/CustomerSearch.vue` | Find by phone / close name, or register a new customer |
| `components/RequestTable.vue` | Request lists (become stacked cards on phones) |
| `components/RequestDetailPanel.vue` | Full request by viewer level: summary, quote, parcels, payments, history |
| `views/HomeView`, `SignInView`, `DashboardView`, `HelpView`, `SignedOutView` | Home, sign-in, role menu, help/privacy/terms, exit screen |
| `views/requests/LogRequestView` | **Area 1**: six steps, review with change/remove, discard |
| `views/quotes/QuoteQueueView`, `PrepareQuoteView`, `OwnerApprovalView` | **Area 2** (office/owner) |
| `views/requests/RequestView`, `CustomerRequestsView`, `FindCustomerView` | **Area 2** (customer decision), cancel, quote versions, receipt |
| `views/allocation/AllocationView`, `AllocateView` | **Area 3**: allocate / replace with reasons |
| `views/jobs/MyJobsView`, `JobView` | **Area 4**: parcel checklist, cash/card, confirm pickup, deliver, fail |
| `views/track/TrackView`, `SearchView` | Tracking by reference; staff search and open deliveries |

Server-side UI support (C#): `Api/Endpoints/*Endpoints.cs` (one class per area), `ApiAccess`
(who may call what), `SessionStore` (sign-in tokens), `Api/Validation/InputRules` (the same format
rules, checked again on the server).

## 4. Changes from Assignment 2 caused by the UI (for Section I)

| Change | Justification |
|---|---|
| Added a web presentation layer (Vue app) and a web API | A2 S1 deferred the UI; A1 specified a web application and A2 DA1 assumed browser clients |
| One API endpoint class and one screen per business task, not one controller | A2 4.5 discarded "Menu/SystemController" as a god class (Riel 3.2) |
| MVC-style presentation | A2 6.5.5 rejected MVC only because UI was out of scope |
| Added `IAuthService`, `UserSession` (roles as a set), `SessionStore` | A2 AccessPolicy checks "the caller" but no class knew who the caller was; set of roles keeps DA3 |
| Added `ApiAccess` | Puts the A2 AccessPolicy permission checks in one place for every request |
| Service interfaces per business area + view records | Four people build in parallel; UI never depends on domain classes |
| Business refusals returned as `OperationResult` messages | A1 actionable error messages; no exceptions as control flow |
| Drafts live only in the browser until submitted | A1 Task 1 variant 6a without A2's "discard abandoned drafts" responsibility |
| Format validation in the UI and again in the API; business rules in the domain | The brief asks for input validation; rules such as service area and expiry stay with their owners |
| Screens ask the lifecycle table which actions to show | One owner of status rules (A2 Contract 2); the UI cannot offer an illegal transition |

## 5. Usability (A1 design-level requirements)

| A1 requirement | How the UI meets it |
|---|---|
| Branding | Logo and "Your Order We Deliver" on every page header |
| Responsiveness | Layout adapts from desktop to phone; lists become cards on narrow screens (delivery staff) |
| Accessibility (WCAG 2.1 AA) | Every input has a label; hints and errors linked with `aria-describedby`; `aria-invalid`; errors announced (`role="alert"`); visible focus outline; "Skip to main content"; text labels ("Error:", "Done:") as well as colour; AA contrast |
| Legal visibility | Privacy policy and terms links in every footer; payment receipt (with GST) for customers |
| Actionable errors | Message under the field saying what to type, e.g. "Postcode must be exactly 4 digits, e.g. 3000." |
| Change of mind | Back buttons; review step with Change/Remove; "Are you sure?" before saving; Discard; Undo changes |

### Input validation

| Input | Rule |
|---|---|
| Names | Required, 2–60 characters, letters/numbers/`. , ' & -`, at least one letter |
| Phone | Australian 10 digits: `04xx xxx xxx` or `0[2378] xxxx xxxx` (`+61` accepted) |
| Street / suburb / postcode | Required; postcode exactly 4 digits; pickup must be in the service area (server check) |
| Ready time | A real date and time, in the future, at most 14 days ahead |
| Parcels | 1–10; each needs a size; description ≤ 40 characters |
| Price | Number, $1–$10,000; a reason is required when it differs from the proposal |
| Windows | End after start, at most 12 h; pickup not before ready time; delivery not before pickup |
| Reasons | 3–200 characters |
| Cash | A number; at least the amount due (server check) |
| Reference | `DMS-yyyy-nnnn` (any case) |

## 6. Coding standards (for Section V)

- **TypeScript / Vue**: the official Vue Style Guide (`eslint-plugin-vue` "recommended" rules),
  typescript-eslint recommended rules, explicit return types, strict TypeScript, Prettier formatting.
  Check: `npm run lint` and `npm run type-check`.
  References: vuejs.org/style-guide, typescript-eslint.io/rules, prettier.io.
- **C#**: Microsoft C# coding conventions and .NET naming guidelines (`.editorconfig` + .NET analyzers).
  Check: `dotnet format --verify-no-changes`.

## 7. Evidence for the marksheet (Section VI)

Demo accounts (password `demo123`): `jane` customer, `olivia` office, `sam` owner + office,
`ben` / `ava` / `raj` delivery staff. Demo data is created fresh each time the server starts.

| Marksheet item | What to capture |
|---|---|
| Explicit evidence of compilation (5) | `dotnet build` (**Build succeeded**) and `npm run build` (vue-tsc type-check + Vite build) |
| Home screen (1) | `Docs/Screenshots/01-home.png` |
| Successful data input (9) | Step (ii) of each scenario below |
| Validate and process inputs (5) | Step (iii) of each scenario |
| Sample outputs (5) | Step (v) of each scenario; request history; receipt; tracking |
| Exit and test screens (5) | Sign out → "You have signed out" (`47-exit-signed-out.png`); `npm test` (17 tests passed) |

Screenshots of every step are in `Docs/Screenshots/` (taken from a real run). File names say the
scenario and the step: `s1-i-…` = scenario 1, empty screen; `-ii-` correct input; `-iii-` wrong input
rejected; `-iv-` change of mind; `-v-` successful result.

**Scenario 1 – Log a request (Area 1)** · `jane` → Log request.
(i) `05-s1-i-empty-pickup` (iii) `06`, `07` out-of-area postcode 2000, `08` blank recipient / phone `12345`
(ii) `09` parcels, `10` review (iv) `11` after removing parcel 2 and changing the destination; `13`–`14` discard
(v) `12` submitted with reference `DMS-2026-0009`.

**Scenario 2 – Quote and decision (Area 2)** · `olivia` → Quotes → DMS-2026-0001.
(i) `17` queue, `18` proposal (iii) `19` price `abc`, missing reason, window before ready time (iv) `20` undo
(v) `21` issued. Customer: `23` quote, (iv) `24` revision requested, `25` confirm, (v) `26` accepted + versions.
Expired quote refused: `15`. Owner prices an out-of-area quote: `29`. Receipt: `27`.

**Scenario 3 – Allocate staff (Area 3)** · `olivia` → Allocation → DMS-2026-0004.
(i) `30` lists, `31` staff with reasons (iii) `32` busy staff refused (iv) `33` not allocated, `35` replace
(v) `36` allocated.

**Scenario 4 – Execute and pay (Area 4)** · `ben` → My jobs → DMS-2026-0006.
(i) `37`, `38` (iii) `39` pickup before ticking, `40` cash short, `43` signature needed
(iv) `41` card declined → cash (ii) `42` cash paid with change (v) `44` delivered with full history.
Failed pickup: `45`. Phone layout: `46`.

**Tracking** · `48` bad / unknown references, `49` reference holder view (no price), `50` staff search,
`51` staff view of a request.
