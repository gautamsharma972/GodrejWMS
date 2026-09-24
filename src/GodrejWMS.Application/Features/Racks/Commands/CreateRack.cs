using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Racks.Commands;

/// <summary>
/// Creates a rack and auto-generates its full grid of pallet positions (Columns x Levels),
/// each addressed as "{Code}-{Column:00}-{Level:00}" per the workbook's nomenclature sheet.
/// The secondary flat physical label (e.g. "A1") is left blank here — the source workbook shows
/// each rack can follow a different ad-hoc floor-labeling scheme, so it is edited manually per
/// position rather than derived by a fixed formula.
/// </summary>
public sealed record CreateRackCommand(
    string Code,
    string? Name,
    int Columns,
    int Levels,
    decimal ShelfLengthMm,
    decimal ShelfWidthMm,
    decimal ShelfHeightMm,
    int LocationTypeId,
    int LocationSubtypeId,
    int ZoneTypeId,
    int StartingDistancePriority,
    int MaxPallets,
    int BoxesPerPallet) : IRequest<int>;

public sealed class CreateRackValidator : AbstractValidator<CreateRackCommand>
{
    public CreateRackValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(10)
            .MustAsync(async (code, ct) => !await db.Racks.AnyAsync(r => r.Code == code, ct))
            .WithMessage("A rack with this code already exists.");
        RuleFor(x => x.Columns).InclusiveBetween(1, 50);
        RuleFor(x => x.Levels).InclusiveBetween(1, 20);
        RuleFor(x => x.ShelfLengthMm).GreaterThan(0);
        RuleFor(x => x.ShelfWidthMm).GreaterThan(0);
        RuleFor(x => x.ShelfHeightMm).GreaterThan(0);
        RuleFor(x => x.LocationTypeId)
            .MustAsync(async (id, ct) => await db.LocationTypes.AnyAsync(l => l.Id == id, ct))
            .WithMessage("Location type is invalid.");
        RuleFor(x => x.LocationSubtypeId)
            .MustAsync(async (id, ct) => await db.LocationSubtypes.AnyAsync(s => s.Id == id, ct))
            .WithMessage("Location subtype is invalid.");
        RuleFor(x => x.ZoneTypeId)
            .MustAsync(async (id, ct) => await db.ZoneTypes.AnyAsync(z => z.Id == id, ct))
            .WithMessage("Zone type is invalid.");
        RuleFor(x => x.StartingDistancePriority).InclusiveBetween(1, 9999);
        RuleFor(x => x.MaxPallets).InclusiveBetween(1, 10);
        RuleFor(x => x.BoxesPerPallet).GreaterThan(0);
    }
}

public sealed class CreateRackHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<CreateRackCommand, int>
{
    public async Task<int> Handle(CreateRackCommand request, CancellationToken cancellationToken)
    {
        var rack = new Rack
        {
            Code = request.Code.Trim().ToUpperInvariant(),
            Name = request.Name,
            Columns = request.Columns,
            Levels = request.Levels,
            ShelfLengthMm = request.ShelfLengthMm,
            ShelfWidthMm = request.ShelfWidthMm,
            ShelfHeightMm = request.ShelfHeightMm,
            StartingDistancePriority = request.StartingDistancePriority,
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = currentUser.UserId
        };

        for (var level = 1; level <= request.Levels; level++)
        {
            for (var column = 1; column <= request.Columns; column++)
            {
                rack.PalletPositions.Add(new PalletPosition
                {
                    Column = column,
                    Level = level,
                    LocationCode = PalletPosition.BuildLocationCode(rack.Code, column, level),
                    LocationTypeId = request.LocationTypeId,
                    LocationSubtypeId = request.LocationSubtypeId,
                    ZoneTypeId = request.ZoneTypeId,
                    DistancePriority = ((level - 1) * 10) + (request.StartingDistancePriority + column - 1),
                    MaxPallets = request.MaxPallets,
                    BoxesPerPallet = request.BoxesPerPallet,
                    CapacityBoxes = request.MaxPallets * request.BoxesPerPallet,
                    IsActive = true,
                    CreatedAt = clock.UtcNow,
                    CreatedByUserId = currentUser.UserId
                });
            }
        }

        db.Racks.Add(rack);
        await db.SaveChangesAsync(cancellationToken);

        return rack.Id;
    }
}
