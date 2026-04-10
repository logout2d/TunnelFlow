using TunnelFlow.UI.ViewModels;

namespace TunnelFlow.Tests.UI;

public sealed class AboutViewModelTests
{
    [Fact]
    public void AboutViewModel_UsesReleaseVersionAndPlaceholderProjectUrl()
    {
        var viewModel = new AboutViewModel();

        Assert.Equal("Version 0.1.0", viewModel.VersionText);
        Assert.Equal("https://example.com/tunnelflow", viewModel.ProjectUrl);
    }
}
