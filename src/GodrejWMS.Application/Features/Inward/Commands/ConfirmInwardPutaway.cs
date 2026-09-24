using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Commands;

public sealed record ConfirmInwardPutawayCommand(string ReferenceNumber) : IRequest<InwardResultDto>;

public sealed class ConfirmInwardPutawayHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock,
    ICurrentUserService currentUser) : IRequestHandler<ConfirmInwardPutawayCommand, InwardResultDto>
{
    public async Task<InwardResultDto> Handle(ConfirmInwardPutawayCommand request, CancellationToken cancellationToken)
    {
        var reference = request.ReferenceNumber.Trim();
        var transaction = await db.InwardTransactions
            .Include(t => t.Lines)
                .ThenInclude(l => l.Material)
            .Include(t => t.Lines)
                .ThenInclude(l => l.Putaways)
                    .ThenInclude(p => p.PalletPosition)
            .FirstOrDefaultAsync(t => t.ReferenceNumber == reference, cancellationToken)
            ?? throw new NotFoundException(nameof(InwardTransaction), reference);

        foreach (var line in transaction.Lines)
        {
            foreach (var putaway in line.Putaways.Where(p => !p.IsConfirmed))
            {
                await InwardPutawayConfirmationLogic.ConfirmAsync(db, line, putaway, clock, currentUser, cancellationToken);
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Catches both a stale RowVersion on an existing StockBatch and a duplicate-key
            // violation on its (Material, MfgMonth, Position) unique index when two confirmations
            // race to create the same brand-new batch row - the second insert fails at the
            // database level, not via a version mismatch, so DbUpdateConcurrencyException alone
            // would miss it and leak a raw SQL error instead.
            throw new InvalidOperationException(
                "This put-away was just confirmed or changed by another user. Please refresh and try again.");
        }

        return InwardResultMapper.ToResult(transaction);
    }
}