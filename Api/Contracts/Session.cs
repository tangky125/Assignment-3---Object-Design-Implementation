namespace Dms.Api.Contracts;

/// <summary>The roles a signed-in user can hold. One person may hold several (A2 DA3: roles by composition).</summary>
public enum UserRole
{
    Customer,
    OfficeStaff,
    DeliveryStaff,
    Owner,
}

/// <summary>Who is using the system right now. Created by <see cref="IAuthService"/> at sign-in.</summary>
/// <param name="Username">The sign-in name.</param>
/// <param name="DisplayName">The name shown in screen headers.</param>
/// <param name="Roles">Every role the user holds.</param>
/// <param name="CustomerId">The customer record for a customer user; otherwise null.</param>
/// <param name="StaffId">The staff record for a staff user; otherwise null.</param>
public sealed record UserSession(
    string Username,
    string DisplayName,
    IReadOnlySet<UserRole> Roles,
    string? CustomerId,
    string? StaffId)
{
    /// <summary>Returns true when the user holds the given role.</summary>
    public bool Has(UserRole role) => Roles.Contains(role);

    /// <summary>Returns true when the user holds any staff role (office, delivery or owner).</summary>
    public bool IsStaff => Has(UserRole.OfficeStaff) || Has(UserRole.DeliveryStaff) || Has(UserRole.Owner);

    /// <summary>The roles as readable text, e.g. "Owner, Office Staff".</summary>
    public string RolesText => string.Join(", ", Roles.OrderBy(r => r).Select(RoleText));

    /// <summary>Readable name of a role.</summary>
    public static string RoleText(UserRole role) => role switch
    {
        UserRole.OfficeStaff => "Office Staff",
        UserRole.DeliveryStaff => "Delivery Staff",
        _ => role.ToString(),
    };
}

/// <summary>Outcome of a command sent to the business layer. Business rules report refusals here, not by crashing the UI.</summary>
/// <param name="Succeeded">True when the command was carried out.</param>
/// <param name="Message">A plain-language message to show the user.</param>
public record OperationResult(bool Succeeded, string Message)
{
    /// <summary>A successful result.</summary>
    public static OperationResult Ok(string message) => new(true, message);

    /// <summary>A refused or failed result.</summary>
    public static OperationResult Fail(string message) => new(false, message);

    /// <summary>A successful result carrying a value.</summary>
    public static OperationResult<T> Ok<T>(T value, string message) => new(true, message, value);

    /// <summary>A refused or failed result for a command that would have returned a value.</summary>
    public static OperationResult<T> Fail<T>(string message) => new(false, message, default);
}

/// <summary>Outcome of a command that also returns a value (e.g. the new request reference).</summary>
/// <typeparam name="T">The type of the returned value.</typeparam>
/// <param name="Succeeded">True when the command was carried out.</param>
/// <param name="Message">A plain-language message to show the user.</param>
/// <param name="Value">The value produced; null when the command failed.</param>
public sealed record OperationResult<T>(bool Succeeded, string Message, T? Value)
    : OperationResult(Succeeded, Message);
