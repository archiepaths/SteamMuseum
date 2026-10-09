using Microsoft.AspNetCore.HttpOverrides;

namespace SteamMuseum.Api;

public static class ProxyHeaders
{
    public static IServiceCollection AddMuseumProxyHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options => {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Trust only configured ingress addresses. Never clear the allowlists to trust everyone.
            foreach (var network in configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [])
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        });
        return services;
    }
}
