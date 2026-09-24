using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Materials.Commands;

/// <summary>Bulk-imports/updates materials from a MaterialMaster-shaped Excel upload.</summary>
public sealed record ImportMaterialsFromExcelCommand(Stream FileStream) : IRequest<ImportMaterialsResult>;

public sealed record ImportMaterialsResult(int Created, int Updated, IReadOnlyList<string> Errors);

public sealed class ImportMaterialsFromExcelValidator : AbstractValidator<ImportMaterialsFromExcelCommand>
{
    public ImportMaterialsFromExcelValidator()
    {
        RuleFor(x => x.FileStream).NotNull();
    }
}

public sealed class ImportMaterialsFromExcelHandler(
    IApplicationDbContext db,
    IExcelService excel,
    IDateTimeProvider clock,
    ICurrentUserService currentUser) : IRequestHandler<ImportMaterialsFromExcelCommand, ImportMaterialsResult>
{
    public async Task<ImportMaterialsResult> Handle(ImportMaterialsFromExcelCommand request, CancellationToken cancellationToken)
    {
        var rows = excel.ReadMaterialMaster(request.FileStream);
        var errors = new List<string>();
        int created = 0, updated = 0;

        var existing = await db.Materials.ToDictionaryAsync(m => m.MaterialNumber, cancellationToken);
        var activeDesignTypeCodes = await db.DesignTypes
            .Where(d => d.IsActive)
            .Select(d => d.Code)
            .ToListAsync(cancellationToken);
        var activeDesignTypes = activeDesignTypeCodes.ToDictionary(c => c, StringComparer.OrdinalIgnoreCase);
        var movementTypeIdsByCode = await db.SkuMovementTypes
            .ToDictionaryAsync(s => s.Code, s => s.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var seasonIdsByCode = await db.Seasons
            .ToDictionaryAsync(s => s.Code, s => s.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var row in rows)
        {
            if (row.MaterialNumber <= 0)
            {
                errors.Add($"Row {row.RowNumber}: Material Number is required.");
                continue;
            }

            if (!activeDesignTypes.TryGetValue(row.DesignType, out var designTypeCode))
            {
                errors.Add($"Row {row.RowNumber}: Design Type '{row.DesignType}' is not active in the Design Type master.");
                continue;
            }

            var movementTypeId = movementTypeIdsByCode.GetValueOrDefault(row.MovementTypeCode, SkuMovementTypeIds.SlowMoving);
            var seasonId = seasonIdsByCode.GetValueOrDefault(row.SeasonCode, SeasonIds.Rainy);

            if (existing.TryGetValue(row.MaterialNumber, out var material))
            {
                material.Description = row.Description;
                material.DesignType = designTypeCode;
                material.CharacteristicValue = row.CharacteristicValue;
                material.PackSize = row.PackSize;
                material.MrpPrice = row.MrpPrice;
                material.LengthMm = row.LengthMm;
                material.WidthMm = row.WidthMm;
                material.HeightMm = row.HeightMm;
                material.NetWeightKg = row.NetWeightKg;
                material.GrossWeightKg = row.GrossWeightKg;
                material.PalletCapacityBoxes = row.PalletCapacityBoxes;
                material.MovementTypeId = movementTypeId;
                material.SeasonId = seasonId;
                material.RecalculateDerivedFields();
                material.UpdatedAt = clock.UtcNow;
                material.UpdatedByUserId = currentUser.UserId;
                updated++;
            }
            else
            {
                material = new Material
                {
                    MaterialNumber = row.MaterialNumber,
                    Description = row.Description,
                    DesignType = designTypeCode,
                    CharacteristicValue = row.CharacteristicValue,
                    PackSize = row.PackSize,
                    MrpPrice = row.MrpPrice,
                    LengthMm = row.LengthMm,
                    WidthMm = row.WidthMm,
                    HeightMm = row.HeightMm,
                    NetWeightKg = row.NetWeightKg,
                    GrossWeightKg = row.GrossWeightKg,
                    PalletCapacityBoxes = row.PalletCapacityBoxes,
                    MovementTypeId = movementTypeId,
                    SeasonId = seasonId,
                    IsActive = true,
                    CreatedAt = clock.UtcNow,
                    CreatedByUserId = currentUser.UserId
                };
                material.RecalculateDerivedFields();
                db.Materials.Add(material);
                existing[row.MaterialNumber] = material;
                created++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return new ImportMaterialsResult(created, updated, errors);
    }
}
