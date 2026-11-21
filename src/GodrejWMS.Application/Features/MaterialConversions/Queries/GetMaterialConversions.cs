using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Models;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.MaterialConversions.Dtos;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.MaterialConversions.Queries;

public sealed record GetMaterialConversionsQuery(
    string? Search = null, MaterialConversionStatus? Status = null, int? WarehouseId = null, int PageNumber = 1, int PageSize = 20)
    : IRequest<PaginatedList<MaterialConversionDto>>;

public sealed class GetMaterialConversionsHandler(IApplicationDbContext db, ICurrentUserService currentUser, IPermissionService permissions)
    : IRequestHandler<GetMaterialConversionsQuery, PaginatedList<MaterialConversionDto>>
{
    public async Task<PaginatedList<MaterialConversionDto>> Handle(GetMaterialConversionsQuery request, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Menu.ViewMaterialConversion, cancellationToken))
        {
            throw new UnauthorizedAccessException("You are not permitted to view material conversions.");
        }

        var query = db.MaterialConversions.AsNoTracking().AsQueryable();
        if (request.Status.HasValue) query = query.Where(c => c.Status == request.Status.Value);
        if (request.WarehouseId.HasValue) query = query.Where(c => c.WarehouseId == request.WarehouseId.Value);

        // Approvers see every request; everybody else only their own.
        if (!await permissions.HasPermissionAsync(PermissionKeys.Inventory.ApproveMaterialConversion, cancellationToken))
        {
            query = query.Where(c => c.RequestedByUserId == currentUser.UserId);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(c => c.ConversionNumber.Contains(term)
                || c.FromMaterial.MaterialNumber.ToString().Contains(term) || c.ToMaterial.MaterialNumber.ToString().Contains(term)
                || c.FromMaterial.Description.Contains(term) || c.ToMaterial.Description.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var page = Math.Max(1, request.PageNumber);
        var size = Math.Clamp(request.PageSize, 1, 100);
        var items = await MaterialConversionMapper.WithDetail(query.OrderByDescending(c => c.RequestedAt))
            .Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
        return new PaginatedList<MaterialConversionDto>(items.Select(MaterialConversionMapper.ToDto).ToList(), total, page, size);
    }
}
