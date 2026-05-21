using TunnelFlow.Core.Models;

namespace TunnelFlow.Tests.Core;

public class AppRuleMatcherTests
{
    [Fact]
    public void FindBestMatch_ExeNameRule_MatchesProcessBasenameCaseInsensitively()
    {
        var rule = new AppRule
        {
            Id = Guid.NewGuid(),
            ExePath = "game.exe",
            MatchType = AppRuleMatchType.ExeName,
            Mode = RuleMode.Proxy,
            IsEnabled = true
        };

        var match = AppRuleMatcher.FindBestMatch([rule], @"C:\Games\GAME.EXE");

        Assert.Same(rule, match);
    }

    [Fact]
    public void FindBestMatch_FullPathWinsOverExeName()
    {
        var exeRule = new AppRule
        {
            Id = Guid.NewGuid(),
            ExePath = "game.exe",
            MatchType = AppRuleMatchType.ExeName,
            Mode = RuleMode.Proxy,
            IsEnabled = true
        };
        var pathRule = new AppRule
        {
            Id = Guid.NewGuid(),
            ExePath = @"C:\Games\game.exe",
            MatchType = AppRuleMatchType.FullPath,
            Mode = RuleMode.Direct,
            IsEnabled = true
        };

        var match = AppRuleMatcher.FindBestMatch([exeRule, pathRule], @"c:\games\GAME.exe");

        Assert.Same(pathRule, match);
    }
}
