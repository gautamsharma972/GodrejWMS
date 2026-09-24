using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Materials.Commands;

public sealed record CreateMaterialCommand(
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
    int? PreferredZoneTypeId = null,
    bool RequirePreferredZone = false) : IRequest<int>;

public sealed class CreateMaterialValidator : AbstractValidator<CreateMaterialCommand>
{
    public CreateMaterialValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.MaterialNumber)
            .GreaterThan(0)
            .MustAsync(async (materialNumber, ct) =>
                !await db.Materials.AnyAsync(m => m.MaterialNumber == materialNumber, ct))
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

public sealed class CreateMaterialHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<CreateMaterialCommand, int>
{
    public async Task<int> Handle(CreateMaterialCommand request, CancellationToken cancellationToken)
    {
        var material = new Material
        {
            MaterialNumber = request.MaterialNumber,
            Description = request.Description,
            DesignType = request.DesignType,
            CharacteristicValue = request.CharacteristicValue,
            PackSize = request.PackSize,
            MrpPrice = request.MrpPrice,
            LengthMm = request.LengthMm,
            WidthMm = request.WidthMm,
            HeightMm = request.HeightMm,
            NetWeightKg = request.NetWeightKg,
            GrossWeightKg = request.GrossWeightKg,
            PalletCapacityBoxes = request.PalletCapacityBoxes,
            MovementTypeId = request.MovementTypeId,
            SeasonId = request.SeasonId,
            PreferredZoneTypeId = request.PreferredZoneTypeId,
            RequirePreferredZone = request.RequirePreferredZone && request.PreferredZoneTypeId.HasValue,
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = currentUser.UserId
        };
        material.RecalculateDerivedFields();

        db.Materials.Add(material);
        await db.SaveChangesAsync(cancellationToken);

        return material.Id;
    }
}
