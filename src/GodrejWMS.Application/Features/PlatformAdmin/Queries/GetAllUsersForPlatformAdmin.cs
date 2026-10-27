using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using MediatR;

namespace GodrejWMS.Application.Features.PlatformAdmin.Queries;

/// <summary>Every account in the system, including other Platform Admin accounts - only for the
/// hidden Platform Admin screen. The client-facing Users page uses GetUsersQuery instead, which
/// goes through IUserManagementService.ListUsersAsync (Platform-Admin-filtered).</summary>
public sealed record GetAllUsersForPlatformAdminQuery : IRequest<IReadOnlyList<UserAccountDto>>;

public sealed class GetAllUsersForPlatformAdminValidator : AbstractValidator<GetAllUsersForPlatformAdminQuery>
{
    public GetAllUsersForPlatformAdminValidator(ICurrentUserService currentUser)
    {
        RuleFor(x => x)
            .Must(_ => currentUser.IsInRole(KnownRoles.PlatformAdmin))
            .WithMessage("Only Platform Admin can view this.");
    }
}

public sealed class GetAllUsersForPlatformAdminHandler(IUserManagementService users)
    : IRequestHandler<GetAllUsersForPlatformAdminQuery, IReadOnlyList<UserAccountDto>>
{
    public Task<IReadOnlyList<UserAccountDto>> Handle(GetAllUsersForPlatformAdminQuery request, CancellationToken cancellationToken) =>
        users.ListAllUsersAsync(cancellationToken);
}
