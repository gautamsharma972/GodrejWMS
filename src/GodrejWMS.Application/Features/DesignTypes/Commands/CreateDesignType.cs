using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.DesignTypes.Commands;

public sealed record CreateDesignTypeCommand(string Code, string? Description) : IRequest<int>;

public sealed class CreateDesignTypeValidator : AbstractValidator<CreateDesignTypeCommand>
{
    public CreateDesignTypeValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(20)
            .MustAsync(async (code, ct) =>
                !await db.DesignTypes.AnyAsync(d => d.Code == code.Trim(), ct))
            .WithMessage("A design type with this code already exists.");
        RuleFor(x => x.Description).MaximumLength(200);
    }
}

public sealed class CreateDesignTypeHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<CreateDesignTypeCommand, int>
{
    public async Task<int> Handle(CreateDesignTypeCommand request, CancellationToken cancellationToken)
    {
        var designType = new DesignType
        {
            Code = request.Code.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = currentUser.UserId
        };

        db.DesignTypes.Add(designType);
        await db.SaveChangesAsync(cancellationToken);

        return designType.Id;
    }
}
