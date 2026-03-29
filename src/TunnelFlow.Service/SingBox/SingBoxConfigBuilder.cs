using System.Text.Json;
using System.Text.Json.Nodes;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Service.SingBox;

public class SingBoxConfigBuilder
{
    private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

    public string Build(VlessProfile profile, SingBoxConfig config)
    {
        bool tlsEnabled = !string.Equals(profile.Security, "none", StringComparison.OrdinalIgnoreCase);
        string sni = profile.Tls?.Sni ?? profile.ServerAddress;
        bool allowInsecure = profile.Tls?.AllowInsecure ?? false;
        bool utlsEnabled = profile.Tls?.Fingerprint is not null;
        string fingerprint = profile.Tls?.Fingerprint ?? "chrome";

        var logNode = new JsonObject { ["level"] = "info" };
        if (!string.IsNullOrEmpty(config.LogOutputPath))
            logNode["output"] = config.LogOutputPath;

        var root = new JsonObject
        {
            ["log"] = logNode,
            ["inbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "socks",
                    ["tag"] = "socks-in",
                    ["listen"] = "127.0.0.1",
                    ["listen_port"] = config.SocksPort,
                    ["sniff"] = false
                }
            },
            ["outbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "vless",
                    ["tag"] = "vless-out",
                    ["server"] = profile.ServerAddress,
                    ["server_port"] = profile.ServerPort,
                    ["uuid"] = profile.UserId,
                    ["flow"] = "",
                    ["tls"] = new JsonObject
                    {
                        ["enabled"] = tlsEnabled,
                        ["server_name"] = sni,
                        ["insecure"] = allowInsecure,
                        ["utls"] = new JsonObject
                        {
                            ["enabled"] = utlsEnabled,
                            ["fingerprint"] = fingerprint
                        }
                    }
                },
                new JsonObject { ["type"] = "direct", ["tag"] = "direct" },
                new JsonObject { ["type"] = "block",  ["tag"] = "block"  }
            },
            ["route"] = new JsonObject
            {
                ["final"] = "vless-out"
            }
        };

        return root.ToJsonString(_writeOptions);
    }
}
