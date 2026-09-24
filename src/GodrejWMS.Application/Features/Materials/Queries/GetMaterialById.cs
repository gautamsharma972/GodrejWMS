using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Materials.Dtos;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Materials.Queries;

public sealed record GetMaterialByIdQuery(int Id) : IRequest<MaterialDto>;

public sealed class GetMaterialByIdHandler(IApplicationDbContext db) : IRequestHandler<GetMaterialByIdQuery, MaterialDto>
{
    public async Task<MaterialDto> Handle(GetMaterialByIdQuery request, CancellationToken cancellationToken)
    {
        var m = await db.Materials.AsNoTracking().Include(x => x.MovementType).Include(x => x.Season).Include(x => x.PreferredZoneType).FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Material), request.Id);

        return new MaterialDto(
            m.Id, m.MaterialNumber, m.Description, m.DesignType, m.CharacteristicValue,
            m.PackSize, m.MrpPrice, m.LengthMm, m.WidthMm, m.HeightMm, m.VolumeMm3,
            m.NetWeightKg, m.GrossWeightKg, m.BoxWeightKg, m.PalletCapacityBoxes,
            m.PalletWeightKg, m.MovementTypeId, m.MovementType.Code, m.MovementType.DisplayName,
            m.SeasonId, m.Season.Code, m.Season.DisplayName,
            m.PreferredZoneTypeId, m.PreferredZoneType?.DisplayName, m.RequirePreferredZone, m.IsActive);
    }
}
