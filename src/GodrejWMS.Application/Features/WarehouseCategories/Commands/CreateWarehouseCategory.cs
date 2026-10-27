using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.WarehouseCategories.Commands;

public sealed record CreateWarehouseCategoryCommand(string Code, string Name, string? Description) : IRequest<int>;

public sealed class CreateWarehouseCategoryValidator : AbstractValidator<CreateWarehouseCategoryCommand>
{
    public CreateWarehouseCategoryValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .MustAsync(async (code, ct) => !await db.WarehouseCategories.AnyAsync(w => w.Code == code.Trim(), ct))
            .WithMessage("A warehouse category with this code already exists.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(300);
    }
}

public sealed class CreateWarehouseCategoryHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<CreateWarehouseCategoryCommand, int>
{
    public async Task<int> Handle(CreateWarehouseCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = new WarehouseCategory
        {
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = currentUser.UserId
        };
        db.WarehouseCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        return category.Id;
    }
}
