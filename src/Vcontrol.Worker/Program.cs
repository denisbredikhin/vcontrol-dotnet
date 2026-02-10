using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Vcontrol.Worker;

var builder = WebApplication.CreateBuilder(args);

// Logging configuration (preserve existing behavior)
builder.Logging.ClearProviders();
var levelEnv = Environment.GetEnvironmentVariable("LOG_LEVEL");
var minLevel = LogLevel.Information;
if (!string.IsNullOrWhiteSpace(levelEnv))
{
    var val = levelEnv.Trim();
    if (!Enum.TryParse<LogLevel>(val, true, out minLevel))
    {
        switch (val.ToLowerInvariant())
        {
            case "info":
                minLevel = LogLevel.Information;
                break;
            case "warn":
                minLevel = LogLevel.Warning;
                break;
            case "err":
                minLevel = LogLevel.Error;
                break;
            case "fatal":
                minLevel = LogLevel.Critical;
                break;
        }
    }
}
builder.Logging.SetMinimumLevel(minLevel);
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
    options.UseUtcTimestamp = false; // set true if you prefer UTC
    options.SingleLine = true;
});

// Services configuration
var services = builder.Services;
var configuration = builder.Configuration;

// Bind options from configuration sections (appsettings, env with Mqtt__* / Vcontrol__*)
services.Configure<MqttOptions>(configuration.GetSection("Mqtt"));
services.Configure<VcontrolOptions>(configuration.GetSection("Vcontrol"));

// Back-compat with existing env names (MQTT_HOST, VCONTROLD_PORT, etc.)
services.PostConfigure<MqttOptions>(opts =>
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
services.PostConfigure<VcontrolOptions>(opts =>
{
    var hostEnv = Environment.GetEnvironmentVariable("VCONTROLD_HOST");
    var portEnv = Environment.GetEnvironmentVariable("VCONTROLD_PORT");
    var commandsEnv = Environment.GetEnvironmentVariable("COMMANDS");
    var pollEnv = Environment.GetEnvironmentVariable("POLL_SECONDS");
    var publishValueOnlyEnv = Environment.GetEnvironmentVariable("PUBLISH_VALUE_ONLY");
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
    if (int.TryParse(pollEnv, out var poll) && poll > 0)
    {
        opts.PollSeconds = poll;
    }
    if (!string.IsNullOrWhiteSpace(publishValueOnlyEnv))
    {
        var val = publishValueOnlyEnv.Trim();
        if (string.Equals(val, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(val, "true", StringComparison.OrdinalIgnoreCase))
        {
            opts.PublishValueOnly = true;
        }
        else if (string.Equals(val, "0", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(val, "false", StringComparison.OrdinalIgnoreCase))
        {
            opts.PublishValueOnly = false;
        }
    }
});

// Application services
services.AddSingleton<MqttService>();
services.AddSingleton<VclientService>();
services.AddSingleton<LastReplyState>();
services.AddHostedService<Worker>();
services.AddHostedService<CommandsSubscriber>();

// Health checks
services.AddHealthChecks()
    .AddCheck<LastReplyHealthCheck>("last_reply");

var app = builder.Build();

// Liveness endpoint - process up
app.MapGet("/health/live", () => Results.Ok(new { status = "Live" }));

// Readiness endpoint - based on last reply state
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    ResultStatusCodes = new Dictionary<HealthStatus, int>
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    },
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var state = context.RequestServices.GetRequiredService<LastReplyState>();
        var snapshot = state.GetSnapshot();
        var payload = new
        {
            status = report.Status.ToString(),
            lastSuccess = snapshot.LastSuccess,
            lastSuccessAt = snapshot.LastSuccessAt,
            lastFailureAt = snapshot.LastFailureAt,
            lastExitCode = snapshot.LastExitCode,
            lastError = snapshot.LastError
        };
        await context.Response.WriteAsJsonAsync(payload);
    }
});

app.Run();
