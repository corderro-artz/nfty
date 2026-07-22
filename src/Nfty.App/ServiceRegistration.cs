using Microsoft.Extensions.DependencyInjection;

namespace Nfty.App;

public static class ServiceRegistration
{
    /// <summary>Registers all Nfty.App services and ViewModels. Extended by later tasks.</summary>
    public static IServiceCollection AddNftyApp(this IServiceCollection services) => services;
}
