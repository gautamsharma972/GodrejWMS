using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.MaterialConversions.Dtos;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.MaterialConversions.Commands;

public sealed record ConversionLineInput(int StockBatchId, decimal QuantityBoxes);

/// <summary>Request to convert stock of the old code into the new code (usually the proposal of <c>GetMaterialConversionProposalQuery</c>, possibly edited).
/// Needs approval; a user who may also approve is the final reviewer, so theirs is applied at once.</summary>
public sealed record SubmitMaterialConversionCommand(
    int FromMaterialId, int ToMaterialId, int WarehouseId, IReadOnlyList<ConversionLineInput> Lines, string? ReasonText = null)
    : IRequest<MaterialConversionDto>;

public sealed record ApproveMaterialConversionCommand(int ConversionId, string? Comment = null) : IRequest<MaterialConversionDto>;

public sealed record RejectMaterialConversionCommand(int ConversionId, string Comment) : IRequest<MaterialConversionDto>;

public sealed record CancelMaterialConversionCommand(int ConversionId) : IRequest<MaterialConversionDto>;

public sealed class SubmitMaterialConversionValidator : AbstractValidator<SubmitMaterialConversionCommand>
{
    public SubmitMaterialConversionValidator()
    {
        RuleFor(x => x.FromMaterialId).GreaterThan(0).WithMessage("Select the old material code.");
        RuleFor(x => x.ToMaterialId).GreaterThan(0).WithMessage("Select the new material code.");
        RuleFor(x => x.WarehouseId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Select at least one stock line to convert.");
        RuleForEach(x => x.Lines).ChildRules(l => l.RuleFor(i => i.QuantityBoxes).GreaterThan(0).WithMessage("Quantity must be greater than zero."));
        RuleFor(x => x.ReasonText).MaximumLength(500);
    }
}

public sealed class RejectMaterialConversionValidator : AbstractValidator<RejectMaterialConversionCommand>
{
    public RejectMaterialConversionValidator()
    {
        RuleFor(x => x.ConversionId).GreaterThan(0);
        RuleFor(x => x.Comment).NotEmpty().WithMessage("Enter the reason for rejecting.").MaximumLength(500);
    }
}

public sealed class SubmitMaterialConversionHandler(
    IApplicationDbContext db, IStockPostingService posting, IUnitOfWork uow, IDateTimeProvider clock, ICurrentUserService currentUser,
    IPermissionService permissions, IWarehouseAccessService warehouseAccess)
    : IRequestHandler<SubmitMaterialConversionCommand, MaterialConversionDto>
{
    public async Task<MaterialConversionDto> Handle(SubmitMaterialConversionCommand request, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Inventory.ConvertMaterial, cancellationToken))
        {
            throw new UnauthorizedAccessException("You are not permitted to request material conversions.");
        }

        if (!await warehouseAccess.HasAccessAsync(request.WarehouseId, cancellationToken))
        {
            throw new UnauthorizedAccessException("You do not have access to this warehouse.");
        }

        var (from, to) = await MaterialConversionRules.LoadMaterialsAsync(db, request.FromMaterialId, request.ToMaterialId, cancellationToken);
        if (request.Lines.GroupBy(l => l.StockBatchId).Any(g => g.Count() > 1))
        {
            throw new InvalidOperationException("Each stock line can only be selected once.");
        }

        var batchIds = request.Lines.Select(l => l.StockBatchId).ToList();
        var batches = await MaterialConversionRules.ConvertibleBatches(db, from.Id, request.WarehouseId)
            .Where(b => batchIds.Contains(b.Id))
            .Include(b => b.PalletPosition)
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        var pendingBatches = await db.MaterialConversionLines
            .Where(l => l.StockBatchId != null && batchIds.Contains(l.StockBatchId.Value) && l.MaterialConversion.Status == MaterialConversionStatus.PendingApproval)
            .Select(l => l.StockBatchId!.Value).ToListAsync(cancellationToken);
        var pendingAdjustments = await db.InventoryAdjustmentLines
            .Where(l => l.StockBatchId != null && batchIds.Contains(l.StockBatchId.Value) && l.InventoryAdjustment.Status == Domain.Enums.InventoryAdjustmentStatus.PendingApproval)
            .Select(l => l.StockBatchId!.Value).ToListAsync(cancellationToken);

