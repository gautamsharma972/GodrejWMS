using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Brands.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Brands.Queries;

public sealed record GetBrandsQuery : IRequest<IReadOnlyList<BrandDto>>;

public sealed class GetBrandsHandler(IApplicationDbContext db) : IRequestHandler<GetBrandsQuery, IReadOnlyList<BrandDto>>
{
    public async Task<IReadOnlyList<BrandDto>> Handle(GetBrandsQuery request, CancellationToken cancellationToken) =>
        await db.Brands.AsNoTracking().OrderBy(x => x.Code)
            .Select(x => new BrandDto(x.Id, x.Code, x.Description, x.IsActive))
            .ToListAsync(cancellationToken);
}
