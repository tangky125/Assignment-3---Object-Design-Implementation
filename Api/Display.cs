using System.Globalization;

namespace Dms.Api;

/// <summary>Formats values the Australian way in messages written by the server.</summary>
public static class Display
{
    /// <summary>Money, e.g. $1,027.50.</summary>
    public static string Money(decimal amount) => "$" + amount.ToString("#,##0.00", CultureInfo.InvariantCulture);

    /// <summary>A date and time, e.g. Mon 02/11/2026 09:30.</summary>
    public static string When(DateTime at) => at.ToString("ddd dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
}
