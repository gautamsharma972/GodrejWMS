using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.ZoneTypes.Commands;

public sealed record CreateZoneTypeCommand(string Code, string DisplayName, string? Description, int SortOrder) : IRequest<int>;

public sealed class CreateZoneTypeValidator : AbstractValidator<CreateZoneTypeCommand>
{
    public CreateZoneTypeValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(20)
            .MustAsync(async (code, ct) =>
                !await db.ZoneTypes.AnyAsync(z => z.Code == code.Trim(), ct))
            .WithMessage("A zone type with this code already exists.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateZoneTypeHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<CreateZoneTypeCommand, int>
{
    public async Task<int> Handle(CreateZoneTypeCommand request, CancellationToken cancellationToken)
    {
        var zoneType = new ZoneType
        {
            Code = request.Code.Trim(),
            DisplayName = request.DisplayName.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = currentUser.UserId
        };

        db.ZoneTypes.Add(zoneType);
        await db.SaveChangesAsync(cancellationToken);

        return zoneType.Id;
    }
}
