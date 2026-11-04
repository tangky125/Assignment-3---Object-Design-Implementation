namespace Dms.Domain;

/// <summary>
/// Proves the Area 2 rules (A1 Tasks 2 and 3) on demand: pricing, windows, quote validity and the
/// negotiation lifecycle. Run with <c>dotnet run --selftest</c>; prints PASS/FAIL per line.
/// </summary>
public static class QuotingSelfTest
{
    private static readonly TimeSpan Melbourne = TimeSpan.FromHours(11);
    private static readonly DateTimeOffset At = new(2026, 11, 2, 9, 0, 0, Melbourne);
    private static readonly SystemConfiguration Config = SystemConfiguration.Default;

    /// <summary>Runs every check and prints PASS/FAIL per line.</summary>
    public static void Run()
    {
        CheckPricing();
        CheckWindowsAndValidity();
        CheckIssueAndDecide();
        CheckExpiry();
        CheckRejection();
        CheckOwnerApproval();
        CheckRestore();
    }

    // a. ServiceArea selects the policy; ZonePricingPolicy applies the owners' rules
    private static void CheckPricing()
    {
        ServiceArea area = new(Config);
        Check("postcode 3000 is in zone A", area.ZoneOf("3000")?.Name == "A");
        Check("postcode 3220 is outside the service area", !area.Contains("3220"));
        Check("pickup outside the service area is refused",
            Throws<InvalidOperationException>(() => area.SelectPolicy("2000", "3000"), "outside our Greater Melbourne service area"));
        Check("in-area destination is priced by zone rules", area.SelectPolicy("3000", "3181") is ZonePricingPolicy);
        Check("out-of-area destination is priced by an owner", area.SelectPolicy("3121", "3220") is OwnerApprovedPricingPolicy);

        PriceProposal standard = area.ProposePrice(Subject("3000", "3181", At.AddDays(1), Small(fragile: true), Medium()), At);
        Check("A->B, small fragile + medium costs $36.00 (22 + 4 + 5 + 5)", standard.Price == 36m);
        Check("breakdown lists base, extra parcel, size and handling", standard.Breakdown.Count == 4);
        Check("zone price needs no owner approval", !standard.NeedsOwnerApproval);

        PriceProposal dearer = area.ProposePrice(Subject("3000", "3200", At.AddDays(1), Small()), At);
        Check("the dearer zone sets the base rate (zone C $30.00)", dearer.Price == 30m);

        PriceProposal express = area.ProposePrice(Subject("3000", "3001", At.AddHours(1), Small()), At);
        Check("ready within 2 hours is express: $15.00 x 1.5 = $22.50", express.Price == 22.50m);

        PriceProposal owner = area.ProposePrice(Subject("3121", "3220", At.AddDays(1), Small()), At);
        Check("out-of-area proposal has no price and needs an owner", owner.Price is null && owner.NeedsOwnerApproval);
    }

    // b. windows are proposed from the ready time; a quote is valid for 24 hours
    private static void CheckWindowsAndValidity()
    {
        Check("a window must end after it starts",
            Throws<ArgumentException>(() => _ = new TimeWindow(At, At), "must end after it starts"));

        QuoteNegotiator negotiator = New(Subject("3000", "3181", At.AddMinutes(10), Small()));
        (TimeWindow pickup, TimeWindow delivery) = negotiator.ProposeWindows(At);
        Check("pickup window starts at the ready time rounded up to the half hour",
            pickup.Start == At.AddMinutes(30) && pickup.Length == TimeSpan.FromHours(2));
        Check("delivery window opens 1 hour after pickup opens and lasts 3 hours",
            delivery.Start == pickup.Start.AddHours(1) && delivery.Length == TimeSpan.FromHours(3));

        Quote quote = Issue(negotiator, pickup, delivery);
        Check("a quote is valid for 24 hours from issue", quote.ValidUntil == At.AddHours(24));

        QuoteNegotiator early = New(Subject("3000", "3181", At.AddHours(3), Small()));
        Check("pickup before the parcels are ready is refused",
            Throws<ArgumentException>(() => Issue(early, Window(At.AddHours(1), 2), Window(At.AddHours(2), 3)), "before the parcels are ready"));
        Check("a delivery window before the pickup window is refused",
            Throws<ArgumentException>(() => Issue(early, Window(At.AddHours(4), 2), Window(At.AddHours(3), 3)), "cannot start before the pickup window"));
        Check("an adjusted price without a reason is refused",
            Throws<ArgumentException>(
                () => early.IssueQuote(Guid.NewGuid(), "S001", At, Proposal(36m), 30m, "  ", Window(At.AddHours(3), 2), Window(At.AddHours(4), 3)),
                "reason is required"));
        Check("a refused issue leaves the request Requested with no quote", early.Status == Status.Requested && early.Quotes.Count == 0);
    }

