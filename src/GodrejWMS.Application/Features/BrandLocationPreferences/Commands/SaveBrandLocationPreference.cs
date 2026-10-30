using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.BrandLocationPreferences.Commands;

public sealed record CreateBrandLocationPreferenceCommand(
    int BrandId,
    int FromPalletPositionId,
    int ToPalletPositionId) : IRequest<int>;

public sealed class CreateBrandLocationPreferenceValidator : AbstractValidator<CreateBrandLocationPreferenceCommand>
{
    public CreateBrandLocationPreferenceValidator()
    {
        RuleFor(x => x.BrandId).GreaterThan(0);
        RuleFor(x => x.FromPalletPositionId).GreaterThan(0);
        RuleFor(x => x.ToPalletPositionId).GreaterThan(0);
    }
}

public sealed class CreateBrandLocationPreferenceHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock,
    ICurrentUserService currentUser)
    : IRequestHandler<CreateBrandLocationPreferenceCommand, int>
{
    public async Task<int> Handle(CreateBrandLocationPreferenceCommand request, CancellationToken cancellationToken)
    {
        await BrandLocationPreferenceRules.ValidateAsync(
            db, request.BrandId, request.FromPalletPositionId, request.ToPalletPositionId, null, cancellationToken);

        var entity = new BrandLocationPreference
        {
            BrandId = request.BrandId,
            FromPalletPositionId = request.FromPalletPositionId,
            ToPalletPositionId = request.ToPalletPositionId,
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = currentUser.UserId
        };
        db.BrandLocationPreferences.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }
}

public sealed record UpdateBrandLocationPreferenceCommand(
    int Id,
    int BrandId,
    int FromPalletPositionId,
    int ToPalletPositionId,
    bool IsActive) : IRequest;

public sealed class UpdateBrandLocationPreferenceValidator : AbstractValidator<UpdateBrandLocationPreferenceCommand>
{
    public UpdateBrandLocationPreferenceValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.BrandId).GreaterThan(0);
        RuleFor(x => x.FromPalletPositionId).GreaterThan(0);
        RuleFor(x => x.ToPalletPositionId).GreaterThan(0);
    }
}

public sealed class UpdateBrandLocationPreferenceHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock,
    ICurrentUserService currentUser)
    : IRequestHandler<UpdateBrandLocationPreferenceCommand>
{
    public async Task Handle(UpdateBrandLocationPreferenceCommand request, CancellationToken cancellationToken)
    {
        var entity = await db.BrandLocationPreferences.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(BrandLocationPreference), request.Id);

        await BrandLocationPreferenceRules.ValidateAsync(
            db, request.BrandId, request.FromPalletPositionId, request.ToPalletPositionId, request.Id, cancellationToken);

        entity.BrandId = request.BrandId;
        entity.FromPalletPositionId = request.FromPalletPositionId;
        entity.ToPalletPositionId = request.ToPalletPositionId;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = clock.UtcNow;
        entity.UpdatedByUserId = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record DeleteBrandLocationPreferenceCommand(int Id) : IRequest;

public sealed class DeleteBrandLocationPreferenceHandler(IApplicationDbContext db)
    : IRequestHandler<DeleteBrandLocationPreferenceCommand>
{
    public async Task Handle(DeleteBrandLocationPreferenceCommand request, CancellationToken cancellationToken)
    {
        var entity = await db.BrandLocationPreferences.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(BrandLocationPreference), request.Id);
        db.BrandLocationPreferences.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal static class BrandLocationPreferenceRules
{
    public static async Task ValidateAsync(
        IApplicationDbContext db,
        int brandId,
        int fromId,
        int toId,
        int? excludingId,
        CancellationToken cancellationToken)
    {
        if (!await db.Brands.AnyAsync(x => x.Id == brandId && x.IsActive, cancellationToken))
        {
            throw new InvalidOperationException("Select an active Brand Code.");
        }

        var endpoints = await db.PalletPositions.AsNoTracking()
            .Where(x => x.Id == fromId || x.Id == toId)
            .Select(x => new { x.Id, x.RackId, x.Column, x.Level, x.IsActive, RackActive = x.Rack.IsActive })
            .ToListAsync(cancellationToken);
        var from = endpoints.FirstOrDefault(x => x.Id == fromId);
        var to = endpoints.FirstOrDefault(x => x.Id == toId);

        if (from is null || to is null || !from.IsActive || !to.IsActive || !from.RackActive || !to.RackActive)
        {
            throw new InvalidOperationException("Both location endpoints must be active pallet positions.");
        }
        if (from.RackId != to.RackId)
        {
            throw new InvalidOperationException("Location From and Location To must belong to the same rack. Create a separate mapping for each rack.");
        }
        if (from.Column > to.Column || from.Level > to.Level)
        {
            throw new InvalidOperationException("Location To must be at or after Location From by both column and level.");
        }

        if (await db.BrandLocationPreferences.AnyAsync(x =>
            x.Id != excludingId && x.BrandId == brandId
            && x.FromPalletPositionId == fromId && x.ToPalletPositionId == toId, cancellationToken))
        {
            throw new InvalidOperationException("This Brand and location range mapping already exists.");
        }
    }
}
