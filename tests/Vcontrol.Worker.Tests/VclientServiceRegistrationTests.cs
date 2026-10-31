using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Vcontrol.Worker.Tests;

public sealed class VclientServiceRegistrationTests
{
    [Theory]
    [InlineData(false, false, typeof(VclientService))]
    [InlineData(true, false, typeof(DirectVclientService))]
    [InlineData(false, true, typeof(NativeVclientService))]
    public void Service_flags_select_the_expected_singleton(bool direct, bool native, Type expected)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_ => new VcontrolMetrics(false));
        services.Configure<VcontrolOptions>(o =>
        {
            o.UseDirectConnection = direct;
            o.UseNativeConnection = native;
            o.OptolinkConfigPath = Path.Combine(AppContext.BaseDirectory, "OptolinkFixtures", "vcontrold.xml");
        });
        services.AddVclientService();
        using var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<IVclientService>();

        service.GetType().Should().Be(expected);
        provider.GetRequiredService<IVclientService>().Should().BeSameAs(service);
    }

    [Fact]
    public async Task Conflicting_experimental_flags_fail_host_startup()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.Configure<VcontrolOptions>(o =>
        {
            o.UseDirectConnection = true;
            o.UseNativeConnection = true;
        });
        builder.Services.AddVclientService();
        using var host = builder.Build();

        var act = () => host.StartAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OptionsValidationException>().WithMessage("*cannot both be enabled*");
    }

    [Fact]
    public async Task Native_mode_requires_a_nonempty_device_path()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.Configure<VcontrolOptions>(o =>
        {
            o.UseNativeConnection = true;
            o.OptolinkDevice = "";
        });
        builder.Services.AddVclientService();
        using var host = builder.Build();

        var act = () => host.StartAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OptionsValidationException>().WithMessage("*OPTOLINK_DEVICE*");
    }
}
