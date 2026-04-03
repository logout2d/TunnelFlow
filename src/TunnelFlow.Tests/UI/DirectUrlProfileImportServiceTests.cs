using TunnelFlow.UI.Services;

namespace TunnelFlow.Tests.UI;

public class DirectUrlProfileImportServiceTests
{
    [Fact]
    public async Task ImportFromUrlAsync_FetchesSingleVlessProfile_AndMapsCoreFields()
    {
        var service = new DirectUrlProfileImportService(
            fetchTextAsync: (_, _) => Task.FromResult(
                "vless://11111111-1111-1111-1111-111111111111@vpn.example.com:443?security=reality&sni=cdn.example.com&fp=chrome&pbk=public-key&sid=short-id&type=tcp&flow=xtls-rprx-vision#Imported%20Profile"));

        var profile = await service.ImportFromUrlAsync("https://example.com/profile.txt", CancellationToken.None);

        Assert.Equal("Imported Profile", profile.Name);
        Assert.Equal("vpn.example.com", profile.ServerAddress);
        Assert.Equal(443, profile.ServerPort);
        Assert.Equal("11111111-1111-1111-1111-111111111111", profile.UserId);
        Assert.Equal("xtls-rprx-vision", profile.Flow);
        Assert.Equal("tcp", profile.Network);
        Assert.Equal("reality", profile.Security);
        Assert.NotNull(profile.Tls);
        Assert.Equal("cdn.example.com", profile.Tls!.Sni);
        Assert.Equal("chrome", profile.Tls.Fingerprint);
        Assert.Equal("public-key", profile.Tls.RealityPublicKey);
        Assert.Equal("short-id", profile.Tls.RealityShortId);
        Assert.False(profile.IsActive);
    }

    [Fact]
    public async Task ImportFromUrlAsync_ParsesDirectVlessUri_WithoutRemoteFetch()
    {
        var fetchCalled = false;
        var service = new DirectUrlProfileImportService(
            fetchTextAsync: (_, _) =>
            {
                fetchCalled = true;
                return Task.FromResult(string.Empty);
            });

        var profile = await service.ImportFromUrlAsync(
            "vless://11111111-1111-1111-1111-111111111111@vpn.example.com:443?security=tls&sni=cdn.example.com&fp=chrome&type=ws#Direct%20Import",
            CancellationToken.None);

        Assert.False(fetchCalled);
        Assert.Equal("Direct Import", profile.Name);
        Assert.Equal("vpn.example.com", profile.ServerAddress);
        Assert.Equal(443, profile.ServerPort);
        Assert.Equal("11111111-1111-1111-1111-111111111111", profile.UserId);
        Assert.Equal("ws", profile.Network);
        Assert.Equal("tls", profile.Security);
        Assert.NotNull(profile.Tls);
        Assert.Equal("cdn.example.com", profile.Tls!.Sni);
        Assert.Equal("chrome", profile.Tls.Fingerprint);
    }

    [Fact]
    public async Task ImportFromUrlAsync_WhenBodyHasNoSupportedProfile_ThrowsFriendlyError()
    {
        var service = new DirectUrlProfileImportService(
            fetchTextAsync: (_, _) => Task.FromResult("not-a-supported-profile"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ImportFromUrlAsync("https://example.com/profile.txt", CancellationToken.None));

        Assert.Equal("The URL did not return a supported VLESS profile.", ex.Message);
    }

    [Fact]
    public async Task ImportFromUrlAsync_WhenInputIsUnsupported_ThrowsBroaderValidationMessage()
    {
        var service = new DirectUrlProfileImportService();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ImportFromUrlAsync("ftp://example.com/profile.txt", CancellationToken.None));

        Assert.Equal("Enter a valid HTTP or HTTPS URL, a subscription URL, or a direct vless:// URI.", ex.Message);
    }

    [Fact]
    public async Task ImportProfilesAsync_FetchesBase64Subscription_ImportsMultipleProfiles_AndReportsSkippedEntries()
    {
        var subscriptionBody = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(string.Join('\n',
            "# subscription",
            "vless://11111111-1111-1111-1111-111111111111@alpha.example.com:443?security=tls&sni=alpha-sni.example.com&type=tcp#Alpha",
            "vmess://unsupported-entry",
            "vless://22222222-2222-2222-2222-222222222222@beta.example.com:8443?security=reality&sni=beta-sni.example.com&fp=chrome&pbk=beta-key&sid=beta-short&type=grpc#Beta")));
        var service = new DirectUrlProfileImportService(
            fetchTextAsync: (_, _) => Task.FromResult(subscriptionBody));

        var result = await service.ImportProfilesAsync("https://example.com/subscription.txt", CancellationToken.None);

        Assert.Equal(2, result.ImportedProfileCount);
        Assert.Equal(1, result.SkippedProfileCount);

        Assert.Equal("Alpha", result.Profiles[0].Name);
        Assert.Equal("alpha.example.com", result.Profiles[0].ServerAddress);
        Assert.Equal("tcp", result.Profiles[0].Network);
        Assert.Equal("tls", result.Profiles[0].Security);

        Assert.Equal("Beta", result.Profiles[1].Name);
        Assert.Equal("beta.example.com", result.Profiles[1].ServerAddress);
        Assert.Equal("grpc", result.Profiles[1].Network);
        Assert.Equal("reality", result.Profiles[1].Security);
        Assert.Equal("beta-key", result.Profiles[1].Tls!.RealityPublicKey);
        Assert.Equal("beta-short", result.Profiles[1].Tls!.RealityShortId);
    }

    [Fact]
    public async Task ImportProfilesAsync_WhenFetchedContentHasNoSupportedProfiles_ThrowsFriendlyError()
    {
        var service = new DirectUrlProfileImportService(
            fetchTextAsync: (_, _) => Task.FromResult("vmess://unsupported-entry"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ImportProfilesAsync("https://example.com/subscription.txt", CancellationToken.None));

        Assert.Equal("The URL did not return any supported VLESS profiles.", ex.Message);
    }
}
