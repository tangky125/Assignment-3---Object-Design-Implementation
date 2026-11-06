namespace Dms.Domain;

/// <summary>Area 1 checks: customers, request logging rules, references and access levels.</summary>
public static class RequestingSelfTest
{
    private static readonly DateTime Now = new(2026, 11, 6, 9, 0, 0);

    /// <summary>Runs every check and prints PASS/FAIL per line.</summary>
    public static void Run()
    {
        CheckDirectory();
        CheckLogging();
        CheckParcels();
        CheckAccess();
    }

    private static void CheckDirectory()
    {
        var directory = Directory();
        Check("a customer is found by phone, ignoring spaces", directory.Search("0412345678", null).Count == 1);
        Check("a customer is found by part of the name", directory.Search(null, "citizen").Single().CustomerId == "C001");
        Check("a close spelling finds the customer (Citizn)", directory.Search(null, "Citizn").Count == 1);
        Check("an unrelated name finds nobody", directory.Search(null, "Zhang").Count == 0);

        Outcome<Customer> again = directory.Register("Janet Citizen", "0412 345 678", null);
        Check("a second customer with the same phone is refused", !again.Succeeded && again.Message.Contains("already held"));

        Outcome<Customer> added = directory.Register("Mia Wong", "0433 222 111", "mia@example.com");
        Check("a new customer gets the next id", added.Succeeded && added.Value!.CustomerId == "C003" && directory.NextNumber == 4);
        Check("a customer needs a name and a 10-digit phone", !directory.Register("", "0400", null).Succeeded);
    }

    private static void CheckLogging()
    {
        var register = new RequestRegister(Directory(), 10);
        Outcome<DeliveryRequest> logged = register.Log(Form(), Now, "Jane Citizen");
        Check("a valid request is logged as DMS-2026-0010", logged.Succeeded && logged.Value!.Reference == "DMS-2026-0010");
        Check("a new request is born Requested", logged.Value!.Status == Status.Requested);
        Check("the request records who logged it and when", logged.Value.LoggedBy == "Jane Citizen" && logged.Value.LoggedAt == Now);
        Check("parcels are labelled P1, P2 in order", logged.Value.Parcels.Select(p => p.Label).SequenceEqual(new[] { "P1", "P2" }));
        Check("references follow on without gaps", register.Log(Form(), Now, "Jane Citizen").Value!.Reference == "DMS-2026-0011");

        Refused(register, Form() with { Pickup = new Address("1 Main St", "Geelong", "3220") }, "outside our Greater Melbourne", "a pickup outside postcodes 3000-3207 is refused");
        Refused(register, Form() with { Parcels = Array.Empty<ParcelEntry>() }, "at least one parcel", "a request without parcels is refused");
        Refused(register, Form() with { Parcels = Enumerable.Repeat(Small, 11).ToList() }, "at most 10", "more than 10 parcels is refused");
        Refused(register, Form() with { ReadyTime = Now.AddMinutes(-5) }, "already passed", "a ready time in the past is refused");
        Refused(register, Form() with { ReadyTime = Now.AddDays(15) }, "14 days", "a ready time more than 14 days ahead is refused");
        Refused(register, Form() with { CustomerId = "C999" }, "register the customer", "a request for an unknown customer is refused");
        Refused(register, Form() with { Notes = new string('x', 201) }, "200", "notes over 200 characters are refused");
        Check("a refused request does not use up a reference", register.NextNumber == 12);
    }

    private static void CheckParcels()
    {
        Check("a parcel size must be Small, Medium or Large", Throws(() => _ = new Parcel("P1", "Huge")));
        Check("a parcel description is at most 40 characters", Throws(() => _ = new Parcel("P1", "Small", Array.Empty<string>(), new string('d', 41))));
        var parcel = new Parcel("P1", "large", new[] { "Fragile", "fragile" }, "  Vase ");
        Check("size, handling and description are tidied", parcel.SizeBand == "Large" && parcel.Handling.Count == 1 && parcel.Description == "Vase");
        parcel.MarkCollected();
        Check("a parcel can be ticked as collected", parcel.IsCollected);
    }

    private static void CheckAccess()
    {
        DeliveryRequest request = new RequestRegister(Directory(), 1).Log(Form(), Now, "Jane Citizen").Value!;
        Check("a visitor with the reference sees the recipient view", AccessPolicy.LevelFor(request, false, false, null) == AccessLevel.Recipient);
        Check("the customer who placed it sees the customer view", AccessPolicy.LevelFor(request, true, false, "C001") == AccessLevel.Customer);
        Check("another customer sees nothing", AccessPolicy.LevelFor(request, true, false, "C002") == AccessLevel.None);
        Check("staff see everything", AccessPolicy.LevelFor(request, true, true, null) == AccessLevel.Staff);
        Check("an unknown reference shows nothing", AccessPolicy.LevelFor(null, true, true, null) == AccessLevel.None);
    }

    private static readonly ParcelEntry Small = new("Small", Array.Empty<string>(), "Documents");

    private static CustomerDirectory Directory() => new(new[]
    {
        new Customer("C001", "Jane Citizen", "0412 345 678", "jane@example.com"),
        new Customer("C002", "Acme Florist Pty Ltd", "03 9123 4567", null),
    }, 3);

    private static RequestForm Form() => new(
        "C001",
        new Address("10 Swanston St", "Melbourne", "3000"),
        new Contact("Jane Citizen", "0412 345 678"),
        Now.AddHours(2),
        new Address("8 High St", "Kew", "3101"),
        new Contact("Mia Wong", "0433 222 111"),
        new[] { new ParcelEntry("Large", new[] { "Fragile" }, null), Small },
        null);

    private static void Refused(RequestRegister register, RequestForm form, string expected, string name)
    {
        Outcome<DeliveryRequest> outcome = register.Log(form, Now, "Jane Citizen");
        Check(name, !outcome.Succeeded && outcome.Message.Contains(expected, StringComparison.Ordinal));
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static void Check(string name, bool passed) =>
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}");
}
