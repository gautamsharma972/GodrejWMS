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
                .Where(p => p.RackId != command.RackId && candidateCodes.Contains(p.LocationCode))
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
        var namePrefix = request.Name.Trim();
        var outsideGrid = rack.PalletPositions
            .Where(p => p.Column < request.FromColumn || p.Column > request.ToColumn || p.Level > request.Levels)
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

        // A pending (unconfirmed) reservation still needs a human decision - deactivating a
        // location that an in-flight GRN is about to be put away into would silently strand that
        // workflow. Confirmed stock is the same: never silently deactivate a location that still
        // holds inventory.
        var outsideGridIds = outsideGrid.Select(p => p.Id).ToList();
        var pendingOutsideGrid = await db.InwardPutaways
            .Where(p => !p.IsConfirmed && outsideGridIds.Contains(p.PalletPositionId))
            .Select(p => p.PalletPosition.LocationCode)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (pendingOutsideGrid.Count > 0)
        {
            throw new InvalidOperationException(
                $"Cannot reduce the rack grid because these locations have a pending put-away reservation: {string.Join(", ", pendingOutsideGrid)}. Confirm or re-route those put-aways first.");
        }

        // InwardPutaways.PalletPositionId is a required, ON DELETE RESTRICT foreign key, so a
        // location that was ever put away into - even if its stock was later fully pulled out
        // elsewhere - can never be hard-deleted; the row would orphan that audit history. Such
        // locations are soft-deleted (IsActive = false) instead: excluded from capacity/allocation
        // but kept for the history that still points at them. A location with no put-away history
        // at all is genuinely unused and can be removed outright.
        var historicalOutsideGridIds = (await db.InwardPutaways
                .Where(p => outsideGridIds.Contains(p.PalletPositionId))
                .Select(p => p.PalletPositionId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var toDeactivate = outsideGrid.Where(p => historicalOutsideGridIds.Contains(p.Id)).ToList();
        var toDelete = outsideGrid.Where(p => !historicalOutsideGridIds.Contains(p.Id)).ToList();

        foreach (var position in toDeactivate)
        {
            position.IsActive = false;
        }

        db.PalletPositions.RemoveRange(toDelete);

        rack.Code = rackCode;
        rack.Name = namePrefix;
        rack.FromColumn = request.FromColumn;
        rack.Columns = request.ToColumn - request.FromColumn + 1;
        rack.Levels = request.Levels;
        rack.ShelfLengthMm = request.ShelfLengthMm;
        rack.ShelfWidthMm = request.ShelfWidthMm;
        rack.ShelfHeightMm = request.ShelfHeightMm;
        rack.StartingDistancePriority = request.StartingDistancePriority;

        var existingPositions = rack.PalletPositions
            .Where(p => p.Column >= request.FromColumn && p.Column <= request.ToColumn && p.Level <= request.Levels)
            .ToDictionary(p => (p.Column, p.Level));

        for (var level = 1; level <= request.Levels; level++)
        {
            for (var column = request.FromColumn; column <= request.ToColumn; column++)
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

                // Back in range after an earlier shrink soft-deactivated it (see toDeactivate
                // above) - reactivate it now that it's part of the grid again.
                position.IsActive = true;
                position.LocationCode = PalletPosition.BuildLocationCode(namePrefix, column, level);
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
