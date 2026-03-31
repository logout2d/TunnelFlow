using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Service.SingBox;

public class SingBoxConfigBuilder
{
    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public string Build(VlessProfile profile, SingBoxConfig config)
    {
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
                        ["type"] = "https",
                        ["server"] = "1.1.1.1",
                        ["detour"] = "vless-out"
                    },
                    new JsonObject
                    {
                        ["tag"] = "local-dns",
                        ["type"] = "local"
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
                    ["listen_port"] = config.SocksPort
                }
            },
            ["outbounds"] = new JsonArray
            {
                BuildVlessOutbound(profile),
                new JsonObject { ["type"] = "direct", ["tag"] = "direct" }
            },
            ["route"] = new JsonObject
            {
                ["rules"] = new JsonArray
                {
                    new JsonObject { ["action"] = "sniff" },
                    new JsonObject
                    {
                        ["protocol"] = "dns",
                        ["action"] = "hijack-dns"
                    }
                },
                ["final"] = "vless-out",
                ["auto_detect_interface"] = true,
                ["default_domain_resolver"] = "local-dns"
            }
        };

        return root.ToJsonString(_writeOptions);
    }

    private static JsonObject BuildVlessOutbound(VlessProfile profile)
    {
        var vless = new JsonObject
        {
            ["type"] = "vless",
            ["tag"] = "vless-out",
            ["server"] = profile.ServerAddress,
            ["server_port"] = profile.ServerPort,
            ["uuid"] = profile.UserId
        };

        if (!string.IsNullOrWhiteSpace(profile.Flow))
            vless["flow"] = profile.Flow;

        var transport = BuildTransport(profile.Network);
        if (transport is not null)
            vless["transport"] = transport;

        if (string.Equals(profile.Security, "none", StringComparison.OrdinalIgnoreCase))
            return vless;

        string sni = string.IsNullOrEmpty(profile.Tls?.Sni)
            ? profile.ServerAddress
            : profile.Tls!.Sni;
        string fingerprint = string.IsNullOrEmpty(profile.Tls?.Fingerprint)
            ? "chrome"
            : profile.Tls!.Fingerprint!;

        var utls = new JsonObject
        {
            ["enabled"] = true,
            ["fingerprint"] = fingerprint
        };

        if (string.Equals(profile.Security, "reality", StringComparison.OrdinalIgnoreCase))
        {
            vless["tls"] = new JsonObject
            {
                ["enabled"] = true,
                ["server_name"] = sni,
                ["reality"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["public_key"] = profile.Tls?.RealityPublicKey ?? string.Empty,
                    ["short_id"] = profile.Tls?.RealityShortId ?? string.Empty
                },
                ["utls"] = utls
            };
        }
        else
        {
            bool allowInsecure = profile.Tls?.AllowInsecure ?? false;
            vless["tls"] = new JsonObject
            {
                ["enabled"] = true,
                ["server_name"] = sni,
                ["insecure"] = allowInsecure,
                ["utls"] = utls
            };
        }

        return vless;
    }

    private static JsonObject? BuildTransport(string? network)
    {
        if (string.IsNullOrWhiteSpace(network) ||
            string.Equals(network, "tcp", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(network, "ws", StringComparison.OrdinalIgnoreCase))
            return new JsonObject { ["type"] = "ws" };

        if (string.Equals(network, "grpc", StringComparison.OrdinalIgnoreCase))
            return new JsonObject { ["type"] = "grpc" };

        return null;
    }
}
