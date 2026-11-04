using System.Text.Json;
using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Pullout.Dtos;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Pullout.Commands;

/// <summary>Single-call confirm for API clients that only ever want "confirm this pullout as
/// currently saved" - <see cref="SubmitPulloutCommand"/>'s confirm step requires the caller to
/// echo back the exact <see cref="PulloutResultDto"/> it last returned (so a stale client can't
/// silently confirm picks it never saw); reconstructing that object correctly is easy to get
/// subtly wrong from a thin HTTP client. This loads the transaction's own saved preview and feeds
/// it back to <see cref="SubmitPulloutCommand"/> itself, so the two always match by construction.
/// Added for GodrejWMS.PickersApi's Floor Supervisor endpoints; nothing existing changes.</summary>
public sealed record ConfirmPulloutCommand(string ReferenceNumber) : IRequest<PulloutResultDto>;

public sealed class ConfirmPulloutValidator : AbstractValidator<ConfirmPulloutCommand>
{
    public ConfirmPulloutValidator()
    {
        RuleFor(x => x.ReferenceNumber).NotEmpty();
    }
}

public sealed class ConfirmPulloutHandler(IApplicationDbContext db, IPermissionService permissions, IMediator mediator)
    : IRequestHandler<ConfirmPulloutCommand, PulloutResultDto>
{
    public async Task<PulloutResultDto> Handle(ConfirmPulloutCommand request, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Pullout.Confirm, cancellationToken))
        {
            throw new InvalidOperationException("You do not have permission to confirm pullout transactions.");
        }

        var transaction = await db.PulloutTransactions
            .SingleOrDefaultAsync(t => t.ReferenceNumber == request.ReferenceNumber, cancellationToken)
            ?? throw new NotFoundException(nameof(PulloutTransaction), request.ReferenceNumber);

        if (transaction.PreviewJson is null)
        {
            throw new InvalidOperationException("This pullout has no saved picks to confirm.");
        }

        var savedPreview = JsonSerializer.Deserialize<PulloutResultDto>(transaction.PreviewJson)!;
        var lines = savedPreview.Lines
            .Select(l => new SubmitPulloutLine(l.MaterialNumber, l.RequestedQuantityBoxes, l.PlantName, l.Priority, l.MaterialDescription))
            .ToList();

        return await mediator.Send(new SubmitPulloutCommand(lines, ConfirmedPreview: savedPreview), cancellationToken);
    }
}
