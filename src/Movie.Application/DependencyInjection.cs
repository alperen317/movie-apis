using Microsoft.Extensions.DependencyInjection;

namespace Movie.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Registration is generated at compile time by Mediator's source
        // generator, so there is no assembly scanning at startup.
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

        services.AddScoped<Features.Authentication.AuthTokenIssuer>();

        return services;
    }
}
