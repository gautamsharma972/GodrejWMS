using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.LocationSubtypes.Commands;

public sealed record CreateLocationSubtypeCommand(string Code, string DisplayName, string? Description, int SortOrder) : IRequest<int>;

public sealed class CreateLocationSubtypeValidator : AbstractValidator<CreateLocationSubtypeCommand>
{
    public CreateLocationSubtypeValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(20)
            .MustAsync(async (code, ct) =>
                !await db.LocationSubtypes.AnyAsync(s => s.Code == code.Trim(), ct))
            .WithMessage("A location subtype with this code already exists.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateLocationSubtypeHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<CreateLocationSubtypeCommand, int>
{
    public async Task<int> Handle(CreateLocationSubtypeCommand request, CancellationToken cancellationToken)
    {
        var subtype = new LocationSubtype
        {
            Code = request.Code.Trim(),
            DisplayName = request.DisplayName.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = currentUser.UserId
        };

        db.LocationSubtypes.Add(subtype);
        await db.SaveChangesAsync(cancellationToken);

        return subtype.Id;
    }
}
