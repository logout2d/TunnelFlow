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

    private static SingBoxConfig MakeConfig(int socksPort = 2080) => new()
    {
        SocksPort = socksPort,
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
    public void Build_SniffEnabled_OnSocksInbound()
    {
        var json = _builder.Build(MakeProfile(), MakeConfig());

        using var doc = JsonDocument.Parse(json);
        var inbound = doc.RootElement.GetProperty("inbounds")[0];
        Assert.True(inbound.GetProperty("sniff").GetBoolean());
        Assert.True(inbound.GetProperty("sniff_override_destination").GetBoolean());
    }

    [Fact]
    public void Build_DnsSectionIsPresent()
    {
        var json = _builder.Build(MakeProfile(), MakeConfig());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("dns", out var dns));

        // remote-dns server routes through vless-out
        var servers = dns.GetProperty("servers");
        Assert.True(servers.GetArrayLength() >= 2);
        Assert.Equal("remote-dns", servers[0].GetProperty("tag").GetString());
        Assert.Equal("vless-out", servers[0].GetProperty("detour").GetString());

        // route has dns protocol rule
        var routeRules = root.GetProperty("route").GetProperty("rules");
        Assert.True(routeRules.GetArrayLength() >= 1);
        Assert.Equal("dns", routeRules[0].GetProperty("protocol").GetString());
        Assert.Equal("vless-out", routeRules[0].GetProperty("outbound").GetString());
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
        var tlsEnabled = doc.RootElement
            .GetProperty("outbounds")[0]
            .GetProperty("tls")
            .GetProperty("enabled")
            .GetBoolean();
        Assert.False(tlsEnabled);
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
}
