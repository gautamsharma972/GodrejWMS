using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Racks.Commands;

/// <summary>
/// Creates a rack and auto-generates its grid of pallet positions (FromColumn..ToColumn x Levels),
/// each addressed as "{Name}-{Column:00}-{Level:00}" per the workbook's nomenclature sheet. Code
/// stays the unique business key used to select/identify the rack; Name (not required to be
/// unique) drives the location-code prefix, which lets two rack rows share one logical name while
/// covering different, non-overlapping column ranges (e.g. "A" columns 1-5 on one row, "A" columns
/// 6-10 on another). The secondary flat physical label (e.g. "A1") is left blank here — the source
/// workbook shows each rack can follow a different ad-hoc floor-labeling scheme, so it is edited
/// manually per position rather than derived by a fixed formula.
/// </summary>
public sealed record CreateRackCommand(
    string Code,
    string Name,
    int FromColumn,
    int ToColumn,
    int Levels,
    decimal ShelfLengthMm,
    decimal ShelfWidthMm,
    decimal ShelfHeightMm,
    int LocationTypeId,
    int LocationSubtypeId,
    int ZoneTypeId,
    int StartingDistancePriority,
    int MaxPallets,
    int WarehouseId) : IRequest<int>;

public sealed class CreateRackValidator : AbstractValidator<CreateRackCommand>
{
    public CreateRackValidator(IApplicationDbContext db, IWarehouseAccessService warehouseAccess)
    {
        RuleFor(x => x.WarehouseId)
            .MustAsync((id, ct) => warehouseAccess.HasAccessAsync(id, ct))
            .WithMessage("Warehouse is not assigned to you.");
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(10)
            .MustAsync(async (code, ct) => !await db.Racks.AnyAsync(r => r.Code == code, ct))
            .WithMessage("A rack with this code already exists.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        // The absolute column number isn't capped at 50 here - a rack segment can legitimately
        // start well past column 50 (e.g. 101-110). It's the segment's span (ToColumn - FromColumn
        // + 1) that's capped at 50 below, not the raw column numbers.
        RuleFor(x => x.FromColumn).InclusiveBetween(1, 9999);
        RuleFor(x => x.ToColumn).InclusiveBetween(1, 9999);
        RuleFor(x => x)
            .Must(x => x.ToColumn >= x.FromColumn)
            .WithName("ToColumn")
            .WithMessage("To Column must be greater than or equal to From Column.")
            .Must(x => x.ToColumn - x.FromColumn + 1 <= 50)
            .WithName("ToColumn")
            .WithMessage("A rack segment can span at most 50 columns.");
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

        RuleFor(x => x).CustomAsync(async (command, context, ct) =>
        {
            if (string.IsNullOrWhiteSpace(command.Name) || command.ToColumn < command.FromColumn)
            {
                return;
            }

            var namePrefix = command.Name.Trim();
            var candidateCodes = new List<string>();
            for (var level = 1; level <= command.Levels; level++)
            {
                for (var column = command.FromColumn; column <= command.ToColumn; column++)
                {
                    candidateCodes.Add(PalletPosition.BuildLocationCode(namePrefix, column, level));
                }
            }

            var collisions = await db.PalletPositions
                .Where(p => candidateCodes.Contains(p.LocationCode))
                .Select(p => p.LocationCode)
                .ToListAsync(ct);

            if (collisions.Count > 0)
            {
                var preview = string.Join(", ", collisions.OrderBy(c => c).Take(5));
                var suffix = collisions.Count > 5 ? $" and {collisions.Count - 5} more" : string.Empty;
                context.AddFailure("Name",
                    $"These location codes already exist under another rack: {preview}{suffix}. Use a different Name or column range.");
            }
        });
    }
}

public sealed class CreateRackHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<CreateRackCommand, int>
{
    public async Task<int> Handle(CreateRackCommand request, CancellationToken cancellationToken)
    {
        var namePrefix = request.Name.Trim();

        var rack = new Rack
        {
            WarehouseId = request.WarehouseId,
            Code = request.Code.Trim().ToUpperInvariant(),
            Name = namePrefix,
            FromColumn = request.FromColumn,
            Columns = request.ToColumn - request.FromColumn + 1,
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
            for (var column = request.FromColumn; column <= request.ToColumn; column++)
            {
                rack.PalletPositions.Add(new PalletPosition
                {
                    Column = column,
                    Level = level,
                    LocationCode = PalletPosition.BuildLocationCode(namePrefix, column, level),
                    LocationTypeId = request.LocationTypeId,
                    LocationSubtypeId = request.LocationSubtypeId,
                    ZoneTypeId = request.ZoneTypeId,
                    DistancePriority = ((level - 1) * 10) + (request.StartingDistancePriority + column - 1),
                    MaxPallets = request.MaxPallets,
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
