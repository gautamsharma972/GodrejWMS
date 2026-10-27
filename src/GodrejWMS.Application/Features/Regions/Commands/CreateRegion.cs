using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Regions.Commands;

public sealed record CreateRegionCommand(string Code, string Name, string? Description) : IRequest<int>;

public sealed class CreateRegionValidator : AbstractValidator<CreateRegionCommand>
{
    public CreateRegionValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .MustAsync(async (code, ct) => !await db.Regions.AnyAsync(r => r.Code == code.Trim(), ct))
            .WithMessage("A region with this code already exists.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(300);
    }
}

public sealed class CreateRegionHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<CreateRegionCommand, int>
{
    public async Task<int> Handle(CreateRegionCommand request, CancellationToken cancellationToken)
    {
        var region = new Region
        {
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = currentUser.UserId
        };
        db.Regions.Add(region);
        await db.SaveChangesAsync(cancellationToken);
        return region.Id;
    }
}
