using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSingleton<Vcontrol.Worker.MqttService>();
        services.AddSingleton<Vcontrol.Worker.VclientService>();
        services.AddHostedService<Vcontrol.Worker.Worker>();
    })
    .Build();

await host.RunAsync();
