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
            ["dns"] = new JsonObject
            {
                ["servers"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["tag"] = "remote-dns",
                        ["address"] = "https://1.1.1.1/dns-query",
                        ["detour"] = "vless-out"
                    },
                    new JsonObject
                    {
                        ["tag"] = "local-dns",
                        ["address"] = "local"
                    }
                },
                ["rules"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["outbound"] = "any",
                        ["server"] = "local-dns"
                    }
                },
                ["final"] = "remote-dns"
            },
            ["inbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "socks",
                    ["tag"] = "socks-in",
                    ["listen"] = "127.0.0.1",
                    ["listen_port"] = config.SocksPort,
                    ["sniff"] = true,
                    ["sniff_override_destination"] = true
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
                ["rules"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["protocol"] = "dns",
                        ["outbound"] = "vless-out"
                    }
                },
                ["final"] = "vless-out",
                ["auto_detect_interface"] = true
            }
        };

        return root.ToJsonString(_writeOptions);
    }
}
