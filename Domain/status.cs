namespace Dms.Domain;

public enum Status
{
    Requested, Quoted, Accepted, Scheduled, Collected,
    Delivered, DeliveryFailed, Cancelled, Rejected
}