using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Racks.Commands;

/// <summary>Updates location master fields for a pallet position/location.</summary>
public sealed record UpdatePalletPositionLabelCommand(
    int PalletPositionId,
    string? FlatLabel,
    int LocationTypeId,
    int LocationSubtypeId,
    int ZoneTypeId,
    int DistancePriority,
    int MaxPallets,
    bool IsActive) : IRequest;

public sealed class UpdatePalletPositionLabelValidator : AbstractValidator<UpdatePalletPositionLabelCommand>
{
    public UpdatePalletPositionLabelValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.PalletPositionId).GreaterThan(0);
        RuleFor(x => x.FlatLabel).MaximumLength(20);
        RuleFor(x => x.LocationTypeId)
            .MustAsync(async (id, ct) => await db.LocationTypes.AnyAsync(l => l.Id == id, ct))
            .WithMessage("Location type is invalid.");
        RuleFor(x => x.LocationSubtypeId)
            .MustAsync(async (id, ct) => await db.LocationSubtypes.AnyAsync(s => s.Id == id, ct))
            .WithMessage("Location subtype is invalid.");
        RuleFor(x => x.ZoneTypeId)
            .MustAsync(async (id, ct) => await db.ZoneTypes.AnyAsync(z => z.Id == id, ct))
            .WithMessage("Zone type is invalid.");
        RuleFor(x => x.DistancePriority).InclusiveBetween(1, 9999);
        RuleFor(x => x.MaxPallets).InclusiveBetween(1, 10);
    }
}

public sealed class UpdatePalletPositionLabelHandler(IApplicationDbContext db)
    : IRequestHandler<UpdatePalletPositionLabelCommand>
{
    public async Task Handle(UpdatePalletPositionLabelCommand request, CancellationToken cancellationToken)
    {
        var position = await db.PalletPositions.FindAsync([request.PalletPositionId], cancellationToken)
            ?? throw new NotFoundException(nameof(PalletPosition), request.PalletPositionId);

        position.FlatLabel = string.IsNullOrWhiteSpace(request.FlatLabel) ? null : request.FlatLabel.Trim();
        position.LocationTypeId = request.LocationTypeId;
        position.LocationSubtypeId = request.LocationSubtypeId;
        position.ZoneTypeId = request.ZoneTypeId;
        position.DistancePriority = request.DistancePriority;
        position.MaxPallets = request.MaxPallets;
        position.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);
    }
}
