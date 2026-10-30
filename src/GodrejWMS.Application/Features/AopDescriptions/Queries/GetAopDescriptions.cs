using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.AopDescriptions.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.AopDescriptions.Queries;

public sealed record GetAopDescriptionsQuery : IRequest<IReadOnlyList<AopDescriptionDto>>;

public sealed class GetAopDescriptionsHandler(IApplicationDbContext db) : IRequestHandler<GetAopDescriptionsQuery, IReadOnlyList<AopDescriptionDto>>
{
    public async Task<IReadOnlyList<AopDescriptionDto>> Handle(GetAopDescriptionsQuery request, CancellationToken cancellationToken) =>
        await db.AopDescriptions.AsNoTracking().OrderBy(x => x.Code)
            .Select(x => new AopDescriptionDto(x.Id, x.Code, x.Description, x.IsActive))
            .ToListAsync(cancellationToken);
}
