using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Cities.Commands;

public sealed record CreateCityCommand(string Code, string Name, int StateId) : IRequest<int>;

public sealed class CreateCityValidator : AbstractValidator<CreateCityCommand>
{
    public CreateCityValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .MustAsync(async (code, ct) => !await db.Cities.AnyAsync(c => c.Code == code.Trim(), ct))
            .WithMessage("A city with this code already exists.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.StateId)
            .MustAsync(async (id, ct) => await db.States.AnyAsync(s => s.Id == id, ct))
            .WithMessage("State is invalid.");
    }
}

public sealed class CreateCityHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<CreateCityCommand, int>
{
    public async Task<int> Handle(CreateCityCommand request, CancellationToken cancellationToken)
    {
        var city = new City
        {
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            StateId = request.StateId,
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = currentUser.UserId
        };
        db.Cities.Add(city);
        await db.SaveChangesAsync(cancellationToken);
        return city.Id;
    }
}
