namespace Vcontrol.Worker;

public class VcontrolOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3002;
    public List<string> Commands { get; set; } = [];
}
