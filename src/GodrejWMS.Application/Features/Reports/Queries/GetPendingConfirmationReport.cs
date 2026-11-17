using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Inward;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Queries;

/// <summary>One GRN or pullout that is posted to inventory but still waiting for the picker: stock in the system is ahead of (inward)
/// or behind (pullout) what is physically on the floor until the picker acts.</summary>
public sealed record PendingConfirmationRowDto(
    string Kind,
    int Id,
    string ReferenceNumber,
    DateTimeOffset Since,
    double AgeHours,
    bool IsOverdue,
    string WarehouseCode,
    int WaitingCount,
    decimal WaitingBoxes,
    int AwaitingConfirmCount,
    decimal PickReservedBoxes,
    string Status);

public sealed record PendingConfirmationReportDto(
    IReadOnlyList<PendingConfirmationRowDto> Rows,
    int OverdueHours,
    int OverdueCount,
    decimal WaitingInwardBoxes,
    decimal WaitingPulloutBoxes);

/// <summary>Pending-confirmation report: every non-rejected GRN whose put-aways the picker has not all submitted and confirmed, and every
/// posted pullout whose pick-list is not confirmed yet, with how long it has been waiting. Anything older than
/// <paramref name="OverdueHours"/> is flagged so the supervisor can chase it before it distorts the inventory.</summary>
public sealed record GetPendingConfirmationReportQuery(int? WarehouseId = null, int OverdueHours = 24) : IRequest<PendingConfirmationReportDto>;

public sealed class GetPendingConfirmationReportHandler(IApplicationDbContext db, IPermissionService permissions, IDateTimeProvider clock)
    : IRequestHandler<GetPendingConfirmationReportQuery, PendingConfirmationReportDto>
{
    public async Task<PendingConfirmationReportDto> Handle(GetPendingConfirmationReportQuery request, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Reports.View, cancellationToken))
            throw new UnauthorizedAccessException("Reports require an authorized warehouse role.");

        var now = clock.UtcNow;
        var overdueHours = Math.Max(1, request.OverdueHours);
        var label = PutawayPickReservation.Label;

        var grnQuery = db.InwardTransactions.AsNoTracking().Where(t => !t.IsRejected);
        if (request.WarehouseId is { } wh)
        {
            grnQuery = grnQuery.Where(t => t.WarehouseId == wh);
        }

        // Waiting for the picker: not placed, not locked, and either already posted (stock sits at the suggested cell) or still an open reservation.
        var grns = await grnQuery
            .Where(t => t.Lines.Any(l => l.Putaways.Any(p => !p.IsExcludedLocked && p.ExcludedLocation != label)))
            .Select(t => new
            {
                t.Id,
                t.ReferenceNumber,
                Since = t.PostedAt ?? t.CreatedAt,
                WarehouseCode = db.Warehouses.Where(w => w.Id == t.WarehouseId).Select(w => w.Code).FirstOrDefault(),
                WaitingCount = t.Lines.SelectMany(l => l.Putaways)
                    .Count(p => !p.IsPickerPlaced && !p.IsExcludedLocked && p.ExcludedLocation == null && (t.PostedAt != null || !p.IsConfirmed)),
                WaitingBoxes = t.Lines.SelectMany(l => l.Putaways)
                    .Where(p => !p.IsPickerPlaced && !p.IsExcludedLocked && p.ExcludedLocation == null && (t.PostedAt != null || !p.IsConfirmed))
                    .Sum(p => (decimal?)p.QuantityBoxes) ?? 0m,
                AwaitingConfirmCount = t.Lines.SelectMany(l => l.Putaways).Count(p => p.IsPickerPlaced && !p.IsExcludedLocked && p.ExcludedLocation != label),
                Reserved = t.Lines.SelectMany(l => l.Putaways).Where(p => p.ExcludedLocation == label).Sum(p => (decimal?)p.QuantityBoxes) ?? 0m
            })
            .ToListAsync(cancellationToken);

        var pulloutQuery = db.PulloutTransactions.AsNoTracking()
            .Where(t => !t.IsRejected && t.IsConfirmed && t.ConfirmedAt == null && t.UploadHash != null);
        if (request.WarehouseId is { } wh2)
        {
            pulloutQuery = pulloutQuery.Where(t => t.WarehouseId == wh2);
        }

        var pullouts = await pulloutQuery
            .Select(t => new
            {
                t.Id,
                t.ReferenceNumber,
                t.CreatedAt,
                WarehouseCode = db.Warehouses.Where(w => w.Id == t.WarehouseId).Select(w => w.Code).FirstOrDefault(),
                Unsubmitted = t.Lines.SelectMany(l => l.Picks).Count(p => p.PickedAt == null),
                UnsubmittedBoxes = t.Lines.SelectMany(l => l.Picks).Where(p => p.PickedAt == null).Sum(p => (decimal?)p.QuantityBoxes) ?? 0m,
                SubmittedCount = t.Lines.SelectMany(l => l.Picks).Count(p => p.PickedAt != null)
            })
            .ToListAsync(cancellationToken);

        var rows = new List<PendingConfirmationRowDto>();
        foreach (var g in grns.Where(g => g.WaitingCount > 0 || g.AwaitingConfirmCount > 0))
        {
            var age = (now - g.Since).TotalHours;
            rows.Add(new PendingConfirmationRowDto(
                "GRN", g.Id, g.ReferenceNumber, g.Since, age, age >= overdueHours, g.WarehouseCode ?? "-",
                g.WaitingCount, g.WaitingBoxes, g.AwaitingConfirmCount, g.Reserved,
                g.WaitingCount > 0
                    ? $"{g.WaitingCount} location(s) not placed - system stock is ahead of the shelf"
                    : "All locations placed - picker must press Confirm"));
        }

        foreach (var p in pullouts)
        {
            var age = (now - p.CreatedAt).TotalHours;
            rows.Add(new PendingConfirmationRowDto(
                "Pullout", p.Id, p.ReferenceNumber, p.CreatedAt, age, age >= overdueHours, p.WarehouseCode ?? "-",
                p.Unsubmitted, p.UnsubmittedBoxes, 0, 0m,
                p.Unsubmitted > 0
                    ? $"{p.Unsubmitted} pick(s) not picked - system stock is already deducted"
                    : "All picks submitted - picker must press Confirm"));
        }

        rows = rows.OrderByDescending(r => r.AgeHours).ToList();
        return new PendingConfirmationReportDto(
            rows,
            overdueHours,
            rows.Count(r => r.IsOverdue),
            rows.Where(r => r.Kind == "GRN").Sum(r => r.WaitingBoxes),
            rows.Where(r => r.Kind == "Pullout").Sum(r => r.WaitingBoxes));
    }
}
