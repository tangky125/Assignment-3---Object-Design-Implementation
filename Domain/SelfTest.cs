namespace Dms.Domain;

/// <summary>The only class allowed to print: proves the status and payment rules on demand.</summary>
public static class SelfTest
{
    private static readonly DateTimeOffset At = new(2026, 3, 11, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Runs every check and prints PASS/FAIL per line.</summary>
    public static void Run()
    {
        CheckTransitions();
        CheckHappyPath();
        CheckPaymentWindow();
        CheckReplay();
        CheckFailureLoop();
        CheckGuards();
        CheckPaymentBeforeCollection();
        CheckAlwaysOnShift();
    }

    // a. refusal table
    private static void CheckTransitions()
    {
        StatusLifecycle lifecycle = new();

        Check("refuses Delivered -> Scheduled", Refused(lifecycle, Status.Delivered, Status.Scheduled));
        Check("refuses Requested -> Delivered", Refused(lifecycle, Status.Requested, Status.Delivered));
        Check("refuses every move out of Rejected",
            Enum.GetValues<Status>().All(s => s == Status.Rejected || Refused(lifecycle, Status.Rejected, s)));

        (DeliveryRequest request, Delivery delivery, _) = NewDelivery("P1");
        delivery.RecordAllocation(new DeliveryStaffProfile("S-1", "Ana"), Guid.NewGuid(), "coord", At);
        delivery.RecordParcelCollected(Guid.NewGuid(), "driver", At, request.Parcels[0]);
        delivery.RecordSuccessfulHandover(Guid.NewGuid(), "driver", At, "Customer");

        Check("finished delivery refuses reallocation with refusal text",
            Throws<InvalidOperationException>(
                () => delivery.RecordAllocation(new DeliveryStaffProfile("S-2", "Bo"), Guid.NewGuid(), "coord", At),
                "Illegal: Delivered -> Scheduled"));
    }

    // b. A2 Scenario 8.4 happy path, in the order the Vue job page drives it
    private static void CheckHappyPath()
    {
        (DeliveryRequest request, Delivery delivery, CountingObserver observer) = NewDelivery("P1", "P2");
        PaymentGateway gateway = new();

        delivery.RecordAllocation(new DeliveryStaffProfile("S-11", "Ana"), Guid.NewGuid(), "coord", At);
        Check("allocation moves Accepted -> Scheduled", delivery.Status == Status.Scheduled);

        delivery.RecordParcelCollected(Guid.NewGuid(), "driver", At, request.Parcels[0]);
        Check("first parcel keeps status Scheduled", delivery.Status == Status.Scheduled);

        delivery.RecordParcelCollected(Guid.NewGuid(), "driver", At, request.Parcels[1]);
        Check("last parcel moves status to Collected", delivery.Status == Status.Collected);

        Payment declined = delivery.RecordPaymentAttempt(
            Guid.NewGuid(), "driver", At, new CardPayment(gateway, "0000123456789012"), 42.50m);
        Check("card token 0000 is declined", declined.Outcome == PaymentOutcome.Declined);
        Check("card attempt names the method the UI shows", declined.MethodName == "Card");

        Payment approved = delivery.RecordPaymentAttempt(Guid.NewGuid(), "driver", At, new CashPayment(), 42.50m);
        Check("cash is approved", approved.WasSuccessful);
        Check("cash attempt names the method the UI shows", approved.MethodName == "Cash");
        Check("approved attempt carries wording for the UI", approved.OutcomeNote.Length > 0);

        delivery.RecordSuccessfulHandover(Guid.NewGuid(), "driver", At, "Customer");

        Check("handover moves status to Delivered", delivery.Status == Status.Delivered);
        Check("keeps exactly 2 payments", delivery.Payments.Count == 2);
        Check("keeps exactly 1 successful payment", delivery.Payments.Count(p => p.WasSuccessful) == 1);
        Check("history holds 5 events", delivery.History.Count == 5);
        Check("history holds 3 status events", delivery.History.Count(e => e.To is not null) == 3);
        Check("history holds 2 payment events",
            delivery.History.Count(e => e.Note.StartsWith("Payment attempt", StringComparison.Ordinal)) == 2);
        Check("observer notified 3 times", observer.Count == 3);
    }

    // c. A5 payment window: taken at pickup, so Scheduled (the UI order) or Collected, nothing else
    private static void CheckPaymentWindow()
    {
        (DeliveryRequest request, Delivery delivery, _) = NewDelivery("P1", "P2");
        delivery.RecordAllocation(new DeliveryStaffProfile("S-1", "Ana"), Guid.NewGuid(), "coord", At);

        delivery.RecordParcelCollected(Guid.NewGuid(), "driver", At, request.Parcels[0]);
        Payment atPickup = delivery.RecordPaymentAttempt(Guid.NewGuid(), "driver", At, new CashPayment(), 10m);
        Check("payment is accepted while Scheduled", atPickup.WasSuccessful && delivery.Status == Status.Scheduled);
        Check("payment keeps the caller's timestamp", atPickup.AttemptedAt == At);

        delivery.RecordParcelCollected(Guid.NewGuid(), "driver", At, request.Parcels[1]);
        delivery.RecordSuccessfulHandover(Guid.NewGuid(), "driver", At, "Customer");
        Check("payment after hand-over is refused",
            Throws<InvalidOperationException>(
                () => delivery.RecordPaymentAttempt(Guid.NewGuid(), "driver", At, new CashPayment(), 10m),
                "payment is taken at pickup"));
    }

    // d. replay protection (DA9)
    private static void CheckReplay()
    {
        (DeliveryRequest request, Delivery delivery, _) = NewDelivery("P1", "P2");
        delivery.RecordAllocation(new DeliveryStaffProfile("S-1", "Ana"), Guid.NewGuid(), "coord", At);

        Guid firstCollectionId = Guid.NewGuid();
        delivery.RecordParcelCollected(firstCollectionId, "driver", At, request.Parcels[0]);
        delivery.RecordParcelCollected(Guid.NewGuid(), "driver", At, request.Parcels[1]);
        int events = delivery.History.Count;

        bool silent = true;
        try
        {
            delivery.RecordParcelCollected(firstCollectionId, "driver", At, request.Parcels[0]);
            delivery.RecordParcelCollected(firstCollectionId, "driver", At, request.Parcels[1]);
        }
        catch (InvalidOperationException)
        {
            silent = false;
        }

        Check("replayed collection is silent and adds no event", silent && delivery.History.Count == events);

        Guid paymentId = Guid.NewGuid();
        Payment paid = delivery.RecordPaymentAttempt(paymentId, "driver", At, new CashPayment(), 42.50m);
        Payment replay = delivery.RecordPaymentAttempt(paymentId, "driver", At, new CashPayment(), 42.50m);
        Check("replayed payment returns the recorded payment",
            ReferenceEquals(paid, replay) && delivery.Payments.Count == 1);
    }

    // e. failed pickup, then reallocation (back in the allocation queue)
    private static void CheckFailureLoop()
    {
        (_, Delivery delivery, CountingObserver observer) = NewDelivery("P1");
        delivery.RecordAllocation(new DeliveryStaffProfile("S-1", "Ana"), Guid.NewGuid(), "coord", At);

        delivery.RecordFailedAttempt(Guid.NewGuid(), "driver", At, "Customer not home");
        Check("failed attempt moves Scheduled -> DeliveryFailed", delivery.Status == Status.DeliveryFailed);

        delivery.RecordAllocation(new DeliveryStaffProfile("S-2", "Bo"), Guid.NewGuid(), "coord", At);
        Check("reallocation moves DeliveryFailed -> Scheduled", delivery.Status == Status.Scheduled);
        Check("failure loop notified observer 3 times", observer.Count == 3);
    }

    // f. guards
    private static void CheckGuards()
    {
        (DeliveryRequest request, Delivery delivery, _) = NewDelivery("P1");

        Check("payment before pickup is refused",
            Throws<InvalidOperationException>(
                () => delivery.RecordPaymentAttempt(Guid.NewGuid(), "driver", At, new CashPayment(), 5m),
                "payment is taken at pickup"));

        delivery.RecordAllocation(new DeliveryStaffProfile("S-1", "Ana"), Guid.NewGuid(), "coord", At);
        delivery.RecordParcelCollected(Guid.NewGuid(), "driver", At, request.Parcels[0]);
        delivery.RecordPaymentAttempt(
            Guid.NewGuid(), "driver", At, new CardPayment(new PaymentGateway(), "0000000000001111"), 5m);
        delivery.RecordPaymentAttempt(Guid.NewGuid(), "driver", At, new CashPayment(), 5m);

        Check("third attempt after a successful payment is refused",
            Throws<InvalidOperationException>(
                () => delivery.RecordPaymentAttempt(Guid.NewGuid(), "driver", At, new CashPayment(), 5m),
                "already retained"));

        (_, Delivery blankReason, _) = NewDelivery("P1");
        blankReason.RecordAllocation(new DeliveryStaffProfile("S-1", "Ana"), Guid.NewGuid(), "coord", At);
        Check("blank failure reason is refused",
            Throws<ArgumentException>(
                () => blankReason.RecordFailedAttempt(Guid.NewGuid(), "driver", At, "   "),
                "reason is required"));
    }

    // g. A5: money changes hands at pickup, before the parcels leave
    private static void CheckPaymentBeforeCollection()
    {
        const decimal Due = 42.50m;
        (DeliveryRequest request, Delivery delivery, _) = NewDelivery("P1");
        delivery.RecordAllocation(new DeliveryStaffProfile("S-1", "Ana"), Guid.NewGuid(), "coord", At);

        Check("a parcel cannot be ticked before the customer pays",
            Throws<InvalidOperationException>(
                () => delivery.RecordParcelCollected(Guid.NewGuid(), "driver", At, request.Parcels[0], Due),
                "Take payment before ticking the parcels"));
        Check("a refused tick leaves the parcel unticked and the status Scheduled",
            !request.Parcels[0].IsCollected && delivery.Status == Status.Scheduled);

        Payment declined = delivery.RecordPaymentAttempt(
            Guid.NewGuid(), "driver", At, new CardPayment(new PaymentGateway(), "0000123456789012"), Due);
        Check("a declined card does not unlock collection",
            declined.Outcome == PaymentOutcome.Declined
            && Throws<InvalidOperationException>(
                () => delivery.RecordParcelCollected(Guid.NewGuid(), "driver", At, request.Parcels[0], Due),
                "Take payment before ticking the parcels"));

        delivery.RecordPaymentAttempt(Guid.NewGuid(), "driver", At, new CashPayment(), Due);
        delivery.RecordParcelCollected(Guid.NewGuid(), "driver", At, request.Parcels[0], Due);
        Check("cash success unlocks collection", delivery.Status == Status.Collected);
        Check("handover after payment moves status to Delivered", HandedOver(delivery, Due) && delivery.Status == Status.Delivered);

        (DeliveryRequest unpaidRequest, Delivery unpaid, _) = NewDelivery("P1");
        unpaid.RecordAllocation(new DeliveryStaffProfile("S-2", "Bo"), Guid.NewGuid(), "coord", At);
        unpaid.RecordParcelCollected(Guid.NewGuid(), "driver", At, unpaidRequest.Parcels[0]);
        Check("handover of an unpaid delivery is refused",
            Throws<InvalidOperationException>(
                () => unpaid.RecordSuccessfulHandover(Guid.NewGuid(), "driver", At, "Customer", Due),
                "Payment must be collected before the parcels are handed over"));
        Check("a refused handover leaves the delivery Collected", unpaid.Status == Status.Collected);

        (DeliveryRequest freeRequest, Delivery free, _) = NewDelivery("P1");
        free.RecordAllocation(new DeliveryStaffProfile("S-3", "Cy"), Guid.NewGuid(), "coord", At);
        free.RecordParcelCollected(Guid.NewGuid(), "driver", At, freeRequest.Parcels[0], 0m);
        Check("a zero-amount job needs no payment and is not stranded",
            HandedOver(free, 0m) && free.Status == Status.Delivered);
    }

    // h. availability window: an always-on profile (00:00-23:59) also covers a span that crosses midnight
    private static void CheckAlwaysOnShift()
    {
        var alwaysOn = new DeliveryStaffProfile("S-9", "Raj", TimeOnly.MinValue, TimeOnly.MaxValue);
        var dayShift = new DeliveryStaffProfile("S-8", "Ben", new TimeOnly(7, 0), new TimeOnly(19, 0));
        DateTime lateStart = new(2026, 3, 11, 21, 0, 0);      // pick-up opens 21:00
        DateTime lateEnd = new(2026, 3, 12, 1, 0, 0);         // delivery closes 01:00 next day

        Check("an always-on profile covers a window that crosses midnight",
            alwaysOn.WorksDuring(lateStart, lateEnd));
        Check("an always-on profile reports itself as always on, a day shift does not",
            alwaysOn.IsAlwaysOn && !dayShift.IsAlwaysOn);
        Check("a day shift does not cover a window that crosses midnight",
            !dayShift.WorksDuring(lateStart, lateEnd));
        Check("an always-on profile still refuses an empty window",
            !alwaysOn.WorksDuring(lateEnd, lateStart));
        Check("an always-on profile refuses a window longer than a day",
            !alwaysOn.WorksDuring(lateStart, lateStart.AddDays(1).AddMinutes(1)));
        Check("a day shift still covers its own hours", dayShift.WorksDuring(
            new DateTime(2026, 3, 11, 9, 0, 0), new DateTime(2026, 3, 11, 18, 0, 0)));
    }

    private static bool HandedOver(Delivery delivery, decimal amountDue)
    {
        try
        {
            delivery.RecordSuccessfulHandover(Guid.NewGuid(), "driver", At, "Customer", amountDue);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static (DeliveryRequest Request, Delivery Delivery, CountingObserver Observer) NewDelivery(
        params string[] parcelLabels)
    {
        DeliveryRequest request = new("REQ-TEST");
        foreach (string label in parcelLabels)
        {
            request.Parcels.Add(new Parcel(label, "Small"));
        }

        Delivery delivery = new(request, new StatusLifecycle());
        CountingObserver observer = new();
        delivery.AttachObserver(observer);
        return (request, delivery, observer);
    }

    private static bool Refused(StatusLifecycle lifecycle, Status from, Status to) =>
        !lifecycle.CanTransition(from, to)
        && lifecycle.ExplainRefusal(from, to).Contains("Illegal:", StringComparison.Ordinal);

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

    private sealed class CountingObserver : StatusObserver
    {
        public int Count { get; private set; }

        public void OnStatusChanged(Delivery delivery, Status newStatus) => Count++;
    }
}
