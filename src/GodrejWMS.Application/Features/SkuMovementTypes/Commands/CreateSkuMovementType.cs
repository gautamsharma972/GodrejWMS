using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.SkuMovementTypes.Commands;

public sealed record CreateSkuMovementTypeCommand(string Code, string DisplayName, string? Description, int SortOrder) : IRequest<int>;

public sealed class CreateSkuMovementTypeValidator : AbstractValidator<CreateSkuMovementTypeCommand>
{
    public CreateSkuMovementTypeValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(20)
            .MustAsync(async (code, ct) =>
                !await db.SkuMovementTypes.AnyAsync(s => s.Code == code.Trim(), ct))
            .WithMessage("A SKU movement type with this code already exists.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateSkuMovementTypeHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<CreateSkuMovementTypeCommand, int>
{
    public async Task<int> Handle(CreateSkuMovementTypeCommand request, CancellationToken cancellationToken)
    {
        var movementType = new SkuMovementType
        {
            Code = request.Code.Trim(),
            DisplayName = request.DisplayName.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = currentUser.UserId
        };

        db.SkuMovementTypes.Add(movementType);
        await db.SaveChangesAsync(cancellationToken);

        return movementType.Id;
    }
}
