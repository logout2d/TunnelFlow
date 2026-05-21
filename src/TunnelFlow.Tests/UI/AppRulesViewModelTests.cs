using System.Text.Json;
using TunnelFlow.Core.Models;
using TunnelFlow.UI.Services;
using TunnelFlow.UI.ViewModels;

namespace TunnelFlow.Tests.UI;

public class AppRulesViewModelTests
{
    [Theory]
    [InlineData("game", "game.exe")]
    [InlineData("  launcher.exe  ", "launcher.exe")]
    [InlineData("GAME.EXE", "GAME.EXE")]
    public void TryNormalizeExeName_NormalizesExpectedInputs(string input, string expected)
    {
        var ok = AppRulesViewModel.TryNormalizeExeName(input, out var normalized, out var error);

        Assert.True(ok);
        Assert.Equal(expected, normalized);
        Assert.Equal(string.Empty, error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\Games\game.exe")]
    [InlineData("Games/game.exe")]
    public void TryNormalizeExeName_RejectsInvalidInputs(string input)
    {
        var ok = AppRulesViewModel.TryNormalizeExeName(input, out var normalized, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, normalized);
        Assert.NotEqual(string.Empty, error);
    }

    [Fact]
    public async Task AddExeRuleCommand_AddsNormalizedExeNameRule()
    {
        using var client = new ServiceClient();
        var sentRules = new List<AppRule>();
        var viewModel = new AppRulesViewModel(
            client,
            sendCommandAsync: (type, payload, _) =>
            {
                Assert.Equal("UpsertRule", type);
                sentRules.Add((AppRule)payload!);
                return Task.FromResult<JsonElement?>(null);
            },
            promptExeName: () => "game")
        {
            IsServiceConnected = true
        };

        viewModel.AddExeRuleCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.Rules.Count == 1 && sentRules.Count == 1);

        var item = viewModel.Rules[0];
        Assert.Equal("game.exe", item.ExePath);
        Assert.Equal("game", item.DisplayName);
        Assert.Equal(AppRuleMatchType.ExeName, item.MatchType);
        Assert.Equal("Exe", item.MatchTypeLabel);
        Assert.Equal(AppRuleMatchType.ExeName, sentRules[0].MatchType);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        while (!condition())
        {
            await Task.Delay(10, cts.Token);
        }
    }
}
