using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Plants.Commands;

public sealed record CreatePlantCommand(string Code, string Name, int? PlantTypeId, string? Description) : IRequest<int>;

public sealed class CreatePlantValidator : AbstractValidator<CreatePlantCommand>
{
    public CreatePlantValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(20)
            .MustAsync(async (code, ct) =>
                !await db.Plants.AnyAsync(p => p.Code == code.Trim(), ct))
            .WithMessage("A plant with this code already exists.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.PlantTypeId)
            .MustAsync(async (id, ct) => id is null || await db.PlantTypes.AnyAsync(t => t.Id == id, ct))
            .WithMessage("Plant type is invalid.");
    }
}

public sealed class CreatePlantHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<CreatePlantCommand, int>
{
    public async Task<int> Handle(CreatePlantCommand request, CancellationToken cancellationToken)
    {
        var plant = new Plant
        {
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            PlantTypeId = request.PlantTypeId,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = currentUser.UserId
        };

        db.Plants.Add(plant);
        await db.SaveChangesAsync(cancellationToken);

        return plant.Id;
    }
}
