using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Commands;

/// <summary>
/// Rejects a GRN that was never (fully) put away - e.g. the physical stock was refused at the
/// dock or the receipt was logged in error. Only possible before any of its put-aways have been
/// confirmed: once a put-away is confirmed it has already written real <see cref="StockBatch"/>
/// inventory, and reversing that is a different problem (a return/reversal transaction), not a
/// rejection of the receipt itself.
/// </summary>
public sealed record RejectInwardCommand(int TransactionId, string Reason) : IRequest<InwardResultDto>;

public sealed class RejectInwardValidator : AbstractValidator<RejectInwardCommand>
{
    public RejectInwardValidator()
    {
        RuleFor(x => x.TransactionId).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class RejectInwardHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock,
    ICurrentUserService currentUser) : IRequestHandler<RejectInwardCommand, InwardResultDto>
{
    public async Task<InwardResultDto> Handle(RejectInwardCommand request, CancellationToken cancellationToken)
    {
        var transaction = await db.InwardTransactions
            .Include(t => t.Lines)
                .ThenInclude(l => l.Material)
            .Include(t => t.Lines)
                .ThenInclude(l => l.Putaways)
                    .ThenInclude(p => p.PalletPosition)
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId, cancellationToken)
            ?? throw new NotFoundException(nameof(InwardTransaction), request.TransactionId);

        if (transaction.IsRejected)
        {
            throw new InvalidOperationException("This GRN has already been rejected.");
        }

        if (transaction.Lines.Any(l => l.Putaways.Any(p => p.IsConfirmed)))
        {
            throw new InvalidOperationException(
                "This GRN can't be rejected because some of its stock has already been confirmed into inventory.");
        }

        // Nothing was confirmed, so no real StockBatch inventory exists yet for this GRN - the
        // only thing "undoing" it needs to do is release the pending location reservations so
        // that capacity is free again for other allocations. The line-level requested/allocated
        // quantities stay on InwardTransactionLine as the audit record of what was rejected.
        foreach (var line in transaction.Lines)
        {
            foreach (var putaway in line.Putaways.ToList())
            {
                db.InwardPutaways.Remove(putaway);
            }
        }

        transaction.IsRejected = true;
        transaction.RejectedAt = clock.UtcNow;
        transaction.RejectedByUserId = currentUser.UserId;
        transaction.RejectedByUserName = currentUser.UserName;
        transaction.RejectionReason = request.Reason.Trim();

        await db.SaveChangesAsync(cancellationToken);

        return InwardResultMapper.ToResult(transaction);
    }
}
