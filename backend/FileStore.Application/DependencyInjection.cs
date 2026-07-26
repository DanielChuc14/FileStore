using System.Reflection;
using FileStore.Application.Common.Behaviors;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace FileStore.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);

            // El orden es el de ejecucion. La comprobacion de la cuenta va
            // primero: a un cliente dado de baja se le responde 401 sin llegar a
            // evaluar si su request estaba bien formado.
            cfg.AddOpenBehavior(typeof(ClientStatusBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly);

        return services;
    }
}
