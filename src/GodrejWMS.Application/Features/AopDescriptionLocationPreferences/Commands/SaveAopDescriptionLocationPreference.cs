using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.AopDescriptionLocationPreferences.Commands;

public sealed record CreateAopDescriptionLocationPreferenceCommand(
    int AopDescriptionId,
    int FromPalletPositionId,
    int ToPalletPositionId) : IRequest<int>;

public sealed class CreateAopDescriptionLocationPreferenceValidator : AbstractValidator<CreateAopDescriptionLocationPreferenceCommand>
{
    public CreateAopDescriptionLocationPreferenceValidator()
    {
        RuleFor(x => x.AopDescriptionId).GreaterThan(0).WithMessage("Select an active AOP Description.");
        RuleFor(x => x.FromPalletPositionId).GreaterThan(0);
        RuleFor(x => x.ToPalletPositionId).GreaterThan(0);
    }
}

public sealed class CreateAopDescriptionLocationPreferenceHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock,
    ICurrentUserService currentUser)
    : IRequestHandler<CreateAopDescriptionLocationPreferenceCommand, int>
{
    public async Task<int> Handle(CreateAopDescriptionLocationPreferenceCommand request, CancellationToken cancellationToken)
    {
        await AopDescriptionLocationPreferenceRules.ValidateAsync(
            db, request.AopDescriptionId, request.FromPalletPositionId, request.ToPalletPositionId, isActive: true, null, cancellationToken);

        var entity = new AopDescriptionLocationPreference
        {
            AopDescriptionId = request.AopDescriptionId,
            FromPalletPositionId = request.FromPalletPositionId,
            ToPalletPositionId = request.ToPalletPositionId,
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = currentUser.UserId
        };
        db.AopDescriptionLocationPreferences.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }
}

public sealed record UpdateAopDescriptionLocationPreferenceCommand(
    int Id,
    int AopDescriptionId,
    int FromPalletPositionId,
    int ToPalletPositionId,
    bool IsActive) : IRequest;

public sealed class UpdateAopDescriptionLocationPreferenceValidator : AbstractValidator<UpdateAopDescriptionLocationPreferenceCommand>
{
    public UpdateAopDescriptionLocationPreferenceValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.AopDescriptionId).GreaterThan(0).WithMessage("Select an active AOP Description.");
        RuleFor(x => x.FromPalletPositionId).GreaterThan(0);
        RuleFor(x => x.ToPalletPositionId).GreaterThan(0);
    }
}

public sealed class UpdateAopDescriptionLocationPreferenceHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock,
    ICurrentUserService currentUser)
    : IRequestHandler<UpdateAopDescriptionLocationPreferenceCommand>
{
    public async Task Handle(UpdateAopDescriptionLocationPreferenceCommand request, CancellationToken cancellationToken)
    {
        var entity = await db.AopDescriptionLocationPreferences.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(AopDescriptionLocationPreference), request.Id);

        await AopDescriptionLocationPreferenceRules.ValidateAsync(
            db, request.AopDescriptionId, request.FromPalletPositionId, request.ToPalletPositionId, request.IsActive, request.Id, cancellationToken);

        entity.AopDescriptionId = request.AopDescriptionId;
        entity.FromPalletPositionId = request.FromPalletPositionId;
        entity.ToPalletPositionId = request.ToPalletPositionId;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = clock.UtcNow;
        entity.UpdatedByUserId = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record DeleteAopDescriptionLocationPreferenceCommand(int Id) : IRequest;

public sealed class DeleteAopDescriptionLocationPreferenceHandler(IApplicationDbContext db)
    : IRequestHandler<DeleteAopDescriptionLocationPreferenceCommand>
{
    public async Task Handle(DeleteAopDescriptionLocationPreferenceCommand request, CancellationToken cancellationToken)
    {
        var entity = await db.AopDescriptionLocationPreferences.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(AopDescriptionLocationPreference), request.Id);
        db.AopDescriptionLocationPreferences.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal static class AopDescriptionLocationPreferenceRules
{
    public static async Task ValidateAsync(
        IApplicationDbContext db,
        int aopDescriptionId,
        int fromId,
        int toId,
        bool isActive,
        int? excludingId,
        CancellationToken cancellationToken)
    {
        // An existing mapping may keep pointing at an AOP Description that was deactivated later, so
        // the active check only applies when the AOP Description is being chosen or changed.
        var currentAopId = excludingId is null
            ? (int?)null
            : await db.AopDescriptionLocationPreferences.AsNoTracking()
                .Where(x => x.Id == excludingId).Select(x => (int?)x.AopDescriptionId).FirstOrDefaultAsync(cancellationToken);
        var aopOk = await db.AopDescriptions.AnyAsync(
            x => x.Id == aopDescriptionId && (x.IsActive || x.Id == currentAopId), cancellationToken);
        if (!aopOk)
        {
            throw new InvalidOperationException("Select an active AOP Description.");
        }

        var endpoints = await db.PalletPositions.AsNoTracking()
            .Where(x => x.Id == fromId || x.Id == toId)
            .Select(x => new
            {
                x.Id, x.RackId, x.Column, x.Level, x.IsActive,
                RackCode = x.Rack.Code, RackActive = x.Rack.IsActive, x.Rack.WarehouseId
            })
            .ToListAsync(cancellationToken);
        var from = endpoints.FirstOrDefault(x => x.Id == fromId);
        var to = endpoints.FirstOrDefault(x => x.Id == toId);

        if (from is null || to is null || !from.IsActive || !to.IsActive
            || !from.RackActive || !to.RackActive)
        {
            throw new InvalidOperationException("Both location endpoints must be active pallet positions.");
        }
        if (from.RackId != to.RackId)
        {
            throw new InvalidOperationException("Location From and Location To must belong to the same rack. Create a separate mapping for each rack.");
        }
        if (await db.Warehouses.AnyAsync(x => x.Id == from.WarehouseId && !x.IsActive, cancellationToken))
        {
            throw new InvalidOperationException("The rack's warehouse is inactive.");
        }
        if (from.Column > to.Column || from.Level > to.Level)
        {
            throw new InvalidOperationException("Location To must be at or after Location From by both column and level.");
        }

        if (await db.AopDescriptionLocationPreferences.AnyAsync(x =>
            x.Id != excludingId && x.AopDescriptionId == aopDescriptionId
            && x.FromPalletPositionId == fromId && x.ToPalletPositionId == toId, cancellationToken))
        {
            throw new InvalidOperationException("This AOP Description and location range mapping already exists.");
        }

        // Overlap: two ranges collide when their column spans and level spans both intersect on the same
        // rack. Only active mappings count on either side, so a deactivated range never blocks a new one.
        if (isActive)
        {
            var overlap = await db.AopDescriptionLocationPreferences.AsNoTracking()
                .Where(x => x.Id != excludingId && x.IsActive && x.AopDescriptionId == aopDescriptionId
                    && x.FromPalletPosition.RackId == from.RackId
                    && x.FromPalletPosition.Column <= to.Column && x.ToPalletPosition.Column >= from.Column
                    && x.FromPalletPosition.Level <= to.Level && x.ToPalletPosition.Level >= from.Level)
                .Select(x => new { From = x.FromPalletPosition.LocationCode, To = x.ToPalletPosition.LocationCode })
                .FirstOrDefaultAsync(cancellationToken);
            if (overlap is not null)
            {
                throw new InvalidOperationException(
                    $"This range overlaps an existing active mapping for the same AOP Description in rack {from.RackCode} ({overlap.From} to {overlap.To}). Edit or deactivate that mapping first.");
            }
        }
    }
}
