using GodrejWMS.Application.Common.Interfaces;
using MediatR;

namespace GodrejWMS.Application.Features.Roles.Queries;

public sealed record GetRoleNamesQuery : IRequest<IReadOnlyList<string>>;

public sealed class GetRoleNamesHandler(IRoleManagementService roles)
    : IRequestHandler<GetRoleNamesQuery, IReadOnlyList<string>>
{
    public Task<IReadOnlyList<string>> Handle(GetRoleNamesQuery request, CancellationToken cancellationToken) =>
        roles.GetAllRoleNamesAsync(cancellationToken);
}
