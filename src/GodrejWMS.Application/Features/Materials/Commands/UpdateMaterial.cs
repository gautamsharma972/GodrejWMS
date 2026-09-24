using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Materials.Commands;

public sealed record UpdateMaterialCommand(
    int Id,
    long MaterialNumber,
    string Description,
    string DesignType,
    string? CharacteristicValue,
    int PackSize,
    decimal MrpPrice,
    decimal LengthMm,
    decimal WidthMm,
    decimal HeightMm,
    decimal NetWeightKg,
    decimal GrossWeightKg,
    int PalletCapacityBoxes,
    int MovementTypeId,
    int SeasonId,
    bool IsActive,
    int? PreferredZoneTypeId = null,
    bool RequirePreferredZone = false) : IRequest;

public sealed class UpdateMaterialValidator : AbstractValidator<UpdateMaterialCommand>
{
    public UpdateMaterialValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.MaterialNumber)
            .GreaterThan(0)
            .MustAsync(async (command, materialNumber, ct) =>
                !await db.Materials.AnyAsync(m => m.Id != command.Id && m.MaterialNumber == materialNumber, ct))
            .WithMessage("A material with this Material Number already exists.");
        RuleFor(x => x.Description).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DesignType)
            .NotEmpty()
            .MaximumLength(20)
            .MustAsync(async (code, ct) =>
                await db.DesignTypes.AnyAsync(d => d.Code == code && d.IsActive, ct))
            .WithMessage("Select an active Design Type from the master.");
        RuleFor(x => x.PackSize).GreaterThan(0);
        RuleFor(x => x.MrpPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.LengthMm).GreaterThan(0);
        RuleFor(x => x.WidthMm).GreaterThan(0);
        RuleFor(x => x.HeightMm).GreaterThan(0);
        RuleFor(x => x.NetWeightKg).GreaterThanOrEqualTo(0);
        RuleFor(x => x.GrossWeightKg).GreaterThan(0);
        RuleFor(x => x.PalletCapacityBoxes).GreaterThan(0);
        RuleFor(x => x.MovementTypeId)
            .MustAsync(async (id, ct) => await db.SkuMovementTypes.AnyAsync(s => s.Id == id, ct))
            .WithMessage("SKU movement type is invalid.");
        RuleFor(x => x.SeasonId)
            .MustAsync(async (id, ct) => await db.Seasons.AnyAsync(s => s.Id == id, ct))
            .WithMessage("Season is invalid.");
        RuleFor(x => x.PreferredZoneTypeId)
            .MustAsync(async (id, ct) => await db.ZoneTypes.AnyAsync(z => z.Id == id, ct))
            .When(x => x.PreferredZoneTypeId.HasValue)
            .WithMessage("Preferred zone is invalid.");
    }
}

public sealed class UpdateMaterialHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<UpdateMaterialCommand>
{
    public async Task Handle(UpdateMaterialCommand request, CancellationToken cancellationToken)
    {
        var material = await db.Materials.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(Material), request.Id);

        material.MaterialNumber = request.MaterialNumber;
        material.Description = request.Description;
        material.DesignType = request.DesignType;
        material.CharacteristicValue = request.CharacteristicValue;
        material.PackSize = request.PackSize;
        material.MrpPrice = request.MrpPrice;
        material.LengthMm = request.LengthMm;
        material.WidthMm = request.WidthMm;
        material.HeightMm = request.HeightMm;
        material.NetWeightKg = request.NetWeightKg;
        material.GrossWeightKg = request.GrossWeightKg;
        material.PalletCapacityBoxes = request.PalletCapacityBoxes;
        material.MovementTypeId = request.MovementTypeId;
        material.SeasonId = request.SeasonId;
        material.PreferredZoneTypeId = request.PreferredZoneTypeId;
        material.RequirePreferredZone = request.RequirePreferredZone && request.PreferredZoneTypeId.HasValue;
        material.IsActive = request.IsActive;
        material.RecalculateDerivedFields();
        material.UpdatedAt = clock.UtcNow;
        material.UpdatedByUserId = currentUser.UserId;

        await db.SaveChangesAsync(cancellationToken);
    }
}
