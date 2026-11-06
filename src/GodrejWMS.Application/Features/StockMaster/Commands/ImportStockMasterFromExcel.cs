using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.StockMaster.Commands;

/// <summary>Replaces one warehouse's current stock with an Inventory-Master-shaped Excel upload, placing each
/// row's quantity directly at the named Pallet Position. Unlike the Inward workflow, this never
/// runs the put-away allocation engine - it's for an initial/opening-balance data load (or a
/// correction) where the client already knows exactly which location each batch sits in. Both
/// the Material and the Pallet Position must already exist (created via Material Master / Location
/// Master first); this command only ever writes to StockBatches.
/// The upload replaces, never merges, the stock of every Pallet Position the file lists: batches there that the
/// file does not list are set to zero (the row is kept, so adjustment history stays linked); other locations are
/// untouched. Rows with errors are skipped and reported while the correct rows are loaded.
/// <see cref="WarehouseId"/> may be omitted only when the user has exactly one accessible warehouse.</summary>
public sealed record ImportStockMasterFromExcelCommand(Stream FileStream, int? WarehouseId = null) : IRequest<ImportStockMasterResult>;

public sealed record ImportStockMasterResult(
    int Created, int Updated, IReadOnlyList<string> Errors, IReadOnlyList<string>? Warnings = null, int Cleared = 0);

public sealed class ImportStockMasterFromExcelValidator : AbstractValidator<ImportStockMasterFromExcelCommand>
{
    public ImportStockMasterFromExcelValidator()
    {
        RuleFor(x => x.FileStream).NotNull();
    }
}

