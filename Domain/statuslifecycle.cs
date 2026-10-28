namespace Dms.Domain;

public class StatusLifecycle
{
    private static readonly Dictionary<Status, HashSet<Status>> Legal = new()
    {
        [Status.Requested]      = new HashSet<Status> { Status.Quoted },
        [Status.Quoted]         = new HashSet<Status> { Status.Accepted, Status.Rejected, Status.Requested },
        [Status.Accepted]       = new HashSet<Status> { Status.Scheduled, Status.Cancelled },
        [Status.Scheduled]      = new HashSet<Status> { Status.Collected, Status.DeliveryFailed },
        [Status.Collected]      = new HashSet<Status> { Status.Delivered, Status.DeliveryFailed, Status.Cancelled },
        [Status.DeliveryFailed] = new HashSet<Status> { Status.Scheduled },
        [Status.Delivered]      = new HashSet<Status>(),  // final
        [Status.Cancelled]      = new HashSet<Status>(),  // final
        [Status.Rejected]       = new HashSet<Status>()   // final
    };

    public bool CanTransition(Status from, Status to)
        => Legal.TryGetValue(from, out var moves) && moves.Contains(to);

    public bool IsFinal(Status s)
        => Legal[s].Count == 0;

    public string ExplainRefusal(Status from, Status to)
        => $"Illegal: {from} -> {to}. Legal moves from {from}: {string.Join(", ", Legal[from])}";
}