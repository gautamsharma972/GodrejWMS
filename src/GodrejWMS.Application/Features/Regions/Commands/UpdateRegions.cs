using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Regions.Commands;

public sealed record RegionAssignment(int Id, string Code, string Name, string? Description, bool IsActive);

public sealed record UpdateRegionsCommand(IReadOnlyList<RegionAssignment> Assignments) : IRequest;

public sealed class UpdateRegionsValidator : AbstractValidator<UpdateRegionsCommand>
{
    public UpdateRegionsValidator()
    {
        RuleFor(x => x.Assignments).Must(a => a.Select(m => m.Id).Distinct().Count() == a.Count)
            .WithMessage("Each region may only appear once.");
        RuleFor(x => x.Assignments).Must(a => a.Select(m => m.Code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == a.Count)
            .WithMessage("Region codes must be unique.");
        RuleForEach(x => x.Assignments).ChildRules(a =>
        {
            a.RuleFor(m => m.Code).NotEmpty().MaximumLength(20);
            a.RuleFor(m => m.Name).NotEmpty().MaximumLength(100);
            a.RuleFor(m => m.Description).MaximumLength(300);
        });
    }
}

public sealed class UpdateRegionsHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<UpdateRegionsCommand>
{
    public async Task Handle(UpdateRegionsCommand request, CancellationToken cancellationToken)
    {
        var byId = await db.Regions.ToDictionaryAsync(r => r.Id, cancellationToken);
        foreach (var assignment in request.Assignments)
        {
            if (!byId.TryGetValue(assignment.Id, out var row)) continue;
            row.Code = assignment.Code.Trim();
            row.Name = assignment.Name.Trim();
            row.Description = string.IsNullOrWhiteSpace(assignment.Description) ? null : assignment.Description.Trim();
            row.IsActive = assignment.IsActive;
            row.UpdatedAt = clock.UtcNow;
            row.UpdatedByUserId = currentUser.UserId;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
