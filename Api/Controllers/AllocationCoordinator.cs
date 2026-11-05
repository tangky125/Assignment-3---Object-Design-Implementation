using Dms.Api.Contracts;
using Dms.Api.Demo;
using Dms.Domain;

namespace Dms.Api.Controllers;

/// <summary>Area 3: applies staff rules and records allocation on the shared delivery aggregate.</summary>
public sealed class AllocationCoordinator : IAllocationService
{
    private readonly DemoBackend _readModel;
    private readonly StaffRegister _staff;
    private readonly JsonDeliveryRegistry _registry;
    private readonly object _gate = new();

    public AllocationCoordinator(DemoBackend readModel, StaffRegister staff, JsonDeliveryRegistry registry)
    { _readModel = readModel; _staff = staff; _registry = registry; }

    public IReadOnlyList<RequestSummary> AwaitingAllocation() => _readModel.AwaitingAllocation();
    public IReadOnlyList<RequestSummary> ScheduledDeliveries() => _readModel.ScheduledDeliveries();
    public IReadOnlyList<StaffOption> StaffOptions(string reference) => _staff.AvailableFor(reference);

    public OperationResult Allocate(string reference, string staffId, UserSession by) => Execute(reference, staffId, by, null);
    public OperationResult Replace(string reference, string staffId, string reason, UserSession by) =>
        string.IsNullOrWhiteSpace(reason) ? OperationResult.Fail("A replacement reason is required.")
        : Execute(reference, staffId, by, reason);

    private OperationResult Execute(string reference, string staffId, UserSession by, string? reason)
    {
        lock (_gate) // one final eligibility check and commit per coordinator instance
        {
            if (!by.Has(UserRole.OfficeStaff) && !by.Has(UserRole.Owner))
                return OperationResult.Fail("Only office staff may allocate deliveries.");
            RequestDetail? detail = _readModel.Find(reference);
            if (detail is null) return OperationResult.Fail($"Unknown delivery {reference}.");
            if (reason is null && detail.Status is not (Status.Accepted or Status.DeliveryFailed))
                return OperationResult.Fail("Only accepted or failed deliveries can be allocated.");
            if (reason is not null && detail.Status != Status.Scheduled)
                return OperationResult.Fail("Only scheduled deliveries can be reallocated.");
            StaffOption? choice = _staff.AvailableFor(reference).FirstOrDefault(x => x.StaffId == staffId);
            if (choice is null || !choice.Eligible)
                return OperationResult.Fail(choice?.Reason ?? "Unknown or inactive delivery staff member.");
            if (_registry.Find(reference) is null)
                return OperationResult.Fail("No accepted delivery aggregate exists.");
            OperationResult changed = reason is null ? _readModel.Allocate(reference, staffId, by)
                : _readModel.Replace(reference, staffId, reason, by);
            if (!changed.Succeeded) return changed;
            _registry.SaveAllocation(reference, staffId, choice.Name, by.Username, reason);
            return changed;
        }
    }
}
