using System.Globalization;
using System.Text.RegularExpressions;

namespace Dms.Api.Validation;

/// <summary>
/// Format checks for everything the user types. Each rule returns null when the value is
/// acceptable, or a plain, actionable message saying what to type instead (A1 design-level
/// requirement "Actionable Error Handling"). Business rules, such as whether a postcode is in
/// the service area, stay in the business layer.
/// </summary>
public static partial class InputRules
{
    /// <summary>Date/time format shown to users.</summary>
    public const string DateTimeHint = "dd/mm/yyyy hh:mm, or 'today 14:30' / 'tomorrow 9:00'";

    private static readonly string[] DateTimeFormats =
    {
        "d/M/yyyy H:mm", "dd/MM/yyyy HH:mm", "d/M/yyyy HH:mm", "dd/MM/yyyy H:mm", "d/M/yy H:mm",
    };

    private static readonly string[] TimeFormats = { "H:mm", "HH:mm" };

    /// <summary>A name for a person or business: 2-60 characters with at least one letter.</summary>
    public static string? Name(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Name cannot be blank.";
        }

        if (value.Length < 2 || value.Length > 60)
        {
            return "Name must be 2 to 60 characters long.";
        }

        if (!NamePattern().IsMatch(value) || !value.Any(char.IsLetter))
        {
            return "Name can contain letters, numbers, spaces and . , ' & - only.";
        }

        return null;
    }

    /// <summary>An Australian mobile or landline number, e.g. 0412 345 678 or 03 9123 4567.</summary>
    public static string? AustralianPhone(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Phone number cannot be blank.";
        }

        return NormalisePhone(value) is null
            ? "Enter an Australian phone number with 10 digits, e.g. 0412 345 678 or 03 9123 4567."
            : null;
    }

    /// <summary>Returns the phone number in standard spacing (0412 345 678 / 03 9123 4567), or null if invalid.</summary>
    public static string? NormalisePhone(string value)
    {
        string digits = new(value.Where(char.IsDigit).ToArray());
        if (value.TrimStart().StartsWith("+61", StringComparison.Ordinal) && digits.Length == 11)
        {
            digits = "0" + digits[2..];
        }

        if (digits.Length != 10)
        {
            return null;
        }

        if (digits.StartsWith("04", StringComparison.Ordinal))
        {
            return $"{digits[..4]} {digits[4..7]} {digits[7..]}";
        }

        if (digits[0] == '0' && "2378".Contains(digits[1], StringComparison.Ordinal))
        {
            return $"{digits[..2]} {digits[2..6]} {digits[6..]}";
        }

        return null;
    }

    /// <summary>An optional email address.</summary>
    public static string? OptionalEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return EmailPattern().IsMatch(value) ? null : "Enter a valid email address, e.g. jane@example.com, or leave it blank.";
    }

    /// <summary>A street line: 3-80 characters including a letter.</summary>
    public static string? Street(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Street address cannot be blank.";
        }

        if (value.Length < 3 || value.Length > 80 || !value.Any(char.IsLetter))
        {
            return "Enter a street address of 3 to 80 characters, e.g. 10 Swanston St.";
        }

        return null;
    }

    /// <summary>A suburb name: letters, spaces, apostrophes and hyphens.</summary>
    public static string? Suburb(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Suburb cannot be blank.";
        }

        return SuburbPattern().IsMatch(value) ? null : "Suburb must be 2 to 40 letters, e.g. Melbourne or Box Hill.";
    }

    /// <summary>An Australian postcode: exactly four digits.</summary>
    public static string? Postcode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Postcode cannot be blank.";
        }

        return PostcodePattern().IsMatch(value) ? null : "Postcode must be exactly 4 digits, e.g. 3000.";
    }

    /// <summary>A reason or explanation: 3-200 characters.</summary>
    public static string? Reason(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Please give a reason; it cannot be blank.";
        }

        return value.Length is < 3 or > 200 ? "Reason must be 3 to 200 characters." : null;
    }

    /// <summary>Optional free text of at most <paramref name="maxLength"/> characters.</summary>
    public static Func<string, string?> OptionalText(int maxLength) =>
        value => value.Length > maxLength ? $"Keep this to {maxLength} characters or fewer." : null;

    /// <summary>A request reference such as DMS-2026-0001.</summary>
    public static string? Reference(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Reference cannot be blank.";
        }

        return ReferencePattern().IsMatch(value) ? null : "A reference looks like DMS-2026-0001.";
    }

    /// <summary>A sign-in name.</summary>
    public static string? Username(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Username cannot be blank." : null;

    /// <summary>Parses money such as 27.5, $27.50 or 1,000.00; returns null when it is not a number.</summary>
    public static decimal? ParseMoney(string value)
    {
        string cleaned = value.Replace("$", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        return decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal amount)
            ? decimal.Round(amount, 2)
            : null;
    }

    /// <summary>Parses a date/time typed as dd/mm/yyyy hh:mm, 'today hh:mm' or 'tomorrow hh:mm'.</summary>
    public static DateTime? ParseDateTime(string value, DateTime now)
    {
        string text = value.Trim();
        foreach ((string word, int days) in new[] { ("today", 0), ("tomorrow", 1) })
        {
            if (text.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                string timePart = text[word.Length..].Trim();
                if (TimeOnly.TryParseExact(timePart, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time))
                {
                    return now.Date.AddDays(days).Add(time.ToTimeSpan());
                }

                return null;
            }
        }

        return DateTime.TryParseExact(text, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
            ? parsed
            : null;
    }

    /// <summary>Upper-cases a reference so dms-2026-0001 is accepted.</summary>
    public static string NormaliseReference(string value) => value.Trim().ToUpperInvariant();

    [GeneratedRegex(@"^[A-Za-z0-9 .,'&\-]+$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z '\-]{1,39}$")]
    private static partial Regex SuburbPattern();

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex PostcodePattern();

    [GeneratedRegex(@"^DMS-\d{4}-\d{4}$", RegexOptions.IgnoreCase)]
    private static partial Regex ReferencePattern();
}
