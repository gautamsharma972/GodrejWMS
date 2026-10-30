using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.AopDescriptions.Commands;

public sealed record UpdateAopDescriptionCommand(int Id, string Code, string? Description, bool IsActive) : IRequest;

public sealed class UpdateAopDescriptionValidator : AbstractValidator<UpdateAopDescriptionCommand>
{
    public UpdateAopDescriptionValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50)
            .MustAsync(async (command, code, ct) => !await db.AopDescriptions.AnyAsync(x => x.Id != command.Id && x.Code == code.Trim(), ct))
            .WithMessage("An AOP description with this code already exists.");
        RuleFor(x => x.Description).MaximumLength(200);
    }
}

public sealed class UpdateAopDescriptionHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser) : IRequestHandler<UpdateAopDescriptionCommand>
{
    public async Task Handle(UpdateAopDescriptionCommand request, CancellationToken cancellationToken)
    {
        var entity = await db.AopDescriptions.FindAsync([request.Id], cancellationToken) ?? throw new NotFoundException(nameof(AopDescription), request.Id);
        entity.Code = request.Code.Trim();
        entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = clock.UtcNow;
        entity.UpdatedByUserId = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);
    }
}
