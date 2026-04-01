using System.Text.Json;
using TunnelFlow.Core.Models;
using TunnelFlow.Service.SingBox;

namespace TunnelFlow.Tests.Service;

public class SingBoxConfigBuilderTests
{
    private readonly SingBoxConfigBuilder _builder = new();

    private static VlessProfile MakeProfile(
        string serverAddress = "proxy.example.com",
        int serverPort = 443,
        string userId = "00000000-0000-0000-0000-000000000001",
        string security = "tls",
        TlsOptions? tls = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "test",
        ServerAddress = serverAddress,
        ServerPort = serverPort,
        UserId = userId,
        Network = "tcp",
        Security = security,
        Tls = tls
    };

    private static SingBoxConfig MakeConfig(
        int socksPort = 2080,
        bool useTunMode = false,
        IReadOnlyList<AppRule>? rules = null) => new()
    {
        SocksPort = socksPort,
        UseTunMode = useTunMode,
        Rules = rules ?? [],
        BinaryPath = "sing-box.exe",
        ConfigOutputPath = "singbox-config.json",
        LogOutputPath = "singbox.log",
        RestartDelay = TimeSpan.FromSeconds(3),
        MaxRestartAttempts = 5
    };

    [Fact]
    public void Build_ProducesValidJson()
    {
        var json = _builder.Build(MakeProfile(), MakeConfig());
        var ex = Record.Exception(() => JsonDocument.Parse(json));
        Assert.Null(ex);
    }

    [Fact]
    public void Build_ContainsServerAddress()
    {
        const string address = "proxy.example.com";
        var json = _builder.Build(MakeProfile(serverAddress: address), MakeConfig());
        Assert.Contains(address, json);
    }

    [Fact]
    public void Build_ContainsSocksPort()
    {
        const int port = 3333;
        var json = _builder.Build(MakeProfile(), MakeConfig(socksPort: port));

        using var doc = JsonDocument.Parse(json);
        var listenPort = doc.RootElement
            .GetProperty("inbounds")[0]
            .GetProperty("listen_port")
            .GetInt32();
        Assert.Equal(port, listenPort);
    }

    [Fact]
    public void Build_InboundType_IsSocks()
    {
        var json = _builder.Build(MakeProfile(), MakeConfig());
        using var doc = JsonDocument.Parse(json);
        var inboundType = doc.RootElement
            .GetProperty("inbounds")[0]
            .GetProperty("type")
            .GetString();
        Assert.Equal("socks", inboundType);
    }

    [Fact]
    public void Build_UseTunModeFalse_KeepsLegacySocksInbound()
    {
        var json = _builder.Build(MakeProfile(), MakeConfig(useTunMode: false));

        using var doc = JsonDocument.Parse(json);
        var inbound = doc.RootElement.GetProperty("inbounds")[0];
        Assert.Equal("socks", inbound.GetProperty("type").GetString());
        Assert.Equal("127.0.0.1", inbound.GetProperty("listen").GetString());
        Assert.Equal(2080, inbound.GetProperty("listen_port").GetInt32());
    }

    [Fact]
    public void Build_UseTunModeTrue_UsesTunInboundSkeleton()
    {
        var json = _builder.Build(MakeProfile(), MakeConfig(useTunMode: true));

        using var doc = JsonDocument.Parse(json);
        var inbound = doc.RootElement.GetProperty("inbounds")[0];
        Assert.Equal("tun", inbound.GetProperty("type").GetString());
        Assert.Equal("tun-in", inbound.GetProperty("tag").GetString());
        Assert.Equal("TunnelFlow", inbound.GetProperty("interface_name").GetString());
        Assert.Equal("172.19.0.1/30", inbound.GetProperty("address")[0].GetString());
        Assert.Equal(1500, inbound.GetProperty("mtu").GetInt32());
        Assert.True(inbound.GetProperty("auto_route").GetBoolean());
        Assert.True(inbound.GetProperty("strict_route").GetBoolean());
        Assert.False(inbound.TryGetProperty("listen_port", out _));
    }

