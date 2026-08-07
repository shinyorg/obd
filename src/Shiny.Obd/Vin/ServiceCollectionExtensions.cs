using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shiny.Obd.Vin;

namespace Shiny;

public static class VinDecoderServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IVinDecoder"/> backed by NHTSA vPIC — free, keyless, and best for North
    /// American vehicles.
    /// </summary>
    /// <remarks>
    /// Also calls <c>AddHttpClient()</c>, since the decoder resolves an
    /// <see cref="IHttpClientFactory"/>; calling both is harmless.
    /// </remarks>
    public static IServiceCollection AddVinDecoder(this IServiceCollection services)
        => services.AddVinDecoder<VpicVinDecoder>();

    /// <summary>
    /// Registers your own <see cref="IVinDecoder"/> — a commercial provider, a regional registry, or
    /// an offline table — in place of the built-in vPIC one.
    /// </summary>
    /// <remarks>
    /// Registered with <c>TryAddSingleton</c>, so the first registration wins and a host that has
    /// already supplied its own decoder is not overwritten by a library calling
    /// <see cref="AddVinDecoder(IServiceCollection)"/> on its behalf.
    /// <para>
    /// Whatever you register must honour the contract on <see cref="IVinDecoder"/>: it never throws,
    /// and it answers null rather than guessing. Callers treat it as background enrichment and have
    /// nowhere to surface a failure.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddVinDecoder<TDecoder>(this IServiceCollection services)
        where TDecoder : class, IVinDecoder
    {
        services.AddHttpClient();
        services.TryAddSingleton<IVinDecoder, TDecoder>();
        return services;
    }
}
