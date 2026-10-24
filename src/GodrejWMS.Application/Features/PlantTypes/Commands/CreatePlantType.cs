using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.PlantTypes.Commands;

public sealed record CreatePlantTypeCommand(string Code, string Name, int SortOrder) : IRequest<int>;

public sealed class CreatePlantTypeValidator : AbstractValidator<CreatePlantTypeCommand>
{
    public CreatePlantTypeValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(20)
            .MustAsync(async (code, ct) =>
                !await db.PlantTypes.AnyAsync(p => p.Code == code.Trim(), ct))
            .WithMessage("A plant type with this code already exists.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreatePlantTypeHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<CreatePlantTypeCommand, int>
{
    public async Task<int> Handle(CreatePlantTypeCommand request, CancellationToken cancellationToken)
    {
        var plantType = new PlantType
        {
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = currentUser.UserId
        };

        db.PlantTypes.Add(plantType);
        await db.SaveChangesAsync(cancellationToken);

        return plantType.Id;
    }
}
