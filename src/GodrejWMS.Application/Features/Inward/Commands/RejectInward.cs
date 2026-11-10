using GodrejWMS.Application.Common.Permissions;
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
[RequiresPermission(PermissionKeys.Inward.Reject)]
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
    IStockPostingService posting,
    IUnitOfWork uow,
    IDateTimeProvider clock,
    ICurrentUserService currentUser) : IRequestHandler<RejectInwardCommand, InwardResultDto>
{
    public Task<InwardResultDto> Handle(RejectInwardCommand request, CancellationToken cancellationToken) =>
        uow.ExecuteInTransactionAsync(ct => RunAsync(request, ct), cancellationToken);

    private async Task<InwardResultDto> RunAsync(RejectInwardCommand request, CancellationToken cancellationToken)
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

        // Stock posted on upload is taken back out where it was placed (a picker's relocation is followed). That only
        // works while it is still there: boxes that were picked or moved elsewhere cannot be reversed by a rejection.
        var posted = transaction.Lines
            .SelectMany(l => l.Putaways.Where(p => p.IsConfirmed && p.ExcludedLocation is null).Select(p => (Line: l, Putaway: p)))
            .Where(x => x.Line.MfgMonth is not null)
            .ToList();
        if (posted.Count > 0)
        {
            try
            {
                await posting.ApplyAsync(
                    new StockPostingContext(StockLedgerEntryTypes.Reversal, "GRN", transaction.ReferenceNumber, "GRN rejected"),
                    posted.Select(x => new StockChange(
                        x.Line.MaterialId, x.Line.MfgMonth!.Value, LocationSubtypeIds.Good, x.Putaway.PalletPosition, -x.Putaway.QuantityBoxes)).ToList(),
                    cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    $"This GRN can't be rejected because its stock is no longer where it was placed ({ex.Message}).");
            }
        }

        // Open (unposted) reservations are simply released so their capacity is free for other allocations. The line-level
        // requested/allocated quantities stay on InwardTransactionLine as the audit record of what was rejected.
        foreach (var line in transaction.Lines)
        {
            foreach (var putaway in line.Putaways.Where(p => !p.IsConfirmed).ToList())
            {
                db.InwardPutaways.Remove(putaway);
            }
        }

        // Frees the upload fingerprint, so a corrected file can be uploaded again.
        transaction.UploadHash = null;

        transaction.IsRejected = true;
        transaction.RejectedAt = clock.UtcNow;
        transaction.RejectedByUserId = currentUser.UserId;
        transaction.RejectedByUserName = currentUser.UserName;
        transaction.RejectionReason = request.Reason.Trim();

        await db.SaveChangesAsync(cancellationToken);

        return InwardResultMapper.ToResult(transaction);
    }
}
