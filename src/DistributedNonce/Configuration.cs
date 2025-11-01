using DistributedNonce.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DistributedNonce;

public static class CacheManagerConfiguration
{
    public static void AddDistributedNonce(this IServiceCollection services)
    {
        services.AddScoped<DistributedNonceService>();
    }
}