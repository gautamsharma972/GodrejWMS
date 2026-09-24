using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.DesignTypes.Commands;

public sealed record UpdateDesignTypeCommand(int Id, string Code, string? Description, bool IsActive) : IRequest;

public sealed class UpdateDesignTypeValidator : AbstractValidator<UpdateDesignTypeCommand>
{
    public UpdateDesignTypeValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(20)
            .MustAsync(async (command, code, ct) =>
                !await db.DesignTypes.AnyAsync(d => d.Id != command.Id && d.Code == code.Trim(), ct))
            .WithMessage("A design type with this code already exists.");
        RuleFor(x => x.Description).MaximumLength(200);
    }
}

public sealed class UpdateDesignTypeHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<UpdateDesignTypeCommand>
{
    public async Task Handle(UpdateDesignTypeCommand request, CancellationToken cancellationToken)
    {
        var designType = await db.DesignTypes.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(DesignType), request.Id);

        var previousCode = designType.Code;
        var newCode = request.Code.Trim();

        designType.Code = newCode;
        designType.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        designType.IsActive = request.IsActive;
        designType.UpdatedAt = clock.UtcNow;
        designType.UpdatedByUserId = currentUser.UserId;

        if (!string.Equals(previousCode, newCode, StringComparison.Ordinal))
        {
            var materials = await db.Materials
                .Where(m => m.DesignType == previousCode)
                .ToListAsync(cancellationToken);

            foreach (var material in materials)
            {
                material.DesignType = newCode;
                material.UpdatedAt = clock.UtcNow;
                material.UpdatedByUserId = currentUser.UserId;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
