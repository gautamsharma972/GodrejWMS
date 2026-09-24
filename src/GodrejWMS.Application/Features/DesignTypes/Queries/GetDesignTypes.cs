using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.DesignTypes.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.DesignTypes.Queries;

public sealed record GetDesignTypesQuery : IRequest<IReadOnlyList<DesignTypeDto>>;

public sealed class GetDesignTypesHandler(IApplicationDbContext db)
    : IRequestHandler<GetDesignTypesQuery, IReadOnlyList<DesignTypeDto>>
{
    public async Task<IReadOnlyList<DesignTypeDto>> Handle(GetDesignTypesQuery request, CancellationToken cancellationToken)
    {
        return await db.DesignTypes
            .AsNoTracking()
            .OrderBy(d => d.Code)
            .Select(d => new DesignTypeDto(d.Id, d.Code, d.Description, d.IsActive))
            .ToListAsync(cancellationToken);
    }
}
