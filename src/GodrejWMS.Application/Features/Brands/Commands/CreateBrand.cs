using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Brands.Commands;

public sealed record CreateBrandCommand(string Code, string? Description) : IRequest<int>;

public sealed class CreateBrandValidator : AbstractValidator<CreateBrandCommand>
{
    public CreateBrandValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50)
            .MustAsync(async (code, ct) => !await db.Brands.AnyAsync(x => x.Code == code.Trim(), ct))
            .WithMessage("A brand with this code already exists.");
        RuleFor(x => x.Description).MaximumLength(200);
    }
}

public sealed class CreateBrandHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<CreateBrandCommand, int>
{
    public async Task<int> Handle(CreateBrandCommand request, CancellationToken cancellationToken)
    {
        var entity = new Brand { Code = request.Code.Trim(), Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(), IsActive = true, CreatedAt = clock.UtcNow, CreatedByUserId = currentUser.UserId };
        db.Brands.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }
}