public sealed class ImportStockMasterFromExcelHandler(
    IApplicationDbContext db, IExcelService excel, IDateTimeProvider clock, ICurrentUserService currentUser, IPermissionService permissions,
    IWarehouseAccessService warehouseAccess)
    : IRequestHandler<ImportStockMasterFromExcelCommand, ImportStockMasterResult>
{
    public async Task<ImportStockMasterResult> Handle(ImportStockMasterFromExcelCommand request, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Inventory.Import, cancellationToken))
        {
            throw new UnauthorizedAccessException("You don't have permission to directly import opening inventory. Quantity corrections must use the adjustment approval workflow.");
        }

        var warehouseId = request.WarehouseId;
        if (warehouseId is null)
        {
            var accessible = await warehouseAccess.GetAccessibleWarehousesAsync(cancellationToken);
            if (accessible.Count != 1)
            {
                throw new InvalidOperationException("Select the warehouse whose inventory this file replaces.");
            }

            warehouseId = accessible[0].Id;
        }
        else if (!await warehouseAccess.HasAccessAsync(warehouseId.Value, cancellationToken))
        {
            throw new UnauthorizedAccessException("Warehouse is not assigned to you.");
        }

        var rows = excel.ReadStockMaster(request.FileStream);
        var errors = new List<string>();
        if (rows.Count == 0)
        {
            return new ImportStockMasterResult(0, 0, ["The file has no stock rows, so nothing was changed."]);
        }

        var materialsByNumber = await db.Materials
            .Select(m => new { m.MaterialNumber, m.Id, m.PalletCapacityBoxes, m.IsActive })
            .ToDictionaryAsync(m => m.MaterialNumber, cancellationToken);
        var materialsById = materialsByNumber.Values.ToDictionary(m => m.Id);
        var positions = await db.PalletPositions
            .Select(p => new { p.Id, p.LocationCode, p.MaxPallets, p.Rack.WarehouseId })
            .ToDictionaryAsync(p => p.LocationCode, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var existingBatches = await db.StockBatches
            .Where(b => b.PalletPosition.Rack.WarehouseId == warehouseId.Value)
            .ToListAsync(cancellationToken);
        var batchesByKey = existingBatches.ToDictionary(b => (b.MaterialId, b.PalletPositionId, b.MfgMonth));

        // Rows with errors are skipped and reported; the correct rows are still loaded.
        var valid = new List<(long MaterialNumber, int MaterialId, int PositionId, string LocationCode, int MfgMonth, decimal Quantity)>();
        var rowOfKey = new Dictionary<(int MaterialId, int PositionId, int MfgMonth), int>();
        foreach (var row in rows)
        {
            if (row.InvalidMaterialCode is not null)
            {
                errors.Add(row.InvalidMaterialCode.Length == 0
                    ? $"Row {row.RowNumber}: Material Code is required."
                    : $"Row {row.RowNumber}: Material Code '{row.InvalidMaterialCode}' is not a valid number.");
                continue;
            }

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

            if (string.IsNullOrWhiteSpace(row.PalletPositionCode) || !positions.TryGetValue(row.PalletPositionCode, out var position))
            {
                errors.Add($"Row {row.RowNumber}: Pallet Position '{row.PalletPositionCode}' was not found. Add it in Location Master first.");
                continue;
            }

            if (position.WarehouseId != warehouseId.Value)
            {
                errors.Add($"Row {row.RowNumber}: Pallet Position '{row.PalletPositionCode}' belongs to a different warehouse than the one selected.");
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

            // A location may hold several materials / PKMs (left-over stock), but each Material + PKM + Position is one batch.
            var key = (material.Id, position.Id, mfgMonth);
            if (rowOfKey.TryGetValue(key, out var firstRow))
            {
                errors.Add($"Row {row.RowNumber}: same Material Code, PKM and Pallet Position as row {firstRow}. Combine them into one row.");
                continue;
            }

            rowOfKey[key] = row.RowNumber;
            valid.Add((row.MaterialNumber, material.Id, position.Id, position.LocationCode, mfgMonth, row.QuantityBoxes));
        }

        if (valid.Count == 0)
        {
            return new ImportStockMasterResult(0, 0, errors);
        }

        // Replace, don't merge: at every location the file lists, batches the file does not list go to zero.
        var now = clock.UtcNow;
        var cleared = 0;
        var listedPositions = valid.Select(v => v.PositionId).ToHashSet();
        foreach (var batch in existingBatches.Where(b => b.QuantityBoxes > 0 && listedPositions.Contains(b.PalletPositionId)
                     && !rowOfKey.ContainsKey((b.MaterialId, b.PalletPositionId, b.MfgMonth))))
        {
            batch.QuantityBoxes = 0;
            batch.RowVersion++;
            batch.UpdatedAt = now;
            batch.UpdatedByUserId = currentUser.UserId;
            cleared++;
        }

        int created = 0, updated = 0;
        var allBatches = new List<StockBatch>(existingBatches);
        foreach (var entry in valid)
        {
            if (batchesByKey.TryGetValue((entry.MaterialId, entry.PositionId, entry.MfgMonth), out var batch))
            {
                batch.QuantityBoxes = entry.Quantity;
                batch.RowVersion++;
                batch.UpdatedAt = now;
                batch.UpdatedByUserId = currentUser.UserId;
                updated++;
            }
            else
            {
                var newBatch = new StockBatch
                {
                    MaterialId = entry.MaterialId,
                    PalletPositionId = entry.PositionId,
                    MfgMonth = entry.MfgMonth,
                    QuantityBoxes = entry.Quantity,
                    CreatedAt = now,
                    CreatedByUserId = currentUser.UserId
                };
                db.StockBatches.Add(newBatch);
                allBatches.Add(newBatch);
                created++;
            }
        }

        // A location holding several materials is only as roomy as its smallest pallet. Stock that already exists
        // physically is still loaded; an over-full location is reported as a warning instead of rejecting rows.
        var warnings = new List<string>();
        var positionsById = positions.Values.ToDictionary(p => p.Id);
        var touchedPositions = valid.Select(v => v.PositionId).ToHashSet();
        foreach (var group in allBatches
                     .Where(b => touchedPositions.Contains(b.PalletPositionId) && b.QuantityBoxes > 0 && materialsById[b.MaterialId].IsActive)
                     .GroupBy(b => b.PalletPositionId))
        {
            var position = positionsById[group.Key];
            var palletSizes = group.Select(b => materialsById[b.MaterialId].PalletCapacityBoxes).ToList();
            var capacity = PalletCapacityCalculator.EffectiveCapacityBoxes(position.MaxPallets, palletSizes);
            var total = group.Sum(b => b.QuantityBoxes);
            if (total > capacity)
            {
                warnings.Add($"{position.LocationCode}: {total:0.###} boxes exceed the capacity of {capacity:0.###} (Max Pallets {position.MaxPallets} x smallest Pallet Size {palletSizes.Min()} among {group.Select(b => b.MaterialId).Distinct().Count()} material(s)). Stock was loaded.");
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return new ImportStockMasterResult(created, updated, errors, warnings, cleared);
    }
}