    [Fact]
    public void Build_UseTunModeTrue_AddsProxyProcessPathRouteRules_AndDirectFinal()
    {
        var rules = new[]
        {
            new AppRule
            {
                Id = Guid.NewGuid(),
                ExePath = @"C:\Apps\ProxyMe.exe",
                DisplayName = "ProxyMe",
                Mode = RuleMode.Proxy,
                IsEnabled = true
            },
            new AppRule
            {
                Id = Guid.NewGuid(),
                ExePath = @"C:\Apps\DirectMe.exe",
                DisplayName = "DirectMe",
                Mode = RuleMode.Direct,
                IsEnabled = true
            },
            new AppRule
            {
                Id = Guid.NewGuid(),
                ExePath = @"C:\Apps\Disabled.exe",
                DisplayName = "Disabled",
                Mode = RuleMode.Proxy,
                IsEnabled = false
            }
        };

        var json = _builder.Build(MakeProfile(), MakeConfig(useTunMode: true, rules: rules));

        using var doc = JsonDocument.Parse(json);
        var route = doc.RootElement.GetProperty("route");
        Assert.Equal("direct", route.GetProperty("final").GetString());

        var routeRules = route.GetProperty("rules");
        Assert.Contains(routeRules.EnumerateArray(), rule =>
            rule.TryGetProperty("process_path", out var processPaths) &&
            processPaths[0].GetString() == @"C:\Apps\ProxyMe.exe" &&
            rule.GetProperty("outbound").GetString() == "vless-out");
        Assert.DoesNotContain(routeRules.EnumerateArray(), rule =>
            rule.TryGetProperty("process_path", out var processPaths) &&
            processPaths[0].GetString() == @"C:\Apps\DirectMe.exe");
        Assert.DoesNotContain(routeRules.EnumerateArray(), rule =>
            rule.TryGetProperty("process_path", out var processPaths) &&
            processPaths[0].GetString() == @"C:\Apps\Disabled.exe");
    }

    [Fact]
    public void Build_UseTunModeTrue_AddsProxyDnsRules_AndKeepsLocalDnsAsDefault()
    {
        var rules = new[]
        {
            new AppRule
            {
                Id = Guid.NewGuid(),
                ExePath = @"C:\Apps\ProxyMe.exe",
                DisplayName = "ProxyMe",
                Mode = RuleMode.Proxy,
                IsEnabled = true
            }
        };

        var json = _builder.Build(MakeProfile(), MakeConfig(useTunMode: true, rules: rules));

        using var doc = JsonDocument.Parse(json);
        var dns = doc.RootElement.GetProperty("dns");
        Assert.Equal("local-dns", dns.GetProperty("final").GetString());

        var dnsRules = dns.GetProperty("rules");
        Assert.Single(dnsRules.EnumerateArray());
        var dnsRule = dnsRules[0];
        Assert.Equal(@"C:\Apps\ProxyMe.exe", dnsRule.GetProperty("process_path")[0].GetString());
        Assert.Equal("route", dnsRule.GetProperty("action").GetString());
        Assert.Equal("remote-dns", dnsRule.GetProperty("server").GetString());
    }

    [Fact]
    public void Build_SniffEnabled_OnSocksInbound()
    {
        var json = _builder.Build(MakeProfile(), MakeConfig());

        using var doc = JsonDocument.Parse(json);
        var rules = doc.RootElement.GetProperty("route").GetProperty("rules");
        Assert.Equal("sniff", rules[0].GetProperty("action").GetString());
    }

    [Fact]
    public void Build_DnsSectionIsPresent()
    {
        var json = _builder.Build(MakeProfile(), MakeConfig());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("dns", out var dns));

        // remote-dns server uses new sing-box 1.12+ typed format
        var servers = dns.GetProperty("servers");
        Assert.True(servers.GetArrayLength() >= 2);
        Assert.Equal("remote-dns", servers[0].GetProperty("tag").GetString());
        Assert.Equal("https", servers[0].GetProperty("type").GetString());
        Assert.Equal("1.1.1.1", servers[0].GetProperty("server").GetString());
        Assert.Equal("vless-out", servers[0].GetProperty("detour").GetString());
        Assert.Equal("local", servers[1].GetProperty("type").GetString());

        Assert.False(dns.TryGetProperty("rules", out _), "dns.rules removed in sing-box 1.13 layout");

        var route = root.GetProperty("route");
        Assert.Equal("local-dns", route.GetProperty("default_domain_resolver").GetString());

