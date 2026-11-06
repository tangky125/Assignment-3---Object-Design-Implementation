namespace Dms.Domain;

/// <summary>
/// A person or business that sends parcels (A2 CRC: Customer). Knows its own contact details and
/// whether a search by phone or name refers to it.
/// </summary>
public sealed class Customer
{
    /// <summary>Creates a customer; the id is issued by <see cref="CustomerDirectory"/>.</summary>
    public Customer(string customerId, string name, string phone, string? email)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new ArgumentException("A customer needs an id.", nameof(customerId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A customer needs a name.", nameof(name));
        }

        if (Digits(phone).Length != 10)
        {
            throw new ArgumentException("A customer needs a 10-digit Australian phone number.", nameof(phone));
        }

        CustomerId = customerId;
        Name = name.Trim();
        Phone = phone.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }

    /// <summary>Directory id, e.g. C004.</summary>
    public string CustomerId { get; }

    /// <summary>Person or business name.</summary>
    public string Name { get; }

    /// <summary>Contact phone number.</summary>
    public string Phone { get; }

    /// <summary>Optional email address.</summary>
    public string? Email { get; }

    /// <summary>True when the phone number is this customer's, ignoring spaces and punctuation.</summary>
    public bool HasPhone(string phone) => Digits(phone).Length > 0 && Digits(phone) == Digits(Phone);

    /// <summary>
    /// True when the query is part of the name, or a close spelling of one word of it
    /// (A1 Task 1 variant 1a: office staff search while the customer is on the phone).
    /// </summary>
    public bool MatchesName(string query)
    {
        string q = query.Trim().ToUpperInvariant();
        if (q.Length == 0)
        {
            return false;
        }

        string n = Name.ToUpperInvariant();
        if (n.Contains(q, StringComparison.Ordinal))
        {
            return true;
        }

        return n.Split(' ', StringSplitOptions.RemoveEmptyEntries).Append(n)
            .Any(word => 1.0 - ((double)Distance(word, q) / Math.Max(word.Length, q.Length)) >= 0.75);
    }

    internal static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private static int Distance(string a, string b)
    {
        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (int j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }

        return d[a.Length, b.Length];
    }
}
