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
        var proxyRules = GetEnabledProxyRules(config);
        var logNode = new JsonObject { ["level"] = "info" };
        if (!string.IsNullOrEmpty(config.LogOutputPath))
            logNode["output"] = config.LogOutputPath;

        var root = new JsonObject
        {
            ["log"] = logNode,
            ["dns"] = BuildDns(config, proxyRules),
            ["inbounds"] = BuildInbounds(config),
            ["outbounds"] = new JsonArray
            {
                BuildVlessOutbound(profile),
                new JsonObject { ["type"] = "direct", ["tag"] = "direct" }
            },
            ["route"] = BuildRoute(config, proxyRules)
        };

        return root.ToJsonString(_writeOptions);
    }

    private static JsonArray BuildInbounds(SingBoxConfig config)
    {
        if (config.UseTunMode)
        {
            return new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "tun",
                    ["tag"] = "tun-in",
                    ["interface_name"] = "TunnelFlow",
                    ["address"] = new JsonArray("172.19.0.1/30"),
                    ["mtu"] = 1500,
                    ["auto_route"] = true,
                    ["strict_route"] = true
                }
            };
        }

        return new JsonArray
        {
            new JsonObject
            {
                ["type"] = "socks",
                ["tag"] = "socks-in",
                ["listen"] = "127.0.0.1",
                ["listen_port"] = config.SocksPort
            }
        };
    }

    private static JsonObject BuildDns(SingBoxConfig config, IReadOnlyList<AppRule> proxyRules)
    {
        var dns = new JsonObject
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
            ["final"] = config.UseTunMode ? "local-dns" : "remote-dns"
        };

        if (!config.UseTunMode || proxyRules.Count == 0)
            return dns;

        var rules = new JsonArray();
        foreach (var rule in proxyRules)
        {
            rules.Add(new JsonObject
            {
                ["process_path"] = new JsonArray(rule.ExePath),
                ["action"] = "route",
                ["server"] = "remote-dns"
            });
        }

        dns["rules"] = rules;
        return dns;
    }

    private static JsonObject BuildRoute(SingBoxConfig config, IReadOnlyList<AppRule> proxyRules)
    {
        var rules = new JsonArray
        {
            new JsonObject { ["action"] = "sniff" }
        };

        if (config.UseTunMode)
        {
            foreach (var rule in proxyRules)
            {
                rules.Add(new JsonObject
                {
                    ["process_path"] = new JsonArray(rule.ExePath),
                    ["action"] = "route",
                    ["outbound"] = "vless-out"
                });
            }
        }

        rules.Add(new JsonObject
        {
            ["protocol"] = "dns",
            ["action"] = "hijack-dns"
        });

        return new JsonObject
        {
            ["rules"] = rules,
            ["final"] = config.UseTunMode ? "direct" : "vless-out",
            ["auto_detect_interface"] = true,
            ["default_domain_resolver"] = "local-dns"
        };
    }

    private static IReadOnlyList<AppRule> GetEnabledProxyRules(SingBoxConfig config) =>
        config.Rules
            .Where(rule =>
                rule.IsEnabled &&
                rule.Mode == RuleMode.Proxy &&
                !string.IsNullOrWhiteSpace(rule.ExePath))
            .ToArray();

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
