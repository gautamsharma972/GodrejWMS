using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Racks.Commands;

public sealed record UpdateRackCommand(
    int RackId,
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
    int MaxPallets) : IRequest;

public sealed class UpdateRackValidator : AbstractValidator<UpdateRackCommand>
{
    public UpdateRackValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.RackId).GreaterThan(0);
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(10)
            .MustAsync(async (command, code, ct) =>
                !await db.Racks.AnyAsync(r => r.Id != command.RackId && r.Code == code.Trim().ToUpperInvariant(), ct))
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
    }
}

public sealed class UpdateRackHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock,
    ICurrentUserService currentUser)
    : IRequestHandler<UpdateRackCommand>
{
    public async Task Handle(UpdateRackCommand request, CancellationToken cancellationToken)
    {
        var rack = await db.Racks
            .Include(r => r.PalletPositions)
                .ThenInclude(p => p.StockBatches)
            .FirstOrDefaultAsync(r => r.Id == request.RackId, cancellationToken)
            ?? throw new NotFoundException(nameof(Rack), request.RackId);

        var rackCode = request.Code.Trim().ToUpperInvariant();
        var outsideGrid = rack.PalletPositions
            .Where(p => p.Column > request.Columns || p.Level > request.Levels)
            .ToList();

        var occupiedOutsideGrid = outsideGrid
            .Where(p => p.StockBatches.Sum(b => b.QuantityBoxes) > 0)
            .Select(p => p.LocationCode)
            .ToList();

        if (occupiedOutsideGrid.Count > 0)
        {
            throw new InvalidOperationException(
                $"Cannot reduce the rack grid because these locations contain stock: {string.Join(", ", occupiedOutsideGrid)}.");
        }

        db.PalletPositions.RemoveRange(outsideGrid);

        rack.Code = rackCode;
        rack.Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        rack.Columns = request.Columns;
        rack.Levels = request.Levels;
        rack.ShelfLengthMm = request.ShelfLengthMm;
        rack.ShelfWidthMm = request.ShelfWidthMm;
        rack.ShelfHeightMm = request.ShelfHeightMm;
        rack.StartingDistancePriority = request.StartingDistancePriority;

        var existingPositions = rack.PalletPositions
            .Where(p => p.Column <= request.Columns && p.Level <= request.Levels)
            .ToDictionary(p => (p.Column, p.Level));

        for (var level = 1; level <= request.Levels; level++)
        {
            for (var column = 1; column <= request.Columns; column++)
            {
                if (!existingPositions.TryGetValue((column, level), out var position))
                {
                    position = new PalletPosition
                    {
                        Rack = rack,
                        RackId = rack.Id,
                        Column = column,
                        Level = level,
                        IsActive = true,
                        CreatedAt = clock.UtcNow,
                        CreatedByUserId = currentUser.UserId
                    };

                    rack.PalletPositions.Add(position);
                }

                position.LocationCode = PalletPosition.BuildLocationCode(rackCode, column, level);
                position.LocationTypeId = request.LocationTypeId;
                position.LocationSubtypeId = request.LocationSubtypeId;
                position.ZoneTypeId = request.ZoneTypeId;
                position.DistancePriority = ((level - 1) * 10) + (request.StartingDistancePriority + column - 1);
                position.MaxPallets = request.MaxPallets;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
