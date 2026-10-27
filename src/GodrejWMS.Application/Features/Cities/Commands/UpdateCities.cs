using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Cities.Commands;

public sealed record CityAssignment(int Id, string Code, string Name, int StateId, bool IsActive);

public sealed record UpdateCitiesCommand(IReadOnlyList<CityAssignment> Assignments) : IRequest;

public sealed class UpdateCitiesValidator : AbstractValidator<UpdateCitiesCommand>
{
    public UpdateCitiesValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Assignments).Must(a => a.Select(m => m.Id).Distinct().Count() == a.Count)
            .WithMessage("Each city may only appear once.");
        RuleFor(x => x.Assignments).Must(a => a.Select(m => m.Code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == a.Count)
            .WithMessage("City codes must be unique.");
        RuleForEach(x => x.Assignments).ChildRules(a =>
        {
            a.RuleFor(m => m.Code).NotEmpty().MaximumLength(20);
            a.RuleFor(m => m.Name).NotEmpty().MaximumLength(100);
            a.RuleFor(m => m.StateId)
                .MustAsync(async (id, ct) => await db.States.AnyAsync(s => s.Id == id, ct))
                .WithMessage("State is invalid.");
        });
    }
}

public sealed class UpdateCitiesHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<UpdateCitiesCommand>
{
    public async Task Handle(UpdateCitiesCommand request, CancellationToken cancellationToken)
    {
        var byId = await db.Cities.ToDictionaryAsync(c => c.Id, cancellationToken);
        foreach (var assignment in request.Assignments)
        {
            if (!byId.TryGetValue(assignment.Id, out var row)) continue;
            row.Code = assignment.Code.Trim();
            row.Name = assignment.Name.Trim();
            row.StateId = assignment.StateId;
            row.IsActive = assignment.IsActive;
            row.UpdatedAt = clock.UtcNow;
            row.UpdatedByUserId = currentUser.UserId;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
