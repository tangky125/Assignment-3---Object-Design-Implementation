using System.Security.Cryptography;
using System.Text;
using Dms.Api.Contracts;
using Dms.Api.Demo;
using Dms.Domain;

namespace Dms.Api.Controllers;

/// <summary>One shared domain registry, backed by the persistent application read model.</summary>
public sealed class JsonDeliveryRegistry : IDeliveryRegistry
{
    private readonly DemoBackend _backend;
    private readonly DemoQuoteRegistry _quotes;
    private readonly StatusLifecycle _lifecycle;
    private readonly Dictionary<string, DeliveryContext> _contexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _lastSynced = new(StringComparer.OrdinalIgnoreCase);

    public JsonDeliveryRegistry(DemoBackend backend, DemoQuoteRegistry quotes, StatusLifecycle lifecycle)
    { _backend = backend; _quotes = quotes; _lifecycle = lifecycle; }

    public DeliveryContext? Find(string reference)
    {
        lock (_gate)
        {
            if (_contexts.TryGetValue(reference, out DeliveryContext? context)) return context;
            RequestDetail? detail = _backend.Find(reference);
            if (detail is null || detail.Status is Status.Requested or Status.Quoted or Status.Rejected or Status.Cancelled)
                return null;

            DeliveryRequest request;
            Delivery delivery;
            decimal amount;
            if (_quotes.AcceptedDeliveries.TryGetValue(reference, out var accepted))
            {
                delivery = accepted.Delivery;
                request = delivery.Request;
                amount = accepted.AmountDue;
            }
            else
            {
                request = new DeliveryRequest(detail.Reference);
                foreach (ParcelView parcel in detail.Parcels)
                    request.Parcels.Add(new Parcel(parcel.ParcelId, parcel.Size.ToString()));
                delivery = new Delivery(request, _lifecycle);
                amount = detail.CurrentQuote?.Price ?? 0m;
            }

            foreach (ParcelView p in detail.Parcels.Where(x => x.Collected))
                request.Parcels.First(x => x.Label == p.ParcelId).MarkCollected();

            var profile = detail.AllocatedStaffId is null ? null
                : new DeliveryStaffProfile(detail.AllocatedStaffId, detail.AllocatedStaffName ?? detail.AllocatedStaffId);
            var payments = detail.Payments.Select((v, index) => new Delivery.PaymentViewSnapshot(
                StableId(reference, "payment", index), new DateTimeOffset(v.At), v.Method,
                v.Amount, v.Successful, v.Outcome)).ToList();
            var history = new List<DeliveryEvent>();
            var saved = _backend.DomainSnapshot(reference);
            delivery.RestoreState(detail.Status, profile,
                saved.Payments.Count > 0 ? saved.Payments : payments,
                saved.History.Count > 0 ? saved.History : history);
            context = new DeliveryContext(delivery, request, amount);
            _contexts[reference] = context;
            _lastSynced[reference] = Fingerprint(context.Delivery);
            return context;
        }
    }

    private static Guid StableId(string reference, string kind, int index) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"{reference}|{kind}|{index}")).AsSpan(0, 16));

    private static string Fingerprint(Delivery delivery) => string.Join("|",
        delivery.Status, delivery.AllocatedProfile?.StaffId ?? "",
        delivery.History.Count, delivery.Payments.Count,
        string.Join(",", delivery.Request.Parcels.Select(p => p.IsCollected ? "1" : "0")));

    public IReadOnlyList<RequestSummary> JobsFor(string staffId) => _backend.JobsFor(staffId);
    public RequestDetail? Detail(string reference) => _backend.Find(reference);

    public void SaveAllocation(string reference, string staffId, string staffName, string actor, string? reason)
    {
        lock (_gate)
        {
            DeliveryContext context = Find(reference) ?? throw new InvalidOperationException("No accepted delivery found.");
            var profile = new DeliveryStaffProfile(staffId, staffName);
            if (reason is null && context.Delivery.Status is (Status.Accepted or Status.DeliveryFailed))
                context.Delivery.RecordAllocation(profile, Guid.NewGuid(), actor, DateTimeOffset.Now);
            else if (reason is not null && context.Delivery.Status == Status.Scheduled)
                context.Delivery.ReplaceAllocation(profile, Guid.NewGuid(), actor, DateTimeOffset.Now, reason);
            else throw new InvalidOperationException($"Cannot allocate a delivery with status {context.Delivery.Status}.");
            _backend.SyncDelivery(reference, context.Delivery);
            _lastSynced[reference] = Fingerprint(context.Delivery);
        }
    }

    /// <summary>Synchronize mutations made by Area 4 back to persistent read models.</summary>
    public void SyncAll()
    {
        lock (_gate)
            foreach (KeyValuePair<string, DeliveryContext> item in _contexts)
            {
                string current = Fingerprint(item.Value.Delivery);
                if (_lastSynced.TryGetValue(item.Key, out string? previous) && current == previous) continue;
                _backend.SyncDelivery(item.Key, item.Value.Delivery);
                _lastSynced[item.Key] = current;
            }
    }
}
