using System.Text.Json.Serialization;

namespace TunnelFlow.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TunnelLifecycleState
{
    Stopped,
    Starting,
    Running,
    Stopping
}