    // c. A2 Scenario 8.2: issue, revise, re-issue, accept
    private static void CheckIssueAndDecide()
    {
        QuoteNegotiator negotiator = New(Subject("3000", "3181", At.AddDays(1), Small()));
        Check("a new request is Requested and waiting for a quote", negotiator.Status == Status.Requested && negotiator.IsWaitingForQuote);

        Guid issueId = Guid.NewGuid();
        Quote v1 = negotiator.IssueQuote(issueId, "S001", At, Proposal(36m), null, null, Window(At.AddDays(1), 2), Window(At.AddDays(1).AddHours(1), 3));
        Check("issuing moves Requested -> Quoted", negotiator.Status == Status.Quoted);
        Check("issuing records a status event", negotiator.History.Count == 1 && negotiator.History[0].To == Status.Quoted);
        Check("a replayed issue returns the same quote (DA9)",
            ReferenceEquals(negotiator.IssueQuote(issueId, "S001", At, Proposal(36m), null, null, Window(At.AddDays(1), 2), Window(At.AddDays(1).AddHours(1), 3)), v1)
            && negotiator.Quotes.Count == 1);
        Check("issuing again while Quoted is refused",
            Throws<InvalidOperationException>(() => Issue(negotiator, Window(At.AddDays(1), 2), Window(At.AddDays(1).AddHours(1), 3)), "Illegal: Quoted -> Quoted"));

        negotiator.RequestRevision(Guid.NewGuid(), "C001", false, At.AddHours(1), "Price too high");
        Check("a revision request returns the request to Requested", negotiator.Status == Status.Requested);
        Check("the revised version is kept with its reason",
            v1.Decision == QuoteDecision.RevisionRequested && v1.DecisionReason == "Price too high");

        Quote v2 = negotiator.IssueQuote(Guid.NewGuid(), "S001", At.AddHours(2), Proposal(36m), 30m, "Matched competitor",
            Window(At.AddDays(1), 2), Window(At.AddDays(1).AddHours(1), 3));
        Check("version 2 records that it revises version 1", v2.Version == 2 && ReferenceEquals(v2.Revises, v1));
        Check("the adjusted price is what the customer pays", v2.FinalPrice == 30m && v2.AdjustReason == "Matched competitor");

        Guid acceptId = Guid.NewGuid();
        Check("office staff must say how the customer confirmed (Task 3 subtask 2)",
            Throws<ArgumentException>(() => negotiator.Accept(acceptId, "S001", true, At.AddHours(3), " "), "how the customer gave this decision"));
        Check("a refused decision changes nothing", negotiator.Status == Status.Quoted && v2.Decision == QuoteDecision.Pending);

        // The same replay key is sent again after the refusal is corrected (DA9 must not swallow it).
        Delivery delivery = negotiator.Accept(acceptId, "S001", true, At.AddHours(3), "phone call from Jane at 10:15");
        Check("a corrected retry with the same replay key is carried out", negotiator.Status == Status.Accepted);
        Check("the confirmation is kept on the quote and in the history",
            v2.DecisionConfirmedVia == "phone call from Jane at 10:15"
            && negotiator.History[^1].Note.Contains("confirmed by phone call from Jane at 10:15", StringComparison.Ordinal));
        Check("a customer deciding online needs no confirmation note", v1.DecisionConfirmedVia is null);
        Check("acceptance moves Quoted -> Accepted", negotiator.Status == Status.Accepted);
        Check("acceptance creates a Delivery in its birth state Accepted",
            negotiator.Delivery is not null && ReferenceEquals(negotiator.Delivery, delivery) && delivery.Status == Status.Accepted);
        Check("the decision records that office staff entered it (variant 2c)", v2.DecisionRecordedByStaff);
        Check("history holds Quoted, Requested, Quoted, Accepted",
            negotiator.History.Select(e => e.To).SequenceEqual(new Status?[] { Status.Quoted, Status.Requested, Status.Quoted, Status.Accepted }));
    }

    // d. A1 Task 2 variant 5a and Task 3 variant 1a: lazy expiry
    private static void CheckExpiry()
    {
        QuoteNegotiator negotiator = New(Subject("3000", "3181", At.AddDays(1), Small()));
        Quote quote = Issue(negotiator, Window(At.AddDays(1), 2), Window(At.AddDays(1).AddHours(1), 3));
        DateTimeOffset late = quote.ValidUntil.AddMinutes(1);

        Check("nothing expires before the validity ends", !negotiator.ExpireIfDue(Guid.NewGuid(), quote.ValidUntil));
        Check("an expired quote cannot be accepted",
            Throws<InvalidOperationException>(() => negotiator.Accept(Guid.NewGuid(), "C001", false, late), "can no longer be accepted"));
        Check("a refused acceptance leaves the request Quoted", negotiator.Status == Status.Quoted && negotiator.Delivery is null);
        Check("lazy expiry returns the request to Requested", negotiator.ExpireIfDue(Guid.NewGuid(), late) && negotiator.Status == Status.Requested);
        Check("the expired version is marked Expired by the system",
            quote.Decision == QuoteDecision.Expired && quote.DecidedBy == QuoteNegotiator.SystemActor);
    }

