using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Commands;

/// <summary>
/// Confirms one recommended put-away location at a time, for the mobile Putaway Execution screen
/// (spec: supervisor scans/confirms one location, then advances to the next). Unlike
/// <see cref="ConfirmInwardPutawayCommand"/>, this commits a single reservation rather than every
/// pending line in the transaction, so a partially worked GRN correctly leaves the rest pending.
/// </summary>
public sealed record ConfirmSingleInwardPutawayCommand(int PutawayId) : IRequest<InwardResultDto>;

public sealed class ConfirmSingleInwardPutawayHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock,
    ICurrentUserService currentUser) : IRequestHandler<ConfirmSingleInwardPutawayCommand, InwardResultDto>
{
    public async Task<InwardResultDto> Handle(ConfirmSingleInwardPutawayCommand request, CancellationToken cancellationToken)
    {
        var putaway = await db.InwardPutaways
            .Include(p => p.PalletPosition)
            .Include(p => p.InwardTransactionLine)
                .ThenInclude(l => l.Material)
            .FirstOrDefaultAsync(p => p.Id == request.PutawayId, cancellationToken)
            ?? throw new NotFoundException(nameof(InwardPutaway), request.PutawayId);

        if (putaway.IsConfirmed)
        {
            throw new InvalidOperationException("This put-away has already been confirmed.");
        }

        var transactionId = putaway.InwardTransactionLine.InwardTransactionId;

        await InwardPutawayConfirmationLogic.ConfirmAsync(db, putaway.InwardTransactionLine, putaway, clock, currentUser, cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // See ConfirmInwardPutaway.cs's identical catch for why this must be the broader
            // DbUpdateException, not just DbUpdateConcurrencyException.
            throw new InvalidOperationException(
                "This put-away was just confirmed or changed by another user. Please refresh and try again.");
        }

        var transaction = await db.InwardTransactions
            .AsNoTracking()
            .Include(t => t.Lines)
                .ThenInclude(l => l.Material)
            .Include(t => t.Lines)
                .ThenInclude(l => l.Putaways)
                    .ThenInclude(p => p.PalletPosition)
            .FirstAsync(t => t.Id == transactionId, cancellationToken);

        return InwardResultMapper.ToResult(transaction);
    }
}
