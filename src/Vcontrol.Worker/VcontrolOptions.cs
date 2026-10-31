using System.ComponentModel.DataAnnotations;

namespace Vcontrol.Worker;

internal class VcontrolOptions
{
    public string Host { get; set; } = "127.0.0.1";

    [Range(1, 65535)]
    public int Port { get; set; } = 3002;
    public List<string> Commands { get; set; } = [];
    public int PollSeconds { get; set; } = 60;
    public bool PublishValueOnly { get; set; }
    public bool UseDirectConnection { get; set; }
    public bool UseNativeConnection { get; set; }
    public string OptolinkDevice { get; set; } = "/dev/ttyUSB0";
    public string OptolinkConfigPath { get; set; } = "/etc/vcontrold/vcontrold.xml";
}
