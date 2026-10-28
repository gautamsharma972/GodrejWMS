using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Warehouses.Commands;

public sealed record UpdateWarehouseCommand(
    int Id, string Code, string Name, int WarehouseCategoryId, int CityId, string? Address,
    bool AllowPkmMixing, bool IsActive) : IRequest;

public sealed class UpdateWarehouseValidator : AbstractValidator<UpdateWarehouseCommand>
{
    public UpdateWarehouseValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32)
            .MustAsync(async (command, code, ct) =>
                !await db.Warehouses.AnyAsync(w => w.Id != command.Id && w.Code == code.Trim(), ct))
            .WithMessage("A warehouse with this code already exists.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.WarehouseCategoryId)
            .MustAsync(async (id, ct) => await db.WarehouseCategories.AnyAsync(c => c.Id == id, ct))
            .WithMessage("Warehouse category is invalid.");
        RuleFor(x => x.CityId)
            .MustAsync(async (id, ct) => await db.Cities.AnyAsync(c => c.Id == id, ct))
            .WithMessage("City is invalid.");
    }
}

public sealed class UpdateWarehouseHandler(IApplicationDbContext db)
    : IRequestHandler<UpdateWarehouseCommand>
{
    public async Task Handle(UpdateWarehouseCommand request, CancellationToken cancellationToken)
    {
        var warehouse = await db.Warehouses.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(Warehouse), request.Id);

        warehouse.Code = request.Code.Trim();
        warehouse.Name = request.Name.Trim();
        warehouse.WarehouseCategoryId = request.WarehouseCategoryId;
        warehouse.CityId = request.CityId;
        warehouse.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        warehouse.AllowPkmMixing = request.AllowPkmMixing;
        warehouse.IsActive = request.IsActive;

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Updates only the inward PKM-mixing policy from Warehouse Master's quick switch.</summary>
public sealed record UpdateWarehousePkmMixingCommand(int Id, bool AllowPkmMixing) : IRequest;

public sealed class UpdateWarehousePkmMixingHandler(IApplicationDbContext db)
    : IRequestHandler<UpdateWarehousePkmMixingCommand>
{
    public async Task Handle(UpdateWarehousePkmMixingCommand request, CancellationToken cancellationToken)
    {
        var warehouse = await db.Warehouses.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(Warehouse), request.Id);

        warehouse.AllowPkmMixing = request.AllowPkmMixing;
        await db.SaveChangesAsync(cancellationToken);
    }
}
