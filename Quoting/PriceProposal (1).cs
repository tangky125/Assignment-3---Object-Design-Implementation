namespace Dms.Domain;

/// <summary>A price proposed by a pricing policy, with how it was worked out.</summary>
/// <param name="Price">The proposed price, or null when an owner must set it.</param>
/// <param name="NeedsOwnerApproval">True when the quote must wait for an owner (A1 Task 2 variant 2a).</param>
/// <param name="Breakdown">One line per rule applied, for office staff to check.</param>
public sealed record PriceProposal(decimal? Price, bool NeedsOwnerApproval, IReadOnlyList<string> Breakdown);
