using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Locations.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Locations.Commands;

public sealed record ImportLocationsFromExcelCommand(Stream FileStream) : IRequest<ImportLocationsResult>;

public sealed class ImportLocationsFromExcelValidator : AbstractValidator<ImportLocationsFromExcelCommand>
{
    public ImportLocationsFromExcelValidator()
    {
        RuleFor(x => x.FileStream).NotNull();
    }
}

public sealed class ImportLocationsFromExcelHandler(IExcelService excel, IApplicationDbContext db)
    : IRequestHandler<ImportLocationsFromExcelCommand, ImportLocationsResult>
{
    public async Task<ImportLocationsResult> Handle(ImportLocationsFromExcelCommand request, CancellationToken cancellationToken)
    {
        var rows = excel.ReadLocationMaster(request.FileStream);
        var errors = new List<string>();
        var updated = 0;

        var subtypesByCode = await db.LocationSubtypes
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Code, s => s.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var zoneTypesByCode = await db.ZoneTypes
            .AsNoTracking()
            .ToDictionaryAsync(z => z.Code, z => z.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var locationTypesByCode = await db.LocationTypes
            .AsNoTracking()
            .ToDictionaryAsync(l => l.Code, l => l.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var row in rows)
        {
            var locationCode = row.LocationCode.Trim();
            var position = await db.PalletPositions
                .FirstOrDefaultAsync(p => p.LocationCode == locationCode, cancellationToken);

            if (position is null)
            {
                errors.Add($"Row {row.RowNumber}: Location Code '{locationCode}' was not found.");
                continue;
            }

            if (row.MaxPallets <= 0)
            {
                errors.Add($"Row {row.RowNumber}: Max Pallets must be greater than zero.");
                continue;
            }

            if (!subtypesByCode.TryGetValue(row.LocationSubtypeCode.Trim(), out var locationSubtypeId))
            {
                errors.Add($"Row {row.RowNumber}: Location Subtype '{row.LocationSubtypeCode}' was not recognized.");
                continue;
            }

            if (!zoneTypesByCode.TryGetValue(row.ZoneTypeCode.Trim(), out var zoneTypeId))
            {
                errors.Add($"Row {row.RowNumber}: Zone Type '{row.ZoneTypeCode}' was not recognized.");
                continue;
            }

            if (!locationTypesByCode.TryGetValue(row.LocationTypeCode.Trim(), out var locationTypeId))
            {
                errors.Add($"Row {row.RowNumber}: Location Type '{row.LocationTypeCode}' was not recognized.");
                continue;
            }

            position.FlatLabel = string.IsNullOrWhiteSpace(row.FlatLabel) ? null : row.FlatLabel.Trim();
            position.LocationTypeId = locationTypeId;
            position.LocationSubtypeId = locationSubtypeId;
            position.ZoneTypeId = zoneTypeId;
            position.DistancePriority = Math.Clamp(row.DistancePriority, 1, 9999);
            position.MaxPallets = row.MaxPallets;
            position.IsActive = row.IsActive;
            updated++;
        }

        if (updated > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new ImportLocationsResult(updated, errors);
    }
}
