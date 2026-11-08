namespace GodrejWMS.Domain.Entities;

public static class VirtualLocationStatus
{
    public const string Free = "FREE";
    public const string InUse = "IN_USE";
}

/// <summary>Per-warehouse counter behind VL-&lt;WH&gt;-nnnnnn codes. Read with a row lock when a new virtual location is created.</summary>
public class VirtualLocationSequence
{
    public int WarehouseId { get; set; }

    public int LastValue { get; set; }
}

/// <summary>History of which inward order held a virtual location and for how long. The location itself carries no order link,
/// so it can be reused cleanly once released.</summary>
public class VirtualLocationLog
{
    public int Id { get; set; }

    public int PalletPositionId { get; set; }

    public PalletPosition PalletPosition { get; set; } = null!;

    public int? InwardTransactionId { get; set; }

    public int? InwardPutawayId { get; set; }

    public DateTimeOffset AssignedAt { get; set; }

    public DateTimeOffset? ReleasedAt { get; set; }
}
