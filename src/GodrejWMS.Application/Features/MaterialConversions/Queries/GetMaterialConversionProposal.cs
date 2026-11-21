using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.MaterialConversions.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.MaterialConversions.Queries;

/// <summary>Proposes which stock of the old code to convert for <paramref name="Boxes"/> boxes (null = all of it): FIFO - the oldest PKM first, then the
/// normal pick order of locations (the same engine a pull-out uses, so stock already promised to open picks is left out). Nothing is changed.</summary>
public sealed record GetMaterialConversionProposalQuery(int FromMaterialId, int ToMaterialId, int WarehouseId, decimal? Boxes = null) : IRequest<ConversionProposalDto>;

public sealed class GetMaterialConversionProposalHandler(
    IApplicationDbContext db, IPulloutAllocationService allocator, IPermissionService permissions, IWarehouseAccessService warehouseAccess)
    : IRequestHandler<GetMaterialConversionProposalQuery, ConversionProposalDto>
{
    public async Task<ConversionProposalDto> Handle(GetMaterialConversionProposalQuery request, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Inventory.ConvertMaterial, cancellationToken)
            && !await permissions.HasPermissionAsync(PermissionKeys.Inventory.ApproveMaterialConversion, cancellationToken))
        {
            throw new UnauthorizedAccessException("You are not permitted to convert material codes.");
        }

        if (!await warehouseAccess.HasAccessAsync(request.WarehouseId, cancellationToken))
        {
            throw new UnauthorizedAccessException("You do not have access to this warehouse.");
        }

        if (request.Boxes is <= 0)
        {
            throw new InvalidOperationException("Enter the number of boxes to convert.");
        }

        var (from, to) = await MaterialConversionRules.LoadMaterialsAsync(db, request.FromMaterialId, request.ToMaterialId, cancellationToken);
        var batches = await MaterialConversionRules.ConvertibleBatches(db, from.Id, request.WarehouseId)
            .AsNoTracking()
            .Select(b => new { b.Id, b.PalletPositionId, b.PalletPosition.LocationCode, b.MfgMonth, b.StockSubtypeId, b.QuantityBoxes })
            .ToListAsync(cancellationToken);
        var available = batches.Sum(b => b.QuantityBoxes);
        if (available <= 0)
        {
            throw new InvalidOperationException($"There is no good stock of {from.MaterialNumber} in this warehouse to convert.");
        }

        var wanted = Math.Min(request.Boxes ?? available, available);
        var allocation = await allocator.AllocateAsync(from.Id, wanted, request.WarehouseId, cancellationToken, preview: true);
        var lines = new List<ConversionProposalLineDto>();
        foreach (var pick in allocation.Picks)
        {
            var batch = batches.FirstOrDefault(b => b.LocationCode == pick.LocationCode && b.MfgMonth == pick.MfgMonth);
            if (batch is null)
            {
                continue;
            }

            lines.Add(new ConversionProposalLineDto(
                batch.Id, batch.PalletPositionId, batch.LocationCode, batch.MfgMonth, MfgMonthParser.Format(batch.MfgMonth), batch.StockSubtypeId,
                batch.QuantityBoxes, pick.QuantityBoxes));
        }

        var warnings = await MaterialConversionRules.BuildWarningsAsync(
            db, from, to, request.WarehouseId,
            lines.Select(l => (l.PalletPositionId, l.LocationCode, l.QuantityBoxes, l.AvailableBoxes)).ToList(), cancellationToken);
        if (request.Boxes is { } asked && asked > available)
        {
            warnings.Insert(0, $"Only {available:0.###} box(es) of good stock are available; that is what is proposed.");
        }
        else if (allocation.PickedQuantityBoxes < wanted)
        {
            warnings.Insert(0, $"Only {allocation.PickedQuantityBoxes:0.###} box(es) can be proposed - the rest is already promised to open pick-lists.");
        }

        return new ConversionProposalDto(
            from.Id, from.MaterialNumber, from.Description, to.Id, to.MaterialNumber, to.Description,
            available, lines.Sum(l => l.QuantityBoxes), lines, warnings);
    }
}
