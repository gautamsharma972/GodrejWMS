using System.Reflection;
using FluentValidation;
using GodrejWMS.Application.Common.Behaviours;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using Microsoft.Extensions.DependencyInjection;

namespace GodrejWMS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            // First registered = outermost: the gate must also cover authorization, which reads the same DbContext.
            cfg.AddOpenBehavior(typeof(SerialisedExecutionBehaviour<,>));
            cfg.AddOpenBehavior(typeof(AuthorizationBehaviour<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));
        });

        services.AddScoped<DbOperationGate>();
        services.AddValidatorsFromAssembly(assembly);
        services.AddSingleton<PermissionRevision>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IWarehouseAccessService, WarehouseAccessService>();
        services.AddScoped<IInwardPlacementService, GodrejWMS.Application.Features.Inward.InwardPlacementService>();

        return services;
    }
}
