using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Commands;

/// <summary>The Picker's step of the "Inward without PKM" flow: records the PKM actually found at
/// each reserved location. One PKM per put-away location - when one material line is spread over
/// locations holding different PKMs, the GRN line is split so each resulting line has exactly one
/// PKM (confirmation and stock batches are keyed by line PKM). Nothing is confirmed here; a
/// Supervisor confirms once every location has a PKM.</summary>
public sealed record AssignInwardPutawayPkmCommand(string ReferenceNumber, IReadOnlyList<PutawayPkmAssignment> Assignments) : IRequest<InwardResultDto>;

public sealed record PutawayPkmAssignment(int PutawayId, string MfgMonthText);

public sealed class AssignInwardPutawayPkmValidator : AbstractValidator<AssignInwardPutawayPkmCommand>
{
    public AssignInwardPutawayPkmValidator()
    {
        RuleFor(x => x.ReferenceNumber).NotEmpty();
        RuleFor(x => x.Assignments).NotEmpty();
        RuleForEach(x => x.Assignments).ChildRules(a =>
            a.RuleFor(x => x.MfgMonthText)
                .Must(text => MfgMonthParser.TryParse(text, out _))
                .WithMessage("PKM must look like 'MAR|2026'."));
    }
}

public sealed class AssignInwardPutawayPkmHandler(IApplicationDbContext db, IPermissionService permissions)
    : IRequestHandler<AssignInwardPutawayPkmCommand, InwardResultDto>
{
    public async Task<InwardResultDto> Handle(AssignInwardPutawayPkmCommand request, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Inward.AssignPkm, cancellationToken))
        {
            throw new InvalidOperationException("You do not have permission to assign PKM.");
        }

        var reference = request.ReferenceNumber.Trim();
        var transaction = await db.InwardTransactions
            .Include(t => t.Lines).ThenInclude(l => l.Material)
            .Include(t => t.Lines).ThenInclude(l => l.Putaways).ThenInclude(p => p.PalletPosition)
            .FirstOrDefaultAsync(t => t.ReferenceNumber == reference, cancellationToken)
            ?? throw new NotFoundException(nameof(InwardTransaction), reference);

        if (!transaction.IsPkmless)
        {
            throw new InvalidOperationException("PKM can only be assigned on a GRN submitted through Inward without PKM.");
        }

        if (transaction.IsRejected)
        {
            throw new InvalidOperationException("This GRN has been rejected.");
        }

        var putawaysById = transaction.Lines.SelectMany(l => l.Putaways).ToDictionary(p => p.Id);
        var pkmByPutaway = new Dictionary<int, int>();
        foreach (var assignment in request.Assignments)
        {
            if (!putawaysById.TryGetValue(assignment.PutawayId, out var putaway))
            {
                throw new InvalidOperationException($"Location row {assignment.PutawayId} does not belong to {reference}.");
            }

            if (putaway.IsConfirmed)
            {
                throw new InvalidOperationException($"Location {putaway.PalletPosition.LocationCode} is already confirmed and its PKM can no longer be changed.");
            }

            MfgMonthParser.TryParse(assignment.MfgMonthText, out var mfgMonth);
            pkmByPutaway[assignment.PutawayId] = mfgMonth;
        }

        foreach (var line in transaction.Lines.ToList())
        {
            var assigned = line.Putaways.Where(p => pkmByPutaway.ContainsKey(p.Id)).ToList();
            if (assigned.Count == 0)
            {
                continue;
            }

            // A line already holding other (unassigned or differently-assigned) putaways keeps its
            // current PKM for those; only the putaways whose PKM differs move to a split-off line.
            var groups = assigned.GroupBy(p => pkmByPutaway[p.Id]).ToList();
            var untouchedCount = line.Putaways.Count - assigned.Count;

            foreach (var group in groups)
            {
                var pkm = group.Key;
                var wholeLineTakesThisPkm = untouchedCount == 0 && groups.Count == 1;
                var firstGroupReusesLine = untouchedCount == 0 && ReferenceEquals(group, groups[0]);

                if (wholeLineTakesThisPkm || firstGroupReusesLine)
                {
                    line.MfgMonth = pkm;
                    continue;
                }

                var moved = group.ToList();
                var movedQuantity = moved.Sum(p => p.QuantityBoxes);
                var split = new InwardTransactionLine
                {
                    InwardTransactionId = transaction.Id,
                    MaterialId = line.MaterialId,
                    Material = line.Material,
                    MfgMonth = pkm,
                    RequestedQuantityBoxes = movedQuantity,
                    AllocatedQuantityBoxes = movedQuantity,
                    Status = Domain.Enums.AllocationStatus.Fulfilled
                };
                transaction.Lines.Add(split);
                foreach (var putaway in moved)
                {
                    line.Putaways.Remove(putaway);
                    split.Putaways.Add(putaway);
                }

                line.RequestedQuantityBoxes -= movedQuantity;
                line.AllocatedQuantityBoxes -= movedQuantity;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return InwardResultMapper.ToResult(transaction);
    }
}