        var now = clock.UtcNow;
        var conversion = new MaterialConversion
        {
            ConversionNumber = $"MCV-{now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid().ToString("N")[..8]}",
            WarehouseId = request.WarehouseId,
            FromMaterialId = from.Id,
            ToMaterialId = to.Id,
            Status = MaterialConversionStatus.PendingApproval,
            ReasonText = string.IsNullOrWhiteSpace(request.ReasonText) ? null : request.ReasonText.Trim(),
            RequestedByUserId = currentUser.UserId,
            RequestedByUserName = currentUser.UserName,
            RequestedAt = now,
            RowVersion = 1
        };
        foreach (var input in request.Lines)
        {
            if (!batches.TryGetValue(input.StockBatchId, out var batch))
            {
                throw new InvalidOperationException("A selected stock line is no longer available to convert. Run the proposal again.");
            }

            if (input.QuantityBoxes > batch.QuantityBoxes)
            {
                throw new InvalidOperationException($"{batch.PalletPosition.LocationCode} holds only {batch.QuantityBoxes:0.###} box(es); {input.QuantityBoxes:0.###} cannot be converted.");
            }

            if (pendingBatches.Contains(batch.Id))
            {
                throw new InvalidOperationException($"A conversion request is already pending for {batch.PalletPosition.LocationCode}.");
            }

            if (pendingAdjustments.Contains(batch.Id))
            {
                throw new InvalidOperationException($"An inventory adjustment is pending for {batch.PalletPosition.LocationCode}; finish it first.");
            }

            conversion.Lines.Add(new MaterialConversionLine
            {
                StockBatchId = batch.Id,
                PalletPositionId = batch.PalletPositionId,
                MfgMonth = batch.MfgMonth,
                StockSubtypeId = batch.StockSubtypeId,
                QuantityBoxes = input.QuantityBoxes,
                SystemQuantityAtRequest = batch.QuantityBoxes,
                BatchRowVersionAtRequest = batch.RowVersion
            });
        }

        conversion.TotalBoxes = conversion.Lines.Sum(l => l.QuantityBoxes);
        db.MaterialConversions.Add(conversion);
        await db.SaveChangesAsync(cancellationToken);

        // A user who may approve is the final reviewer: apply the request at once (same rule as inventory adjustments).
        if (await permissions.HasPermissionAsync(PermissionKeys.Inventory.ApproveMaterialConversion, cancellationToken))
        {
            return await MaterialConversionApproval.ApproveAsync(
                db, posting, uow, clock, currentUser, conversion.Id, "Auto-approved for an authorized reviewer.", cancellationToken);
        }

        return await MaterialConversionApproval.LoadDtoAsync(db, conversion.Id, cancellationToken);
    }
}

public sealed class ApproveMaterialConversionHandler(
    IApplicationDbContext db, IStockPostingService posting, IUnitOfWork uow, IDateTimeProvider clock, ICurrentUserService currentUser,
    IPermissionService permissions, IWarehouseAccessService warehouseAccess)
    : IRequestHandler<ApproveMaterialConversionCommand, MaterialConversionDto>
{
    public async Task<MaterialConversionDto> Handle(ApproveMaterialConversionCommand request, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Inventory.ApproveMaterialConversion, cancellationToken))
        {
            throw new UnauthorizedAccessException("You are not permitted to approve material conversions.");
        }

        var warehouseId = await db.MaterialConversions.AsNoTracking().Where(c => c.Id == request.ConversionId).Select(c => (int?)c.WarehouseId).FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(MaterialConversion), request.ConversionId);
        if (!await warehouseAccess.HasAccessAsync(warehouseId, cancellationToken))
        {
            throw new UnauthorizedAccessException("You do not have access to this warehouse.");
        }

        return await MaterialConversionApproval.ApproveAsync(db, posting, uow, clock, currentUser, request.ConversionId, request.Comment, cancellationToken);
    }
}

public sealed class RejectMaterialConversionHandler(
    IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser, IPermissionService permissions)
    : IRequestHandler<RejectMaterialConversionCommand, MaterialConversionDto>
{
    public async Task<MaterialConversionDto> Handle(RejectMaterialConversionCommand request, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Inventory.ApproveMaterialConversion, cancellationToken))
        {
            throw new UnauthorizedAccessException("You are not permitted to review material conversions.");
        }

        var conversion = await db.MaterialConversions.FirstOrDefaultAsync(c => c.Id == request.ConversionId, cancellationToken)
            ?? throw new NotFoundException(nameof(MaterialConversion), request.ConversionId);
        MaterialConversionApproval.EnsurePending(conversion);
        if (conversion.RequestedByUserId is not null && conversion.RequestedByUserId == currentUser.UserId)
        {
            throw new InvalidOperationException("You cannot review your own request. Cancel it instead.");
        }

        conversion.Status = MaterialConversionStatus.Rejected;
        MaterialConversionApproval.Stamp(conversion, clock, currentUser, request.Comment.Trim());
        await db.SaveChangesAsync(cancellationToken);
        return await MaterialConversionApproval.LoadDtoAsync(db, conversion.Id, cancellationToken);
    }
}

