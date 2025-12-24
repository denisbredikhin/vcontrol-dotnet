namespace Vcontrol.Worker;

public class MqttOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 1883;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string? Topic { get; set; }
}
