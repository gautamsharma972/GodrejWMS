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

        if (!transaction.IsConfirmed && transaction.PreviewJson is not null)
        {
            var preview = System.Text.Json.JsonSerializer.Deserialize<PulloutResultDto>(transaction.PreviewJson)!;
            return new PulloutDetailDto(transaction.Id, transaction.ReferenceNumber, transaction.CreatedAt,
                preview.Lines.Select(l => new PulloutDetailLineDto(l.MaterialNumber, l.MaterialDescription,
                    l.RequestedQuantityBoxes, l.PickedQuantityBoxes, l.Status, l.Remarks, l.Picks)).ToList(), false);
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
                l.Picks
                    .OrderBy(p => p.MfgMonth)
                    .ThenBy(p => p.PalletPosition.LocationCode)
                    .Select(p => new PulloutPickDto(
                        p.PalletPosition.LocationCode,
                        MfgMonthParser.Format(p.MfgMonth),
                        p.QuantityBoxes))
                    .ToList()))
            .ToList();

        return new PulloutDetailDto(transaction.Id, transaction.ReferenceNumber, transaction.CreatedAt, lines, transaction.IsConfirmed);
    }
}
