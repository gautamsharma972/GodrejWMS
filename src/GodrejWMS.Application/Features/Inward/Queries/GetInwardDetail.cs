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

        var lines = transaction.Lines
            .OrderBy(l => l.Id)
            .Select(l => new InwardDetailLineDto(
                l.Material.MaterialNumber,
                l.Material.Description,
                l.Material.DesignType,
                MfgMonthParser.Format(l.MfgMonth),
                l.RequestedQuantityBoxes,
                l.AllocatedQuantityBoxes,
                l.Status,
                l.Remarks,
                l.Putaways
                    .OrderBy(p => p.PalletPosition.LocationCode)
                    .Select(p => new InwardPutawayDto(
                        p.PalletPosition.LocationCode, p.QuantityBoxes, p.IsConfirmed, p.Id, p.AllocationReason,
                        p.OverrideReason, p.ConfirmedAt, p.ConfirmedByUserName))
                    .ToList()))
            .ToList();

        return new InwardDetailDto(
            transaction.Id,
            transaction.ReferenceNumber,
            transaction.CreatedAt,
            lines,
            IsConfirmed: transaction.Lines.SelectMany(l => l.Putaways).All(p => p.IsConfirmed),
            IsRejected: transaction.IsRejected,
            RejectedAt: transaction.RejectedAt,
            RejectedByUserName: transaction.RejectedByUserName,
            RejectionReason: transaction.RejectionReason);
    }
}

