namespace Dms.Domain;

/// <summary>Result of a directory or register command: the new object, or a plain-language refusal.</summary>
public sealed record Outcome<T>(T? Value, string Message) where T : class
{
    /// <summary>True when the command succeeded.</summary>
    public bool Succeeded => Value is not null;

    internal static Outcome<T> Ok(T value, string message) => new(value, message);

    internal static Outcome<T> Refused(string message) => new(null, message);
}

/// <summary>
/// Owns the collection of customers (A2 CRC: CustomerDirectory): registers new customers, refuses a
/// second customer with the same phone number, and finds customers by phone, name or both.
/// </summary>
public sealed class CustomerDirectory
{
    private readonly List<Customer> _customers;

    /// <summary>Creates the directory from customers already held (restored at start-up, DA2).</summary>
    /// <param name="customers">Customers already registered.</param>
    /// <param name="nextNumber">Next customer number to issue.</param>
    public CustomerDirectory(IEnumerable<Customer> customers, int nextNumber)
    {
        _customers = customers.ToList();
        NextNumber = Math.Max(nextNumber, 1);
    }

    /// <summary>The next customer number this directory will issue.</summary>
    public int NextNumber { get; private set; }

    /// <summary>Every customer, in registration order.</summary>
    public IReadOnlyList<Customer> Customers => _customers;

    /// <summary>Finds a customer by id.</summary>
    public Customer? Get(string customerId) => _customers.FirstOrDefault(c => c.CustomerId == customerId);

    /// <summary>Customers whose phone is <paramref name="phone"/> or whose name matches <paramref name="name"/>.</summary>
    public IReadOnlyList<Customer> Search(string? phone, string? name) =>
        _customers
            .Where(c => (!string.IsNullOrWhiteSpace(phone) && c.HasPhone(phone)) ||
                        (!string.IsNullOrWhiteSpace(name) && c.MatchesName(name)))
            .ToList();

    /// <summary>Registers a new customer; refuses a phone number that is already held.</summary>
    public Outcome<Customer> Register(string name, string phone, string? email)
    {
        if (_customers.Any(c => c.HasPhone(phone)))
        {
            return Outcome<Customer>.Refused($"A customer with phone {phone} is already held. Search again and choose them.");
        }

        if (string.IsNullOrWhiteSpace(name) || Customer.Digits(phone).Length != 10)
        {
            return Outcome<Customer>.Refused("A new customer needs a name and a 10-digit phone number.");
        }

        var customer = new Customer($"C{NextNumber:000}", name, phone, email);
        NextNumber++;
        _customers.Add(customer);
        return Outcome<Customer>.Ok(customer, $"Customer {customer.Name} registered.");
    }
}
