// STUB - compile-only stand-in. Owner: Maleesha. Delete when the real class lands.
namespace Dms.Domain;

/// <summary>Placeholder staff profile until Maleesha's class lands.</summary>
public class DeliveryStaffProfile
{
    /// <summary>Creates a profile from a staff id and display name.</summary>
    /// <param name="staffId">Staff identifier.</param>
    /// <param name="staffName">Display name.</param>
    public DeliveryStaffProfile(string staffId, string staffName)
    {
        StaffId = staffId;
        StaffName = staffName;
    }

    /// <summary>Staff identifier.</summary>
    public string StaffId { get; }

    /// <summary>Display name.</summary>
    public string StaffName { get; }
}
