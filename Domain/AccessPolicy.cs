namespace Dms.Domain;

/// <summary>How much of a request a viewer may see (A2 AccessPolicy detail levels).</summary>
public enum AccessLevel
{
    /// <summary>May not see the request at all.</summary>
    None,

    /// <summary>Holds the reference only: status and expected window, no price or customer detail.</summary>
    Recipient,

    /// <summary>The customer who placed it: everything except internal notes and staff detail.</summary>
    Customer,

    /// <summary>Office staff, delivery staff and owners: full detail.</summary>
    Staff,
}

/// <summary>
/// Decides how much of a request a viewer may see (A2 CRC: AccessPolicy). A recipient who only
/// holds the reference sees status and timing; the customer who placed the request sees their own
/// request; staff see everything; another signed-in customer sees nothing (DA7).
/// </summary>
public static class AccessPolicy
{
    /// <summary>The level for a viewer looking at a request.</summary>
    /// <param name="request">The request, or null if the reference is unknown.</param>
    /// <param name="signedIn">False for an anonymous visitor using a reference.</param>
    /// <param name="isStaff">True for office staff, delivery staff and owners.</param>
    /// <param name="customerId">The signed-in customer's id, if any.</param>
    public static AccessLevel LevelFor(DeliveryRequest? request, bool signedIn, bool isStaff, string? customerId)
    {
        if (request is null)
        {
            return AccessLevel.None;
        }

        if (!signedIn)
        {
            return AccessLevel.Recipient;
        }

        if (isStaff)
        {
            return AccessLevel.Staff;
        }

        return request.BelongsTo(customerId) ? AccessLevel.Customer : AccessLevel.None;
    }
}
