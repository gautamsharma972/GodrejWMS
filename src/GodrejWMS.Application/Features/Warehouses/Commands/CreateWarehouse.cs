using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Warehouses.Commands;

public sealed record CreateWarehouseCommand(
    string Code, string Name, int WarehouseCategoryId, int CityId, string? Address, bool AllowPkmMixing = true) : IRequest<int>;

public sealed class CreateWarehouseValidator : AbstractValidator<CreateWarehouseCommand>
{
    public CreateWarehouseValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32)
            .MustAsync(async (code, ct) => !await db.Warehouses.AnyAsync(w => w.Code == code.Trim(), ct))
            .WithMessage("A warehouse with this code already exists.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.WarehouseCategoryId)
            .MustAsync(async (id, ct) => await db.WarehouseCategories.AnyAsync(c => c.Id == id, ct))
            .WithMessage("Warehouse category is invalid.");
        // Every warehouse must resolve to a valid city -> state -> region (spec 2.5): the City FK
        // alone is sufficient since State/Region are derived through it, not duplicated here.
        RuleFor(x => x.CityId)
            .MustAsync(async (id, ct) => await db.Cities.AnyAsync(c => c.Id == id, ct))
            .WithMessage("City is invalid.");
        // Platform-Admin-configured cap on warehouse count - null means unlimited, the seeded
        // default, so this is a no-op until a limit is explicitly set.
        RuleFor(x => x)
            .MustAsync(async (_, ct) =>
            {
                var maxWarehouses = await db.OrganizationSettings.Select(s => s.MaxWarehouses).SingleOrDefaultAsync(ct);
                if (maxWarehouses is null) return true;
                return await db.Warehouses.CountAsync(ct) < maxWarehouses;
            })
            .WithMessage("The organization's warehouse limit has been reached. Contact your administrator to increase it.");
    }
}

public sealed class CreateWarehouseHandler(IApplicationDbContext db)
    : IRequestHandler<CreateWarehouseCommand, int>
{
    public async Task<int> Handle(CreateWarehouseCommand request, CancellationToken cancellationToken)
    {
        var warehouse = new Warehouse
        {
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            WarehouseCategoryId = request.WarehouseCategoryId,
            CityId = request.CityId,
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            AllowPkmMixing = request.AllowPkmMixing,
            IsActive = true
        };
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync(cancellationToken);
        return warehouse.Id;
    }
}