    // e. rejection closes the request
    private static void CheckRejection()
    {
        QuoteNegotiator negotiator = New(Subject("3000", "3181", At.AddDays(1), Small()));
        Issue(negotiator, Window(At.AddDays(1), 2), Window(At.AddDays(1).AddHours(1), 3));
        Check("a rejection without a reason is refused",
            Throws<ArgumentException>(() => negotiator.Reject(Guid.NewGuid(), "C001", false, At, " "), "reason is required"));

        negotiator.Reject(Guid.NewGuid(), "C001", false, At, "Found a cheaper courier");
        Check("rejection moves Quoted -> Rejected", negotiator.Status == Status.Rejected);
        Check("a rejected request cannot be revised (Rejected is final)",
            Throws<InvalidOperationException>(() => negotiator.RequestRevision(Guid.NewGuid(), "C001", false, At, "Try again"), "Illegal: Rejected -> Requested"));
    }

    // f. A1 Task 2 variant 2a: an owner prices an out-of-area quote
    private static void CheckOwnerApproval()
    {
        ServiceArea area = new(Config);
        QuoteNegotiator negotiator = New(Subject("3121", "3220", At.AddDays(1), Large(upright: true)));
        PriceProposal proposal = negotiator.ProposePrice(area, At);
        Check("office staff cannot set an out-of-area price",
            Throws<InvalidOperationException>(
                () => negotiator.IssueQuote(Guid.NewGuid(), "S001", At, proposal, 50m, "Guess", Window(At.AddDays(1), 2), Window(At.AddDays(1).AddHours(1), 3)),
                "priced by an owner"));

        Quote held = negotiator.IssueQuote(Guid.NewGuid(), "S001", At, proposal, null, null, Window(At.AddDays(1), 2), Window(At.AddDays(1).AddHours(1), 3));
        Check("an out-of-area quote is held and the request stays Requested",
            held.AwaitingOwnerApproval && negotiator.Status == Status.Requested && negotiator.IsAwaitingOwner && !negotiator.IsWaitingForQuote);
        Check("a held quote cannot be accepted",
            Throws<InvalidOperationException>(() => negotiator.Accept(Guid.NewGuid(), "C002", false, At), "Illegal: Requested -> Accepted"));

        DateTimeOffset release = At.AddHours(5);
        negotiator.ReleaseOwnerPrice(Guid.NewGuid(), "S002", release, 75m);
        Check("the owner's release moves Requested -> Quoted at the owner's price",
            negotiator.Status == Status.Quoted && held.FinalPrice == 75m && held.PricedByOwner == "S002");
        Check("validity starts when the owner releases the quote", held.ValidUntil == release.AddHours(24));
    }

    // g. A2 bootstrap stage 3: restore from storage
    private static void CheckRestore()
    {
        Quote stored = Quote.Restore(1, 22m, null, Window(At, 2), Window(At.AddHours(2), 3), At.AddHours(-25), Config.QuoteValidity, QuoteDecision.Pending, null, null);
        QuoteNegotiator restored = QuoteNegotiator.Restore(
            Subject("3128", "3101", At, Small()), new DeliveryRequest("DMS-TEST"), new StatusLifecycle(), Config, Status.Quoted, new[] { stored });
        Check("restore rebuilds status and quote versions", restored.Status == Status.Quoted && restored.CurrentQuote == stored);
        Check("a restored quote issued 25 hours ago is expired", stored.IsExpired(At));
    }

    private static QuoteNegotiator New(QuoteSubject subject) =>
        new(subject, new DeliveryRequest(subject.Reference), new StatusLifecycle(), Config);

    private static Quote Issue(QuoteNegotiator negotiator, TimeWindow pickup, TimeWindow delivery) =>
        negotiator.IssueQuote(Guid.NewGuid(), "S001", At, Proposal(36m), null, null, pickup, delivery);

    private static PriceProposal Proposal(decimal price) => new(price, false, new[] { "test" });

    private static TimeWindow Window(DateTimeOffset start, int hours) => new(start, start.AddHours(hours));

    private static QuoteSubject Subject(string pickup, string destination, DateTimeOffset ready, params ParcelSpec[] parcels) =>
        new("DMS-TEST", pickup, destination, ready, parcels);

    private static ParcelSpec Small(bool fragile = false) => new("Small", fragile, false, false);

    private static ParcelSpec Medium() => new("Medium", false, false, false);

    private static ParcelSpec Large(bool upright = false) => new("Large", false, false, upright);

    private static bool Throws<TException>(Action action, string expectedText)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException ex)
        {
            return ex.Message.Contains(expectedText, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void Check(string name, bool passed) =>
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}");
}
