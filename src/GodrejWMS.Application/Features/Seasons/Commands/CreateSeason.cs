using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Seasons.Commands;

public sealed record CreateSeasonCommand(string Code, string DisplayName, string? Description, int SortOrder) : IRequest<int>;

public sealed class CreateSeasonValidator : AbstractValidator<CreateSeasonCommand>
{
    public CreateSeasonValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(20)
            .MustAsync(async (code, ct) =>
                !await db.Seasons.AnyAsync(s => s.Code == code.Trim(), ct))
            .WithMessage("A season with this code already exists.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateSeasonHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<CreateSeasonCommand, int>
{
    public async Task<int> Handle(CreateSeasonCommand request, CancellationToken cancellationToken)
    {
        // Season.Id is a plain, non-auto-generated key (Rainy=0 predates identity-friendly values -
        // see SeasonConfiguration), so new rows must claim the next free id by hand.
        var nextId = await db.Seasons.Select(s => (int?)s.Id).MaxAsync(cancellationToken) is { } maxId ? maxId + 1 : 0;

        var season = new Season
        {
            Id = nextId,
            Code = request.Code.Trim(),
            DisplayName = request.DisplayName.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = currentUser.UserId
        };

        db.Seasons.Add(season);
        await db.SaveChangesAsync(cancellationToken);

        return season.Id;
    }
}
