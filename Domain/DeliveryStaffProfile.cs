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

    /// <summary>
    /// True for an account on duty 00:00-23:59. Such an account is available at any time on any day
    /// and is never treated as double-booked, which is what a testing account (e.g. raj) needs.
    /// </summary>
    public bool IsAlwaysOn => ShiftStart == TimeOnly.MinValue && ShiftEnd == TimeOnly.MaxValue;

    /// <summary>
    /// True when the whole pick-up..delivery span fits inside one shift. A profile on duty from
    /// 00:00 to 23:59 (an always-on account, e.g. for testing) covers any span up to a day long,
    /// even when it crosses midnight; every other profile must fit the span inside one calendar day.
    /// </summary>
    /// <param name="start">When the span starts (the pick-up window opens).</param>
    /// <param name="end">When the span ends (the delivery window closes).</param>
    public bool WorksDuring(DateTime start, DateTime end)
    {
        if (!Active || end <= start)
        {
            return false;
        }

        if (IsAlwaysOn)
        {
            return end - start <= TimeSpan.FromDays(1);
        }

        return start.Date == end.Date
            && TimeOnly.FromDateTime(start) >= ShiftStart
            && TimeOnly.FromDateTime(end) <= ShiftEnd;
    }
}
