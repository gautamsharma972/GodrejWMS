using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

/// <summary>
/// Configurable assignment of a calendar month (1-12) to a <see cref="Season"/>. Always
/// exactly 12 rows, one per month — seeded on first run and edited in place, never created or
/// deleted through the UI.
/// </summary>
public class SeasonMonthMap : AuditableEntity
{
    /// <summary>Calendar month, 1 (January) through 12 (December).</summary>
    public int Month { get; set; }

    public int SeasonId { get; set; }

    public Season Season { get; set; } = null!;
}
