using System.Text.Json.Serialization;

namespace TunnelFlow.Core.Models;

public record SingBoxConfig
{
    public int SocksPort { get; init; }

    public int? DnsPort { get; init; }

    public bool UseTunMode { get; init; }

    public IReadOnlyList<AppRule> Rules { get; init; } = [];

    public string BinaryPath { get; init; } = string.Empty;

    public string ConfigOutputPath { get; init; } = string.Empty;

    public string LogOutputPath { get; init; } = string.Empty;

    public TimeSpan RestartDelay { get; init; }

    public int MaxRestartAttempts { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SingBoxStatus
{
    Stopped,
    Starting,
    Running,
    Crashed,
    Restarting
}
