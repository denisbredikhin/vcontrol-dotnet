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
    .ConfigureServices((ctx, services) =>
    {
        // Bind options from configuration sections (appsettings, env with Mqtt__* / Vcontrol__*)
        services.Configure<Vcontrol.Worker.MqttOptions>(ctx.Configuration.GetSection("Mqtt"));
        services.Configure<Vcontrol.Worker.VcontrolOptions>(ctx.Configuration.GetSection("Vcontrol"));

        // Back-compat with existing env names (MQTT_HOST, VCONTROLD_PORT, etc.)
        services.PostConfigure<Vcontrol.Worker.MqttOptions>(opts =>
        {
            var hostEnv = Environment.GetEnvironmentVariable("MQTT_HOST");
            var portEnv = Environment.GetEnvironmentVariable("MQTT_PORT");
            var userEnv = Environment.GetEnvironmentVariable("MQTT_USER");
            var passEnv = Environment.GetEnvironmentVariable("MQTT_PASSWORD");
            var topicEnv = Environment.GetEnvironmentVariable("MQTT_TOPIC");
            if (!string.IsNullOrWhiteSpace(hostEnv)) opts.Host = hostEnv;
            if (int.TryParse(portEnv, out var p)) opts.Port = p;
            if (!string.IsNullOrWhiteSpace(userEnv)) opts.User = userEnv;
            if (!string.IsNullOrWhiteSpace(passEnv)) opts.Password = passEnv;
            if (!string.IsNullOrWhiteSpace(topicEnv)) opts.Topic = topicEnv;
        });
        services.PostConfigure<Vcontrol.Worker.VcontrolOptions>(opts =>
        {
            var hostEnv = Environment.GetEnvironmentVariable("VCONTROLD_HOST");
            var portEnv = Environment.GetEnvironmentVariable("VCONTROLD_PORT");
            var commandsEnv = Environment.GetEnvironmentVariable("COMMANDS");
            if (!string.IsNullOrWhiteSpace(hostEnv)) opts.Host = hostEnv;
            if (int.TryParse(portEnv, out var p)) opts.Port = p;
            if (!string.IsNullOrWhiteSpace(commandsEnv))
            {
                var list = commandsEnv
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
                if (list.Count > 0)
                {
                    opts.Commands = list;
                }
            }
        });

        services.AddSingleton<Vcontrol.Worker.MqttService>();
        services.AddSingleton<Vcontrol.Worker.VclientService>();
        services.AddHostedService<Vcontrol.Worker.Worker>();
    })
    .Build();

await host.RunAsync();
