using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.StockMaster.Commands;

/// <summary>Adds boxes of one material to one location as a single record (the one-at-a-time counterpart of the Excel upload).
/// The quantity is ADDED to whatever the location already holds of the same material and PKM, and a location that already
/// holds this or other materials still accepts it. It never replaces or clears anything. Goes through the stock posting
/// service, so a ledger entry is written and a virtual location is marked in use.</summary>
[RequiresPermission(PermissionKeys.Inventory.Import)]
public sealed record AddStockRecordCommand(long MaterialNumber, string LocationCode, string MfgMonthText, decimal QuantityBoxes)
    : IRequest<AddStockRecordResult>;

public sealed record AddStockRecordResult(
    string LocationCode, long MaterialNumber, string MfgMonthLabel, decimal AddedBoxes, decimal NewQuantityBoxes, string? Warning);

public sealed class AddStockRecordValidator : AbstractValidator<AddStockRecordCommand>
{
    public AddStockRecordValidator()
    {
        RuleFor(x => x.MaterialNumber).GreaterThan(0).WithMessage("Enter a Material Code.");
        RuleFor(x => x.LocationCode).NotEmpty().WithMessage("Enter a Pallet Position (location code).");
        RuleFor(x => x.MfgMonthText).NotEmpty().WithMessage("Enter the Mfg Month, e.g. NOV|2026.");
        RuleFor(x => x.QuantityBoxes).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
    }
}

public sealed class AddStockRecordHandler(
    IApplicationDbContext db,
    IStockPostingService posting,
    IUnitOfWork uow,
    IWarehouseAccessService warehouseAccess) : IRequestHandler<AddStockRecordCommand, AddStockRecordResult>
{
    public Task<AddStockRecordResult> Handle(AddStockRecordCommand request, CancellationToken cancellationToken) =>
        uow.ExecuteInTransactionAsync(ct => RunAsync(request, ct), cancellationToken);

    private async Task<AddStockRecordResult> RunAsync(AddStockRecordCommand request, CancellationToken cancellationToken)
    {
        var material = await db.Materials.FirstOrDefaultAsync(m => m.MaterialNumber == request.MaterialNumber, cancellationToken)
            ?? throw new InvalidOperationException($"Material Code {request.MaterialNumber} was not found. Add it in Material Master first.");

        var code = request.LocationCode.Trim();
        var position = await db.PalletPositions.Include(p => p.Rack)
            .FirstOrDefaultAsync(p => p.LocationCode == code, cancellationToken)
            ?? throw new InvalidOperationException($"Pallet Position '{code}' was not found. Add it in Location Master first.");

        if (!await warehouseAccess.HasAccessAsync(position.Rack.WarehouseId, cancellationToken))
        {
            throw new UnauthorizedAccessException("This location belongs to a warehouse that is not assigned to you.");
        }

        if (!position.IsActive)
        {
            throw new InvalidOperationException($"Pallet Position '{position.LocationCode}' is inactive.");
        }

        if (!MfgMonthParser.TryParse(request.MfgMonthText, out var mfgMonth))
        {
            throw new InvalidOperationException($"Mfg Month '{request.MfgMonthText}' is not a recognized format (expected e.g. \"NOV|2026\").");
        }

        await posting.ApplyAsync(
            new StockPostingContext(StockLedgerEntryTypes.StockImport, "STOCK", position.LocationCode, "Single stock record added"),
            [new StockChange(material.Id, mfgMonth, LocationSubtypeIds.Good, position, request.QuantityBoxes)],
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        // A location holding several materials is only as roomy as its smallest pallet. The stock is added anyway (it is
        // physically there); an over-full location is reported, as the Excel upload does.
        var batches = await db.StockBatches.AsNoTracking().Include(b => b.Material)
            .Where(b => b.PalletPositionId == position.Id && b.QuantityBoxes > 0)
            .ToListAsync(cancellationToken);
        var capacity = PalletCapacityCalculator.EffectiveCapacityBoxes(position.MaxPallets, batches.Select(b => b.Material.PalletCapacityBoxes).ToList());
        var total = batches.Sum(b => b.QuantityBoxes);
        var warning = total > capacity
            ? $"{position.LocationCode} now holds {total:0.###} boxes, above its capacity of {capacity:0.###}. The stock was added."
            : null;

        var newQuantity = batches.Where(b => b.MaterialId == material.Id && b.MfgMonth == mfgMonth).Sum(b => b.QuantityBoxes);
        return new AddStockRecordResult(
            position.LocationCode, material.MaterialNumber, MfgMonthParser.FormatOrNoPkm(mfgMonth), request.QuantityBoxes, newQuantity, warning);
    }
}
