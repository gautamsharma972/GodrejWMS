using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.States.Commands;

public sealed record StateAssignment(int Id, string Code, string Name, int RegionId, bool IsActive);

public sealed record UpdateStatesCommand(IReadOnlyList<StateAssignment> Assignments) : IRequest;

public sealed class UpdateStatesValidator : AbstractValidator<UpdateStatesCommand>
{
    public UpdateStatesValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Assignments).Must(a => a.Select(m => m.Id).Distinct().Count() == a.Count)
            .WithMessage("Each state may only appear once.");
        RuleFor(x => x.Assignments).Must(a => a.Select(m => m.Code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == a.Count)
            .WithMessage("State codes must be unique.");
        RuleForEach(x => x.Assignments).ChildRules(a =>
        {
            a.RuleFor(m => m.Code).NotEmpty().MaximumLength(20);
            a.RuleFor(m => m.Name).NotEmpty().MaximumLength(100);
            a.RuleFor(m => m.RegionId)
                .MustAsync(async (id, ct) => await db.Regions.AnyAsync(r => r.Id == id, ct))
                .WithMessage("Region is invalid.");
        });
    }
}

public sealed class UpdateStatesHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<UpdateStatesCommand>
{
    public async Task Handle(UpdateStatesCommand request, CancellationToken cancellationToken)
    {
        var byId = await db.States.ToDictionaryAsync(s => s.Id, cancellationToken);
        foreach (var assignment in request.Assignments)
        {
            if (!byId.TryGetValue(assignment.Id, out var row)) continue;
            row.Code = assignment.Code.Trim();
            row.Name = assignment.Name.Trim();
            row.RegionId = assignment.RegionId;
            row.IsActive = assignment.IsActive;
            row.UpdatedAt = clock.UtcNow;
            row.UpdatedByUserId = currentUser.UserId;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
