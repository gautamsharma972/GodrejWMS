namespace GodrejWMS.Domain.Enums;

/// <summary>
/// Outcome of trying to allocate an inward or pullout quantity against warehouse capacity.
/// </summary>
public enum AllocationStatus
{
    /// <summary>The full requested quantity was allocated.</summary>
    Fulfilled = 0,

    /// <summary>Only part of the requested quantity could be allocated (capacity/stock ran out).</summary>
    Partial = 1,

    /// <summary>None of the requested quantity could be allocated.</summary>
    Failed = 2
}
