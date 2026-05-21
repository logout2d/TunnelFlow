using TunnelFlow.UI.ViewModels;

namespace TunnelFlow.Tests.UI;

public sealed class AboutViewModelTests
{
    [Fact]
    public void AboutViewModel_UsesReleaseVersionAndRepositoryProjectUrl()
    {
        var viewModel = new AboutViewModel();

        Assert.Equal("Version 1.0.1", viewModel.VersionText);
        Assert.Equal("https://github.com/logout2d/TunnelFlow", viewModel.ProjectUrl);
    }
}
