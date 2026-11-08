using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Models;
using GodrejWMS.Application.Common.Permissions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Pullout.Queries;

public sealed record PickerPulloutDto(
    int Id,
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    string? Destination,
    string? VehicleNumber,
    int LineCount,
    int PickCount,
    int PickedCount,
    decimal TotalBoxes,
    int VirtualPickCount);

/// <summary>The Picker's pick-lists: posted pullouts the picker has not confirmed yet. A pullout leaves the list once every pick is
/// confirmed and the picker has confirmed the pick-list itself; a rejected one never shows.</summary>
public sealed record GetPickerPendingPulloutsQuery(string? Search = null, int PageNumber = 1, int PageSize = 10)
    : IRequest<PaginatedList<PickerPulloutDto>>;

public sealed class GetPickerPendingPulloutsHandler(IApplicationDbContext db, IPermissionService permissions)
    : IRequestHandler<GetPickerPendingPulloutsQuery, PaginatedList<PickerPulloutDto>>
{
    public async Task<PaginatedList<PickerPulloutDto>> Handle(GetPickerPendingPulloutsQuery request, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Pullout.Pick, cancellationToken))
        {
            throw new UnauthorizedAccessException("You do not have permission to work pick-lists.");
        }

        var transactions = db.PulloutTransactions.AsNoTracking()
            // A posted pullout stays here until the picker has pressed Confirm on it (every pick confirmed first).
            .Where(t => t.IsConfirmed && !t.IsRejected && t.UploadHash != null && t.ConfirmedAt == null);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            transactions = transactions.Where(t =>
                t.ReferenceNumber.Contains(term) ||
                (t.VehicleNumber != null && t.VehicleNumber.Contains(term)) ||
                (t.ClientIndentNumber != null && t.ClientIndentNumber.Contains(term)) ||
                t.Lines.Any(l => (l.PlantName != null && l.PlantName.Contains(term))
                    || l.Material.MaterialNumber.ToString().Contains(term) || l.Material.Description.Contains(term)));
        }

        var query = transactions
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new PickerPulloutDto(
                t.Id,
                t.ReferenceNumber,
                t.CreatedAt,
                t.Lines.Select(l => l.PlantName).FirstOrDefault(n => n != null),
                t.VehicleNumber,
                t.Lines.Count,
                t.Lines.SelectMany(l => l.Picks).Count(),
                t.Lines.SelectMany(l => l.Picks).Count(p => p.PickedAt != null),
                t.Lines.SelectMany(l => l.Picks).Sum(p => (decimal?)p.QuantityBoxes) ?? 0m,
                t.Lines.SelectMany(l => l.Picks).Count(p => p.PalletPosition.IsVirtual)));

        return await PaginatedList<PickerPulloutDto>.CreateAsync(query, request.PageNumber, request.PageSize);
    }
}
