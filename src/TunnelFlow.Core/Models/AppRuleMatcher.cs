namespace TunnelFlow.Core.Models;

public static class AppRuleMatcher
{
    public static AppRule? FindBestMatch(IEnumerable<AppRule> rules, string processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return null;
        }

        var enabledRules = rules
            .Where(rule => rule.IsEnabled && !string.IsNullOrWhiteSpace(rule.ExePath))
            .ToList();

        var fullPathMatch = enabledRules.FirstOrDefault(rule =>
            rule.MatchType == AppRuleMatchType.FullPath &&
            string.Equals(rule.ExePath, processPath, StringComparison.OrdinalIgnoreCase));
        if (fullPathMatch is not null)
        {
            return fullPathMatch;
        }

        var exeName = Path.GetFileName(processPath);
        return enabledRules.FirstOrDefault(rule =>
            rule.MatchType == AppRuleMatchType.ExeName &&
            string.Equals(rule.ExePath, exeName, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<AppRule> OrderByMatchPrecedence(IEnumerable<AppRule> rules) =>
        rules
            .OrderBy(rule => rule.MatchType == AppRuleMatchType.FullPath ? 0 : 1)
            .ToArray();
}
