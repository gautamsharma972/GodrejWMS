using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Queries;

public sealed record GetInwardDetailQuery(int Id) : IRequest<InwardDetailDto>;

public sealed class GetInwardDetailHandler(IApplicationDbContext db)
    : IRequestHandler<GetInwardDetailQuery, InwardDetailDto>
{
    public async Task<InwardDetailDto> Handle(GetInwardDetailQuery request, CancellationToken cancellationToken)
    {
        var transaction = await db.InwardTransactions
            .AsNoTracking()
            .Include(t => t.Lines)
                .ThenInclude(l => l.Material)
            .Include(t => t.Lines)
                .ThenInclude(l => l.Putaways)
                    .ThenInclude(p => p.PalletPosition)
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(InwardTransaction), request.Id);

        // Loaded separately (not Included) so a GRN with many put-aways does not multiply its rows by the unsaved lines.
        var unsavedLines = await db.InwardUnsavedLines
            .AsNoTracking()
            .Where(u => u.InwardTransactionId == transaction.Id)
            .OrderBy(u => u.SourceRow ?? int.MaxValue).ThenBy(u => u.Id)
            .Select(u => new InwardUnsavedLineDto(u.CodeText, u.QuantityBoxes, u.Kind, u.Reason, u.SourceRow, u.Description, u.MfgMonth))
            .ToListAsync(cancellationToken);

        var netPosted = transaction.PostedAt is null
            ? new Dictionary<(int MaterialId, int MfgMonth), decimal>()
            : await GrnPostedQuantity.NetPostedAsync(db, transaction.ReferenceNumber, cancellationToken);
        var recorded = await InwardVariances.AccountedAsync(db, transaction.Id, cancellationToken);
        var varianceRows = await db.InwardLineVariances.AsNoTracking()
            .Where(v => v.InwardTransactionId == transaction.Id)
            .OrderBy(v => v.Id)
            .Select(v => new
            {
                v.Id, v.Type, v.Material.MaterialNumber, v.Material.Description, v.MfgMonth,
                Location = v.PalletPosition.LocationCode, v.QuantityBoxes, v.Remarks, v.CreatedByUserName, v.CreatedAt
            })
            .ToListAsync(cancellationToken);
        var variances = varianceRows
            .Select(v => new InwardVarianceDto(
                v.Id, v.Type, v.MaterialNumber, v.Description, MfgMonthParser.Format(v.MfgMonth), v.Location,
                v.QuantityBoxes, v.Remarks, v.CreatedByUserName, v.CreatedAt))
            .ToList();
        var firstOfGroup = transaction.Lines.GroupBy(l => (l.MaterialId, l.MfgMonth)).Select(g => g.OrderBy(l => l.Id).First().Id).ToHashSet();
        // Boxes recorded as damage / shortage / mismatch are accounted for (shown on the group's first line, like the other group figures).
        decimal VarianceOf(InwardTransactionLine l) =>
            l.MfgMonth is { } m && firstOfGroup.Contains(l.Id) ? recorded.GetValueOrDefault((l.MaterialId, m)) : 0m;
        (decimal Unplaced, decimal Left) Split(InwardTransactionLine l)
        {
            var shortfall = Math.Max(0, l.RequestedQuantityBoxes - l.AllocatedQuantityBoxes - VarianceOf(l));
            if (shortfall <= 0)
            {
                return (0, 0);
            }

            // Before the GRN is posted there is no ledger: everything without a location is simply unplaced.
            if (transaction.PostedAt is null || l.MfgMonth is null || !firstOfGroup.Contains(l.Id))
            {
                return (shortfall, 0);
            }

            var unplaced = Math.Min(shortfall, GrnPostedQuantity.UnplacedBoxes(transaction.Lines, l, netPosted, recorded));
            return (unplaced, shortfall - unplaced);
        }

        // Which pullouts took the boxes that left inventory (moved / adjusted boxes have no reference to show).
        var pickInfo = new Dictionary<int, (decimal Boxes, IReadOnlyList<string> References)>();
        foreach (var l in transaction.Lines)
        {
            var left = Split(l).Left;
            if (left <= 0)
            {
                continue;
            }

            var picked = await GrnPostedQuantity.PickedAgainstAsync(db, transaction, l, cancellationToken);
            if (picked.Count > 0)
            {
                pickInfo[l.Id] = (Math.Min(left, picked.Sum(p => p.Boxes)), picked.Select(p => p.Reference).ToList());
            }
        }

        var lines = transaction.Lines
            .OrderBy(l => l.Id)
            .Select(l => new InwardDetailLineDto(
                l.Material.MaterialNumber,
                l.Material.Description,
                l.Material.DesignType,
                MfgMonthParser.FormatOrNoPkm(l.MfgMonth),
                l.RequestedQuantityBoxes,
                l.AllocatedQuantityBoxes,
                l.Status,
                l.Remarks,
                l.Putaways
                    .OrderBy(p => p.PalletPosition.LocationCode)
                    .Select(p => new InwardPutawayDto(
                        p.ExcludedLocation ?? p.PalletPosition.LocationCode, p.QuantityBoxes, p.IsConfirmed, p.Id, p.AllocationReason,
                        p.OverrideReason, p.ConfirmedAt, p.ConfirmedByUserName, p.ExcludedLocation, p.PalletPosition.Id, p.IsPickerPlaced, p.IsExcludedLocked,
                        p.PalletPosition.IsVirtual))
                    .ToList(),
                l.Material.IsActive,
                Split(l).Unplaced,
                Split(l).Left,
                pickInfo.TryGetValue(l.Id, out var info) ? info.Boxes : 0m,
                pickInfo.TryGetValue(l.Id, out var info2) ? info2.References : null,
                VarianceOf(l)))
            .ToList();

        return new InwardDetailDto(
            transaction.Id,
            transaction.ReferenceNumber,
            transaction.CreatedAt,
            lines,
            // All(...) is vacuously true for zero putaways (a totally failed allocation, or a
            // rejected GRN whose putaways RejectInwardHandler deletes) - Any() guards that so a
            // rejected/unconfirmed GRN never also reports as confirmed.
            IsConfirmed: transaction.Lines.SelectMany(l => l.Putaways).Any() &&
                transaction.Lines.SelectMany(l => l.Putaways).All(p => p.IsConfirmed) &&
                // GIT / NO RACK entries stay correctable by the Picker until a supervisor confirms the GRN
                !transaction.Lines.SelectMany(l => l.Putaways).Any(p => p.ExcludedLocation != null && !p.IsExcludedLocked),
            IsRejected: transaction.IsRejected,
            RejectedAt: transaction.RejectedAt,
            RejectedByUserName: transaction.RejectedByUserName,
            RejectionReason: transaction.RejectionReason,
            VehicleNumber: transaction.VehicleNumber,
            LrNumber: transaction.LrNumber,
            UploadHeaderJson: transaction.UploadHeaderJson,
            ReceivedFrom: transaction.ReceivedFrom,
            UnsavedLines: unsavedLines,
            IsPkmless: transaction.IsPkmless,
            PostedAt: transaction.PostedAt,
            Variances: variances);
    }
}

