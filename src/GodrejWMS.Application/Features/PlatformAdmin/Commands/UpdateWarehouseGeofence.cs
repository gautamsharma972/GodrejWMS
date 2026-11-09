using GodrejWMS.Application.Common.Permissions;
using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;

namespace GodrejWMS.Application.Features.PlatformAdmin.Commands;

/// <summary>Sets one warehouse's Picker-login geofence - only reachable from the hidden Platform
/// Admin screen. The client-facing Warehouses CRUD (UpdateWarehouseCommand) never touches these
/// fields, so a client admin can never see or change them.</summary>
[RequiresRole(KnownRoles.PlatformAdmin)]
public sealed record UpdateWarehouseGeofenceCommand(
    int WarehouseId, bool GeofenceEnabled, double? GeofenceLatitude, double? GeofenceLongitude, int? GeofenceRadiusMeters) : IRequest;

public sealed class UpdateWarehouseGeofenceValidator : AbstractValidator<UpdateWarehouseGeofenceCommand>
{
    public UpdateWarehouseGeofenceValidator(ICurrentUserService currentUser)
    {
        RuleFor(x => x)
            .Must(_ => currentUser.IsInRole(KnownRoles.PlatformAdmin))
            .WithMessage("Only Platform Admin can configure warehouse geofencing.");

        RuleFor(x => x.WarehouseId).GreaterThan(0);

        When(x => x.GeofenceEnabled, () =>
        {
            RuleFor(x => x.GeofenceLatitude).NotNull().InclusiveBetween(-90, 90)
                .WithMessage("Latitude must be between -90 and 90 when geofencing is enabled.");
            RuleFor(x => x.GeofenceLongitude).NotNull().InclusiveBetween(-180, 180)
                .WithMessage("Longitude must be between -180 and 180 when geofencing is enabled.");
            RuleFor(x => x.GeofenceRadiusMeters).NotNull().GreaterThan(0)
                .WithMessage("Radius (meters) must be greater than 0 when geofencing is enabled.");
        });
    }
}

public sealed class UpdateWarehouseGeofenceHandler(IApplicationDbContext db)
    : IRequestHandler<UpdateWarehouseGeofenceCommand>
{
    public async Task Handle(UpdateWarehouseGeofenceCommand request, CancellationToken cancellationToken)
    {
        var warehouse = await db.Warehouses.FindAsync([request.WarehouseId], cancellationToken)
            ?? throw new NotFoundException(nameof(Warehouse), request.WarehouseId);

        warehouse.GeofenceEnabled = request.GeofenceEnabled;
        warehouse.GeofenceLatitude = request.GeofenceEnabled ? request.GeofenceLatitude : null;
        warehouse.GeofenceLongitude = request.GeofenceEnabled ? request.GeofenceLongitude : null;
        warehouse.GeofenceRadiusMeters = request.GeofenceEnabled ? request.GeofenceRadiusMeters : null;

        await db.SaveChangesAsync(cancellationToken);
    }
}
