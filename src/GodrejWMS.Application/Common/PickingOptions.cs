namespace GodrejWMS.Application.Common;

/// <summary>Picking behaviour, bound to the "Picking" configuration section.</summary>
public sealed class PickingOptions
{
    /// <summary>Among batches of the same PKM, pick from virtual (overflow) locations before physical ones, so virtual locations
    /// empty out and are reused sooner. FIFO by PKM is never broken. Default on.</summary>
    public bool PickFromVirtualFirst { get; set; } = true;
}
