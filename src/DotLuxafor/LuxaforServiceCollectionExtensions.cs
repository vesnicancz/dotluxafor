#if NET8_0_OR_GREATER
using DotLuxafor;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering Luxafor services with <see cref="IServiceCollection"/>.
/// </summary>
public static class LuxaforServiceCollectionExtensions
{
    /// <summary>
    /// Adds Luxafor device services to the service collection.
    /// Registers <see cref="ILuxaforDeviceManager"/> and <see cref="LuxaforOptions"/>.
    /// Use <see cref="ILuxaforDeviceManager.TryOpen"/> to open a device when needed,
    /// or <see cref="AddLuxaforHostedService"/> for automatic connection management.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLuxafor(this IServiceCollection services)
        => AddLuxafor(services, _ => { });

    /// <summary>
    /// Adds Luxafor device services to the service collection with configuration.
    /// Registers <see cref="ILuxaforDeviceManager"/> and <see cref="LuxaforOptions"/>.
    /// Use <see cref="ILuxaforDeviceManager.TryOpen"/> to open a device when needed,
    /// or <see cref="AddLuxaforHostedService"/> for automatic connection management.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure <see cref="LuxaforOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLuxafor(this IServiceCollection services, Action<LuxaforOptions> configure)
    {
        services.Configure(configure);
        services.TryAddSingleton<ILuxaforDeviceManager, LuxaforDeviceManager>();
        services.TryAddSingleton<IValidateOptions<LuxaforOptions>, LuxaforOptionsValidator>();
        return services;
    }

    /// <summary>
    /// Adds Luxafor device services with a background hosted service that handles
    /// auto-reconnection and automatic monitoring based on <see cref="LuxaforOptions"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure <see cref="LuxaforOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLuxaforHostedService(this IServiceCollection services, Action<LuxaforOptions> configure)
    {
        services.AddLuxafor(configure);
        services.AddHostedService<LuxaforHostedService>();
        return services;
    }
}

internal sealed class LuxaforOptionsValidator : IValidateOptions<LuxaforOptions>
{
    public ValidateOptionsResult Validate(string? name, LuxaforOptions options)
    {
        if (options.ReconnectDelay <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail($"{nameof(LuxaforOptions.ReconnectDelay)} must be positive. Got: {options.ReconnectDelay}");
        }

        return ValidateOptionsResult.Success;
    }
}
#endif
