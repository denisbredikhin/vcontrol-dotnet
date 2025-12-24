using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(options =>
        {
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
            options.UseUtcTimestamp = false; // set true if you prefer UTC
            options.SingleLine = false;
        });
    })
    .ConfigureServices(services =>
    {
        services.AddSingleton<Vcontrol.Worker.MqttService>();
        services.AddSingleton<Vcontrol.Worker.VclientService>();
        services.AddHostedService<Vcontrol.Worker.Worker>();
    })
    .Build();

await host.RunAsync();
