using System.Reflection;
using TunnelFlow.Core;
using TunnelFlow.Service.Configuration;
using TunnelFlow.UI.Services;

namespace TunnelFlow.Tests.Core;

public class RuntimePathsTests
{
    [Fact]
    public void Create_WhenUsingFlatPortableLayout_ReturnsAppLocalRuntimeStatePaths()
    {
        var runtimePaths = RuntimePaths.Create(
            baseDirectory: @"D:\Apps\TunnelFlow",
            legacyDataRoot: @"C:\ProgramData\TunnelFlow");

        Assert.Equal(@"D:\Apps\TunnelFlow", runtimePaths.RuntimeRoot);
        Assert.Equal(Path.Combine(@"D:\Apps\TunnelFlow", "config", "config.json"), runtimePaths.CurrentConfigPath);
        Assert.Equal(Path.Combine(@"D:\Apps\TunnelFlow", "logs"), runtimePaths.CurrentLogsRoot);
        Assert.Equal(Path.Combine(@"D:\Apps\TunnelFlow", "logs", "service.log"), runtimePaths.ServiceLogPath);
        Assert.Equal(Path.Combine(@"D:\Apps\TunnelFlow", "logs", "singbox.log"), runtimePaths.SingBoxLogPath);
        Assert.Equal(Path.Combine(@"D:\Apps\TunnelFlow", "logs", "ui.log"), runtimePaths.UiLogPath);
        Assert.Equal(Path.Combine(@"D:\Apps\TunnelFlow", "config", "appsettings.json"), runtimePaths.AppSettingsPath);
        Assert.Equal(Path.Combine(@"D:\Apps\TunnelFlow", "config", "singbox_last.json"), runtimePaths.SingBoxConfigPath);
        Assert.Equal(Path.Combine(@"D:\Apps\TunnelFlow", "system", "TunnelFlow.Service.exe"), runtimePaths.ServiceExecutablePath);
        Assert.Equal(Path.Combine(@"D:\Apps\TunnelFlow", "system", "TunnelFlow.Bootstrapper.exe"), runtimePaths.BootstrapperExecutablePath);
        Assert.Equal(Path.Combine(@"D:\Apps\TunnelFlow", "core", "sing-box.exe"), runtimePaths.SingBoxExecutablePath);
        Assert.Equal(Path.Combine(@"D:\Apps\TunnelFlow", "core", "wintun.dll"), runtimePaths.WintunDllPath);
        Assert.Equal(Path.Combine(@"C:\ProgramData\TunnelFlow", "config.json"), runtimePaths.LegacyConfigPath);
    }

    [Fact]
    public void Create_WhenUsingFutureSystemLayout_UsesParentPortableRoot()
    {
        var runtimePaths = RuntimePaths.Create(
            baseDirectory: @"D:\Apps\TunnelFlow\system",
            legacyDataRoot: @"C:\ProgramData\TunnelFlow");

        Assert.Equal(@"D:\Apps\TunnelFlow", runtimePaths.RuntimeRoot);
        Assert.Equal(Path.Combine(@"D:\Apps\TunnelFlow", "config", "config.json"), runtimePaths.CurrentConfigPath);
        Assert.Equal(Path.Combine(@"D:\Apps\TunnelFlow", "logs", "service.log"), runtimePaths.ServiceLogPath);
        Assert.Equal(Path.Combine(@"D:\Apps\TunnelFlow", "logs", "ui.log"), runtimePaths.UiLogPath);
    }

    [Fact]
    public void GetExecutableCandidates_StartWithPortableAndFlatPaths()
    {
        var runtimePaths = RuntimePaths.Create(
            baseDirectory: @"D:\Apps\TunnelFlow",
            legacyDataRoot: @"C:\ProgramData\TunnelFlow");

        Assert.Equal(
            [
                Path.Combine(@"D:\Apps\TunnelFlow", "system", "TunnelFlow.Service.exe"),
                Path.Combine(@"D:\Apps\TunnelFlow", "TunnelFlow.Service.exe")
            ],
            runtimePaths.GetServiceExecutableCandidates().Take(2));

        Assert.Equal(
            [
                Path.Combine(@"D:\Apps\TunnelFlow", "system", "TunnelFlow.Bootstrapper.exe"),
                Path.Combine(@"D:\Apps\TunnelFlow", "TunnelFlow.Bootstrapper.exe")
            ],
            runtimePaths.GetBootstrapperExecutableCandidates().Take(2));

        Assert.Equal(
            [
                Path.Combine(@"D:\Apps\TunnelFlow", "core", "sing-box.exe"),
                Path.Combine(@"D:\Apps\TunnelFlow", "sing-box.exe")
            ],
            runtimePaths.GetSingBoxExecutableCandidates().Take(2));

        Assert.Equal(
            [
                Path.Combine(@"D:\Apps\TunnelFlow", "core", "wintun.dll"),
                Path.Combine(@"D:\Apps\TunnelFlow", "wintun.dll")
            ],
            runtimePaths.GetWintunDllCandidates().Take(2));
    }

    [Fact]
    public void DefaultConfigConsumers_UseRuntimePathsCurrentConfigPath()
    {
        var configStore = new ConfigStore();
        var configPathField = typeof(ConfigStore)
            .GetField("_configPath", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(configPathField);
        Assert.Equal(
            RuntimePaths.Current.CurrentConfigPath,
            Assert.IsType<string>(configPathField!.GetValue(configStore)));
        Assert.Equal(RuntimePaths.Current.CurrentConfigPath, LocalConfigSnapshotLoader.DefaultConfigPath);
        Assert.Equal(RuntimePaths.Current.LegacyConfigPath, LocalConfigSnapshotLoader.LegacyConfigPath);
    }
}
