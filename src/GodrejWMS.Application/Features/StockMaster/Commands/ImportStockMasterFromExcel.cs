using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.StockMaster.Commands;

/// <summary>Bulk-loads current stock from an Inventory-Master-shaped Excel upload, placing each
/// row's quantity directly at the named Pallet Position. Unlike the Inward workflow, this never
/// runs the put-away allocation engine - it's for an initial/opening-balance data load (or a
/// correction) where the client already knows exactly which location each batch sits in. Both
/// the Material and the Pallet Position must already exist (created via Material Master / Location
/// Master first); this command only ever writes to StockBatches.</summary>
public sealed record ImportStockMasterFromExcelCommand(Stream FileStream) : IRequest<ImportStockMasterResult>;

public sealed record ImportStockMasterResult(int Created, int Updated, IReadOnlyList<string> Errors);

public sealed class ImportStockMasterFromExcelValidator : AbstractValidator<ImportStockMasterFromExcelCommand>
{
    public ImportStockMasterFromExcelValidator()
    {
        RuleFor(x => x.FileStream).NotNull();
    }
}

public sealed class ImportStockMasterFromExcelHandler(
    IApplicationDbContext db, IExcelService excel, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<ImportStockMasterFromExcelCommand, ImportStockMasterResult>
{
    public async Task<ImportStockMasterResult> Handle(ImportStockMasterFromExcelCommand request, CancellationToken cancellationToken)
    {
        var rows = excel.ReadStockMaster(request.FileStream);
        var errors = new List<string>();
        int created = 0, updated = 0;

        var materialsByNumber = await db.Materials
            .Select(m => new { m.MaterialNumber, m.Id, m.PalletCapacityBoxes })
            .ToDictionaryAsync(m => m.MaterialNumber, cancellationToken);
        var positions = await db.PalletPositions
            .Select(p => new { p.Id, p.LocationCode, p.MaxPallets })
            .ToDictionaryAsync(p => p.LocationCode, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var existingBatches = await db.StockBatches.ToListAsync(cancellationToken);
        var batchesByKey = existingBatches.ToDictionary(b => (b.MaterialId, b.PalletPositionId, b.MfgMonth));
        var occupiedByPosition = existingBatches
            .GroupBy(b => b.PalletPositionId)
            .ToDictionary(g => g.Key, g => g.Sum(b => b.QuantityBoxes));

        foreach (var row in rows)
        {
            if (row.MaterialNumber <= 0)
            {
                errors.Add($"Row {row.RowNumber}: Material Code is required.");
                continue;
            }

            if (!materialsByNumber.TryGetValue(row.MaterialNumber, out var material))
            {
                errors.Add($"Row {row.RowNumber}: Material Code {row.MaterialNumber} was not found. Add it in Material Master first.");
                continue;
            }

            var materialId = material.Id;

            if (string.IsNullOrWhiteSpace(row.PalletPositionCode) || !positions.TryGetValue(row.PalletPositionCode, out var position))
            {
                errors.Add($"Row {row.RowNumber}: Pallet Position '{row.PalletPositionCode}' was not found. Add it in Location Master first.");
                continue;
            }

            if (!MfgMonthParser.TryParse(row.MfgMonthText, out var mfgMonth))
            {
                errors.Add($"Row {row.RowNumber}: Mfg Month '{row.MfgMonthText}' is not a recognized format (expected e.g. \"MAR|2026\").");
                continue;
            }

            if (row.QuantityBoxes <= 0)
            {
                errors.Add($"Row {row.RowNumber}: Total Stock in CFB must be greater than zero.");
                continue;
            }

            var key = (materialId, position.Id, mfgMonth);
            var priorQuantityAtPosition = occupiedByPosition.GetValueOrDefault(position.Id)
                - (batchesByKey.TryGetValue(key, out var existingForCapacity) ? existingForCapacity.QuantityBoxes : 0);
            var effectiveCapacity = PalletCapacityCalculator.EffectiveCapacityBoxes(position.MaxPallets, material.PalletCapacityBoxes);
            if (priorQuantityAtPosition + row.QuantityBoxes > effectiveCapacity)
            {
                errors.Add($"Row {row.RowNumber}: {row.QuantityBoxes:0.###} boxes would exceed {row.PalletPositionCode}'s capacity of {effectiveCapacity:0.###} (Max Pallets x Material Pallet Size; already holds {priorQuantityAtPosition:0.###} from other batches).");
                continue;
            }

            if (batchesByKey.TryGetValue(key, out var batch))
            {
                occupiedByPosition[position.Id] = occupiedByPosition.GetValueOrDefault(position.Id) - batch.QuantityBoxes + row.QuantityBoxes;
                batch.QuantityBoxes = row.QuantityBoxes;
                batch.RowVersion++;
                batch.UpdatedAt = clock.UtcNow;
                batch.UpdatedByUserId = currentUser.UserId;
                updated++;
            }
            else
            {
                var newBatch = new StockBatch
                {
                    MaterialId = materialId,
                    PalletPositionId = position.Id,
                    MfgMonth = mfgMonth,
                    QuantityBoxes = row.QuantityBoxes,
                    CreatedAt = clock.UtcNow,
                    CreatedByUserId = currentUser.UserId
                };
                db.StockBatches.Add(newBatch);
                occupiedByPosition[position.Id] = occupiedByPosition.GetValueOrDefault(position.Id) + row.QuantityBoxes;
                created++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return new ImportStockMasterResult(created, updated, errors);
    }
}
