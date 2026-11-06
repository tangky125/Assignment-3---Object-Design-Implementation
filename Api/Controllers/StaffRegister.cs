using Dms.Api.Contracts;
using Dms.Domain;

namespace Dms.Api.Controllers;

/// <summary>Area 3's real scheduling query service: shifts, active staff and overlapping jobs.</summary>
public sealed class StaffRegister
{
    private readonly Func<IReadOnlyList<DeliveryStaffProfile>> _roster;
    private readonly ITrackingService _tracking;

    public StaffRegister(Func<IReadOnlyList<DeliveryStaffProfile>> roster, ITrackingService tracking)
    { _roster = roster; _tracking = tracking; }

    public IReadOnlyList<StaffOption> AvailableFor(string reference)
    {
        RequestDetail? target = _tracking.Find(reference);
        if (target?.CurrentQuote is null) return Array.Empty<StaffOption>();
        DateTime start = target.CurrentQuote.PickupWindow.Start;
        DateTime end = target.CurrentQuote.DeliveryWindow.End;
        var activeJobs = _tracking.Search(null, null, null, null)
            .Where(j => j.Reference != reference && j.Status is (Status.Scheduled or Status.Collected))
            .Select(j => _tracking.Find(j.Reference))
            .Where(j => j is not null && j.AllocatedStaffId is not null && j.CurrentQuote is not null)
            .ToList();

        return _roster().Select(staff =>
        {
            if (!staff.Active) return new StaffOption(staff.StaffId, staff.StaffName, false, "Inactive staff member");
            if (target.AllocatedStaffId == staff.StaffId)
                return new StaffOption(staff.StaffId, staff.StaffName, false, "Already allocated to this delivery");
            if (!staff.WorksDuring(start, end))
                return new StaffOption(staff.StaffId, staff.StaffName, false,
                    $"Outside shift ({staff.ShiftStart:HH:mm}-{staff.ShiftEnd:HH:mm})");
            // An always-on account (00:00-23:59, e.g. for testing) is never treated as double-booked,
            // so any job can be given to it at any time on any day.
            if (staff.IsAlwaysOn)
                return new StaffOption(staff.StaffId, staff.StaffName, true, "Free for both windows");
            RequestDetail? clash = activeJobs.FirstOrDefault(j =>
                j!.AllocatedStaffId == staff.StaffId &&
                j.CurrentQuote!.PickupWindow.Start < end && start < j.CurrentQuote.DeliveryWindow.End);
            return clash is null
                ? new StaffOption(staff.StaffId, staff.StaffName, true, "Free for both windows")
                : new StaffOption(staff.StaffId, staff.StaffName, false, $"Busy with {clash.Reference}");
        }).ToList();
    }
}
