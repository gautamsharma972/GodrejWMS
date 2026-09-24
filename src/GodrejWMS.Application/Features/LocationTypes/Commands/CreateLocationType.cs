using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.LocationTypes.Commands;

public sealed record CreateLocationTypeCommand(string Code, string DisplayName, string? Description, int SortOrder) : IRequest<int>;

public sealed class CreateLocationTypeValidator : AbstractValidator<CreateLocationTypeCommand>
{
    public CreateLocationTypeValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(20)
            .MustAsync(async (code, ct) =>
                !await db.LocationTypes.AnyAsync(l => l.Code == code.Trim(), ct))
            .WithMessage("A location type with this code already exists.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateLocationTypeHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<CreateLocationTypeCommand, int>
{
    public async Task<int> Handle(CreateLocationTypeCommand request, CancellationToken cancellationToken)
    {
        var locationType = new LocationType
        {
            Code = request.Code.Trim(),
            DisplayName = request.DisplayName.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = currentUser.UserId
        };

        db.LocationTypes.Add(locationType);
        await db.SaveChangesAsync(cancellationToken);

        return locationType.Id;
    }
}