public sealed class CancelMaterialConversionHandler(
    IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser, IPermissionService permissions)
    : IRequestHandler<CancelMaterialConversionCommand, MaterialConversionDto>
{
    public async Task<MaterialConversionDto> Handle(CancelMaterialConversionCommand request, CancellationToken cancellationToken)
    {
        var conversion = await db.MaterialConversions.FirstOrDefaultAsync(c => c.Id == request.ConversionId, cancellationToken)
            ?? throw new NotFoundException(nameof(MaterialConversion), request.ConversionId);
        MaterialConversionApproval.EnsurePending(conversion);
        var mine = conversion.RequestedByUserId is not null && conversion.RequestedByUserId == currentUser.UserId;
        if (!mine && !await permissions.HasPermissionAsync(PermissionKeys.Inventory.ApproveMaterialConversion, cancellationToken))
        {
            throw new UnauthorizedAccessException("Only the requester or a reviewer can cancel this request.");
        }

        conversion.Status = MaterialConversionStatus.Cancelled;
        MaterialConversionApproval.Stamp(conversion, clock, currentUser, "Cancelled");
        await db.SaveChangesAsync(cancellationToken);
        return await MaterialConversionApproval.LoadDtoAsync(db, conversion.Id, cancellationToken);
    }
}

/// <summary>The approval itself, shared by Approve and the auto-approval of a submitter who may approve. Posts through the stock posting service, so the
/// ledger, the negative-stock guard and the virtual-location bookkeeping are the same as for every other stock change.</summary>
internal static class MaterialConversionApproval
{
    public static void EnsurePending(MaterialConversion conversion)
    {
        if (conversion.Status != MaterialConversionStatus.PendingApproval)
        {
            throw new InvalidOperationException($"This request is already {conversion.Status}.");
        }
    }

    public static void Stamp(MaterialConversion conversion, IDateTimeProvider clock, ICurrentUserService currentUser, string? comment)
    {
        conversion.ReviewedByUserId = currentUser.UserId;
        conversion.ReviewedByUserName = currentUser.UserName;
        conversion.ReviewedAt = clock.UtcNow;
        conversion.ReviewComment = comment;
        conversion.RowVersion++;
    }

    public static async Task<MaterialConversionDto> LoadDtoAsync(IApplicationDbContext db, int id, CancellationToken ct) =>
        MaterialConversionMapper.ToDto(await MaterialConversionMapper.WithDetail(db.MaterialConversions.AsNoTracking()).SingleAsync(c => c.Id == id, ct));

    public static async Task<MaterialConversionDto> ApproveAsync(
        IApplicationDbContext db, IStockPostingService posting, IUnitOfWork uow, IDateTimeProvider clock, ICurrentUserService currentUser,
        int conversionId, string? comment, CancellationToken ct)
    {
        await uow.ExecuteInTransactionAsync(async token =>
        {
            var conversion = await db.MaterialConversions
                .Include(c => c.FromMaterial).Include(c => c.ToMaterial)
                .Include(c => c.Lines).ThenInclude(l => l.PalletPosition).ThenInclude(p => p.Rack)
                .SingleOrDefaultAsync(c => c.Id == conversionId, token)
                ?? throw new NotFoundException(nameof(MaterialConversion), conversionId);
            await db.Entry(conversion).ReloadAsync(token);
            EnsurePending(conversion);
            if (!conversion.ToMaterial.IsActive)
            {
                throw new InvalidOperationException($"The new material code {conversion.ToMaterial.MaterialNumber} is inactive in Material Master.");
            }

            // The stock must still be exactly what the request saw; otherwise nobody should approve blind.
            var batchIds = conversion.Lines.Where(l => l.StockBatchId.HasValue).Select(l => l.StockBatchId!.Value).ToList();
            var batches = await db.StockBatches.Where(b => batchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, token);
            var changed = conversion.Lines.Where(l =>
                !l.StockBatchId.HasValue || !batches.TryGetValue(l.StockBatchId.Value, out var b)
                || b.RowVersion != l.BatchRowVersionAtRequest || b.QuantityBoxes != l.SystemQuantityAtRequest).ToList();
            if (changed.Count > 0)
            {
                conversion.Status = MaterialConversionStatus.Conflict;
                Stamp(conversion, clock, currentUser,
                    $"The stock changed after the request was made ({string.Join(", ", changed.Select(l => l.PalletPosition.LocationCode))}). Make a new request.");
                await db.SaveChangesAsync(token);
                return;
            }

            var changes = new List<StockChange>();
            foreach (var line in conversion.Lines)
            {
                changes.Add(new StockChange(conversion.FromMaterialId, line.MfgMonth, line.StockSubtypeId, line.PalletPosition, -line.QuantityBoxes));
                changes.Add(new StockChange(conversion.ToMaterialId, line.MfgMonth, line.StockSubtypeId, line.PalletPosition, line.QuantityBoxes));
            }

            await posting.ApplyAsync(
                new StockPostingContext(StockLedgerEntryTypes.MaterialConversion, "MATERIAL_CONVERSION", conversion.ConversionNumber,
                    $"{conversion.FromMaterial.MaterialNumber} → {conversion.ToMaterial.MaterialNumber}"),
                changes, token);
            conversion.Status = MaterialConversionStatus.Approved;
            Stamp(conversion, clock, currentUser, string.IsNullOrWhiteSpace(comment) ? null : comment.Trim());
            await db.SaveChangesAsync(token);
        }, ct);

        return await LoadDtoAsync(db, conversionId, ct);
    }
}