        var routeRules = route.GetProperty("rules");
        Assert.Equal(2, routeRules.GetArrayLength());
        Assert.Equal("dns", routeRules[1].GetProperty("protocol").GetString());
        Assert.Equal("hijack-dns", routeRules[1].GetProperty("action").GetString());
    }

    [Fact]
    public void Build_RouteHasAutoDetectInterface()
    {
        var json = _builder.Build(MakeProfile(), MakeConfig());

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement
            .GetProperty("route")
            .GetProperty("auto_detect_interface")
            .GetBoolean());
    }

    [Fact]
    public void Build_TlsDisabled_WhenSecurityIsNone()
    {
        var json = _builder.Build(MakeProfile(security: "none"), MakeConfig());

        using var doc = JsonDocument.Parse(json);
        var outbound = doc.RootElement.GetProperty("outbounds")[0];
        Assert.False(outbound.TryGetProperty("tls", out _), "security none must omit tls on vless outbound");
    }

    [Fact]
    public void Build_NullTls_FallsBackToServerAddressAsSni()
    {
        const string serverAddress = "fallback.example.com";
        var profile = MakeProfile(serverAddress: serverAddress, tls: null);
        var json = _builder.Build(profile, MakeConfig());

        using var doc = JsonDocument.Parse(json);
        var sni = doc.RootElement
            .GetProperty("outbounds")[0]
            .GetProperty("tls")
            .GetProperty("server_name")
            .GetString();
        Assert.Equal(serverAddress, sni);
    }

    [Fact]
    public void Build_ExplicitTls_UsesProvidedSni()
    {
        var tls = new TlsOptions { Sni = "custom.sni.example.com", AllowInsecure = false };
        var json = _builder.Build(MakeProfile(tls: tls), MakeConfig());

        using var doc = JsonDocument.Parse(json);
        var sni = doc.RootElement
            .GetProperty("outbounds")[0]
            .GetProperty("tls")
            .GetProperty("server_name")
            .GetString();
        Assert.Equal(tls.Sni, sni);
    }

    [Fact]
    public void Build_Reality_OutboundContainsRealityAndUtls()
    {
        var tls = new TlsOptions
        {
            Sni = "www.example.com",
            AllowInsecure = false,
            Fingerprint = "chrome",
            RealityPublicKey = "SuT2Ct1R8lBD36kL2ZoM8zaiks3P0DetQ-In2z-WSVk",
            RealityShortId = "adbacdba5bfe"
        };
        var profile = MakeProfile(security: "reality", tls: tls);
        var json = _builder.Build(profile, MakeConfig());

        using var doc = JsonDocument.Parse(json);
        var tlsNode = doc.RootElement.GetProperty("outbounds")[0].GetProperty("tls");
        Assert.True(tlsNode.GetProperty("enabled").GetBoolean());
        Assert.Equal("www.example.com", tlsNode.GetProperty("server_name").GetString());
        var reality = tlsNode.GetProperty("reality");
        Assert.True(reality.GetProperty("enabled").GetBoolean());
        Assert.Equal(tls.RealityPublicKey, reality.GetProperty("public_key").GetString());
        Assert.Equal(tls.RealityShortId, reality.GetProperty("short_id").GetString());
        Assert.True(tlsNode.GetProperty("utls").GetProperty("enabled").GetBoolean());
        Assert.False(tlsNode.TryGetProperty("insecure", out _), "reality tls must not use legacy insecure field");
    }

    [Fact]
    public void Build_Flow_OmittedWhenEmpty_PresentWhenSet()
    {
        var emptyFlow = _builder.Build(MakeProfile(), MakeConfig());
        using (var doc = JsonDocument.Parse(emptyFlow))
            Assert.False(doc.RootElement.GetProperty("outbounds")[0].TryGetProperty("flow", out _));

        var withFlow = _builder.Build(
            MakeProfile() with { Flow = "xtls-rprx-vision" },
            MakeConfig());
        using (var doc = JsonDocument.Parse(withFlow))
            Assert.Equal("xtls-rprx-vision",
                doc.RootElement.GetProperty("outbounds")[0].GetProperty("flow").GetString());
    }

    [Fact]
    public void Build_TcpNetwork_OmitsTransport()
    {
        var json = _builder.Build(MakeProfile() with { Network = "tcp" }, MakeConfig());

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("outbounds")[0].TryGetProperty("transport", out _));
    }

    [Fact]
    public void Build_WebSocketNetwork_UsesWsTransport()
    {
        var json = _builder.Build(MakeProfile() with { Network = "ws" }, MakeConfig());

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(
            "ws",
            doc.RootElement.GetProperty("outbounds")[0].GetProperty("transport").GetProperty("type").GetString());
    }

    [Fact]
    public void Build_GrpcNetwork_UsesGrpcTransport()
    {
        var json = _builder.Build(MakeProfile() with { Network = "grpc" }, MakeConfig());

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(
            "grpc",
            doc.RootElement.GetProperty("outbounds")[0].GetProperty("transport").GetProperty("type").GetString());
    }
}
