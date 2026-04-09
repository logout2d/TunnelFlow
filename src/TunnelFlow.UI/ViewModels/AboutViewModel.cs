using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TunnelFlow.UI.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private static readonly Assembly UiAssembly = typeof(AboutViewModel).Assembly;

    public string AppName => "TunnelFlow";

    public string VersionText => $"Version {ResolveVersion()}";

    public string DescriptionText =>
        "TunnelFlow provides a simple and clear interface for working with VLESS profiles and an easy way to tunnel selected applications through a virtual adapter.";

    public string ProjectUrl => "http://www.sample.com";

    public string FooterText => "Windows desktop app for VLESS and per-app tunneling.";

    private static string ResolveVersion()
    {
        var informationalVersion = UiAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return UiAssembly.GetName().Version?.ToString() ?? "Unknown";
    }
}
