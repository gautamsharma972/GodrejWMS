using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Pullout.Dtos;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Pullout.Queries;

public sealed record GetPulloutDetailQuery(int Id) : IRequest<PulloutDetailDto>;

public sealed class GetPulloutDetailHandler(IApplicationDbContext db)
    : IRequestHandler<GetPulloutDetailQuery, PulloutDetailDto>
{
    public async Task<PulloutDetailDto> Handle(GetPulloutDetailQuery request, CancellationToken cancellationToken)
    {
        var transaction = await db.PulloutTransactions
            .AsNoTracking()
            .Include(t => t.Lines)
                .ThenInclude(l => l.Material)
            .Include(t => t.Lines)
                .ThenInclude(l => l.Picks)
                    .ThenInclude(p => p.PalletPosition)
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(PulloutTransaction), request.Id);

        // Resolved live via a separate query (not Include, which would turn into an INNER JOIN on
        // this required FK and drop the transaction if the warehouse row were ever missing) and
        // not trusted from PreviewJson (older saved previews predate this field).
        var warehouseAddress = await db.Warehouses.AsNoTracking()
            .Where(w => w.Id == transaction.WarehouseId)
            .Select(w => w.Address)
            .FirstOrDefaultAsync(cancellationToken);

        if (!transaction.IsConfirmed && transaction.PreviewJson is not null)
        {
            var preview = System.Text.Json.JsonSerializer.Deserialize<PulloutResultDto>(transaction.PreviewJson)!;
            return new PulloutDetailDto(transaction.Id, transaction.ReferenceNumber, transaction.CreatedAt,
                preview.Lines.Select(l => new PulloutDetailLineDto(l.MaterialNumber, l.MaterialDescription,
                    l.RequestedQuantityBoxes, l.PickedQuantityBoxes, l.Status, l.Remarks, MergePicks(l.Picks), l.PlantName, l.Priority)).ToList(), false,
                transaction.IsRejected, transaction.RejectedAt, transaction.RejectedByUserName, transaction.RejectionReason,
                preview.HeaderFields, warehouseAddress);
        }

        var lines = transaction.Lines
            .OrderBy(l => l.Id)
            .Select(l => new PulloutDetailLineDto(
                l.Material.MaterialNumber,
                l.Material.Description,
                l.RequestedQuantityBoxes,
                l.PickedQuantityBoxes,
                l.Status,
                l.Remarks,
                MergePicks(l.Picks
                    .OrderBy(p => p.MfgMonth)
                    .ThenBy(p => p.PalletPosition.LocationCode)
                    .Select(p => new PulloutPickDto(
                        p.PalletPosition.LocationCode,
                        MfgMonthParser.Format(p.MfgMonth),
                        p.QuantityBoxes,
                        p.OverrideReason))
                    .ToList()),
                l.PlantName,
                l.Priority))
            .ToList();

        var headerFields = new PulloutLoadingSheetFieldsDto(
            transaction.TransportName, transaction.VehicleNumber, transaction.GateNumber, transaction.DriverNumber,
            transaction.PlanTime, transaction.DateText, transaction.StartTime, transaction.EndTime,
            transaction.WeightText, transaction.VolumeText);

        return new PulloutDetailDto(transaction.Id, transaction.ReferenceNumber, transaction.CreatedAt, lines, transaction.IsConfirmed,
            transaction.IsRejected, transaction.RejectedAt, transaction.RejectedByUserName, transaction.RejectionReason,
            headerFields, warehouseAddress);
    }

    private static IReadOnlyList<PulloutPickDto> MergePicks(IEnumerable<PulloutPickDto> picks) =>
        picks
            .GroupBy(pick => new
            {
                Location = pick.LocationCode.ToUpperInvariant(),
                Pkm = pick.MfgMonthLabel.ToUpperInvariant()
            })
            .Select(group => new PulloutPickDto(
                group.First().LocationCode,
                group.First().MfgMonthLabel,
                group.Sum(pick => pick.QuantityBoxes),
                group.Select(pick => pick.OverrideReason).FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason))))
            .OrderBy(pick => pick.MfgMonthLabel)
            .ThenBy(pick => pick.LocationCode)
            .ToList();
}
