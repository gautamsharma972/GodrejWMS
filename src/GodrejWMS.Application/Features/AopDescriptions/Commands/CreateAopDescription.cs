using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.AopDescriptions.Commands;

public sealed record CreateAopDescriptionCommand(string Code, string? Description) : IRequest<int>;

public sealed class CreateAopDescriptionValidator : AbstractValidator<CreateAopDescriptionCommand>
{
    public CreateAopDescriptionValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50)
            .MustAsync(async (code, ct) => !await db.AopDescriptions.AnyAsync(x => x.Code == code.Trim(), ct))
            .WithMessage("An AOP description with this code already exists.");
        RuleFor(x => x.Description).MaximumLength(200);
    }
}

public sealed class CreateAopDescriptionHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<CreateAopDescriptionCommand, int>
{
    public async Task<int> Handle(CreateAopDescriptionCommand request, CancellationToken cancellationToken)
    {
        var entity = new AopDescription { Code = request.Code.Trim(), Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(), IsActive = true, CreatedAt = clock.UtcNow, CreatedByUserId = currentUser.UserId };
        db.AopDescriptions.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }
}
