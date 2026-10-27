using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.UserScope.Commands;

/// <summary>Sets (or clears, if <paramref name="WarehouseId"/> is null) the single warehouse a
/// warehouse-scoped user (Office Staff/Head Supervisor/Supervisor) can see. Replaces any prior
/// assignment rather than adding to it, matching "assigned the applicable warehouse" (singular)
/// in the access-scope rules.</summary>
public sealed record AssignUserWarehouseCommand(string UserId, int? WarehouseId) : IRequest;

public sealed class AssignUserWarehouseValidator : AbstractValidator<AssignUserWarehouseCommand>
{
    public AssignUserWarehouseValidator(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        RuleFor(x => x)
            .Must(_ => currentUser.IsPlatformLevel())
            .WithMessage("Only Superadmin can assign warehouse scope.");
        RuleFor(x => x.UserId)
            .NotEmpty()
            .Must(id => id != currentUser.UserId)
            .WithMessage("You cannot change your own scope assignment.");
        RuleFor(x => x.WarehouseId)
            .MustAsync(async (id, ct) => id is null || await db.Warehouses.AnyAsync(w => w.Id == id, ct))
            .WithMessage("Warehouse is invalid.");
    }
}

public sealed class AssignUserWarehouseHandler(IApplicationDbContext db)
    : IRequestHandler<AssignUserWarehouseCommand>
{
    public async Task Handle(AssignUserWarehouseCommand request, CancellationToken cancellationToken)
    {
        var existing = await db.UserWarehouses.Where(w => w.UserId == request.UserId).ToListAsync(cancellationToken);
        db.UserWarehouses.RemoveRange(existing);

        if (request.WarehouseId is { } warehouseId)
        {
            db.UserWarehouses.Add(new UserWarehouse { UserId = request.UserId, WarehouseId = warehouseId });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
