using Microsoft.Extensions.Options;

namespace Vcontrol.Worker;

internal static class VclientServiceRegistration
{
    public static IServiceCollection AddVclientService(this IServiceCollection services)
    {
        services.AddOptions<VcontrolOptions>()
            .ValidateDataAnnotations()
            .Validate(options => !options.UseDirectConnection || !options.UseNativeConnection,
                "VCLIENT_DIRECT_CONNECTION and VCLIENT_NATIVE_CONNECTION cannot both be enabled.")
            .Validate(options => !options.UseNativeConnection || !string.IsNullOrWhiteSpace(options.OptolinkDevice),
                "Native Optolink requires an OPTOLINK_DEVICE path.")
            .Validate(options => !options.UseNativeConnection || !string.IsNullOrWhiteSpace(options.OptolinkConfigPath),
                "Native Optolink requires a vcontrold.xml configuration path.")
            .ValidateOnStart();
        services.AddSingleton<IVclientService>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<VcontrolOptions>>().Value;
            if (options.UseNativeConnection)
            {
                return new NativeVclientService(
                    provider.GetRequiredService<ILogger<NativeVclientService>>(),
                    OptolinkConfiguration.Load(options.OptolinkConfigPath),
                    provider.GetRequiredService<VcontrolMetrics>(),
                    () => new OptolinkSerialStream(options.OptolinkDevice),
                    TimeProvider.System);
            }
            return options.UseDirectConnection
                ? ActivatorUtilities.CreateInstance<DirectVclientService>(provider)
                : ActivatorUtilities.CreateInstance<VclientService>(provider);
        });
        return services;
    }
}
