using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.States.Commands;

public sealed record CreateStateCommand(string Code, string Name, int RegionId) : IRequest<int>;

public sealed class CreateStateValidator : AbstractValidator<CreateStateCommand>
{
    public CreateStateValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .MustAsync(async (code, ct) => !await db.States.AnyAsync(s => s.Code == code.Trim(), ct))
            .WithMessage("A state with this code already exists.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RegionId)
            .MustAsync(async (id, ct) => await db.Regions.AnyAsync(r => r.Id == id, ct))
            .WithMessage("Region is invalid.");
    }
}

public sealed class CreateStateHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<CreateStateCommand, int>
{
    public async Task<int> Handle(CreateStateCommand request, CancellationToken cancellationToken)
    {
        var state = new State
        {
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            RegionId = request.RegionId,
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = currentUser.UserId
        };
        db.States.Add(state);
        await db.SaveChangesAsync(cancellationToken);
        return state.Id;
    }
}
