using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Brands.Commands;

public sealed record UpdateBrandCommand(int Id, string Code, string? Description, bool IsActive) : IRequest;

public sealed class UpdateBrandValidator : AbstractValidator<UpdateBrandCommand>
{
    public UpdateBrandValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50)
            .MustAsync(async (command, code, ct) => !await db.Brands.AnyAsync(x => x.Id != command.Id && x.Code == code.Trim(), ct))
            .WithMessage("A brand with this code already exists.");
        RuleFor(x => x.Description).MaximumLength(200);
    }
}

public sealed class UpdateBrandHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser) : IRequestHandler<UpdateBrandCommand>
{
    public async Task Handle(UpdateBrandCommand request, CancellationToken cancellationToken)
    {
        var entity = await db.Brands.FindAsync([request.Id], cancellationToken) ?? throw new NotFoundException(nameof(Brand), request.Id);
        entity.Code = request.Code.Trim();
        entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = clock.UtcNow;
        entity.UpdatedByUserId = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);
    }
}
