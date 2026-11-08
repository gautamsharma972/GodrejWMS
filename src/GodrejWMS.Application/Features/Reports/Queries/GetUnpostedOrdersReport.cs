using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Queries;

/// <summary>An order whose stock was never posted to inventory: from before posting on upload, or an inward waiting for its PKM.</summary>
public sealed record UnpostedOrderRowDto(
    string Kind,
    int Id,
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    string WarehouseCode,
    int Lines,
    decimal Boxes,
    string Reason);

/// <summary>Exceptions report for the move to "post on upload": every non-rejected GRN that still has an open (unconfirmed)
/// put-away and every pullout that is not confirmed. Nothing is posted automatically - each needs a decision (confirm it,
/// have the picker assign the PKM, or reject it).</summary>
public sealed record GetUnpostedOrdersReportQuery : IRequest<IReadOnlyList<UnpostedOrderRowDto>>;

public sealed class GetUnpostedOrdersReportHandler(IApplicationDbContext db, IPermissionService permissions)
    : IRequestHandler<GetUnpostedOrdersReportQuery, IReadOnlyList<UnpostedOrderRowDto>>
{
    public async Task<IReadOnlyList<UnpostedOrderRowDto>> Handle(GetUnpostedOrdersReportQuery request, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Reports.View, cancellationToken))
            throw new UnauthorizedAccessException("Reports require an authorized warehouse role.");

        var grns = await db.InwardTransactions.AsNoTracking()
            .Where(t => !t.IsRejected && t.Lines.Any(l => l.Putaways.Any(p => !p.IsConfirmed)))
            .Select(t => new
            {
                t.Id,
                t.ReferenceNumber,
                t.CreatedAt,
                WarehouseCode = db.Warehouses.Where(w => w.Id == t.WarehouseId).Select(w => w.Code).FirstOrDefault(),
                Lines = t.Lines.Count,
                Boxes = t.Lines.SelectMany(l => l.Putaways).Where(p => !p.IsConfirmed).Sum(p => (decimal?)p.QuantityBoxes) ?? 0m,
                t.IsPkmless
            })
            .ToListAsync(cancellationToken);

        var pullouts = await db.PulloutTransactions.AsNoTracking()
            .Where(t => !t.IsRejected && !t.IsConfirmed)
            .Select(t => new
            {
                t.Id,
                t.ReferenceNumber,
                t.CreatedAt,
                WarehouseCode = db.Warehouses.Where(w => w.Id == t.WarehouseId).Select(w => w.Code).FirstOrDefault(),
                Lines = t.Lines.Count,
                Boxes = t.Lines.Sum(l => (decimal?)l.PickedQuantityBoxes) ?? 0m
            })
            .ToListAsync(cancellationToken);

        return grns
            .Select(g => new UnpostedOrderRowDto(
                "GRN", g.Id, g.ReferenceNumber, g.CreatedAt, g.WarehouseCode ?? "-", g.Lines, g.Boxes,
                g.IsPkmless ? "Inward without PKM - posts when the picker assigns the PKM" : "Reserved but never confirmed - confirm it or reject it"))
            .Concat(pullouts.Select(p => new UnpostedOrderRowDto(
                "Pullout", p.Id, p.ReferenceNumber, p.CreatedAt, p.WarehouseCode ?? "-", p.Lines, p.Boxes,
                "Saved picks never confirmed - confirm it or reject it")))
            .OrderBy(r => r.CreatedAt)
            .ToList();
    }
}
