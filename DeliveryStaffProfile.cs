namespace Dms.Domain;

/// <summary>A delivery-capable employee's stable identity and availability information.</summary>
public sealed class DeliveryStaffProfile
{
    public DeliveryStaffProfile(string staffId, string staffName)
        : this(staffId, staffName, TimeOnly.MinValue, TimeOnly.MaxValue) { }

    public DeliveryStaffProfile(string staffId, string staffName, TimeOnly shiftStart, TimeOnly shiftEnd,
        bool active = true)
    {
        if (string.IsNullOrWhiteSpace(staffId)) throw new ArgumentException("A staff ID is required.", nameof(staffId));
        if (string.IsNullOrWhiteSpace(staffName)) throw new ArgumentException("A staff name is required.", nameof(staffName));
        if (shiftStart >= shiftEnd) throw new ArgumentException("Shift end must be later than shift start.");
        StaffId = staffId;
        StaffName = staffName;
        ShiftStart = shiftStart;
        ShiftEnd = shiftEnd;
        Active = active;
    }

    public string StaffId { get; }
    public string StaffName { get; }
    public TimeOnly ShiftStart { get; }
    public TimeOnly ShiftEnd { get; }
    public bool Active { get; }

    public bool WorksDuring(DateTime start, DateTime end) =>
        Active && end > start && start.Date == end.Date &&
        TimeOnly.FromDateTime(start) >= ShiftStart && TimeOnly.FromDateTime(end) <= ShiftEnd;
}
