using System.Text.Json.Serialization;

namespace TunnelFlow.Core.IPC.Responses;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TunnelStatusMode
{
    Legacy,
    Tun
}
