using TunnelFlow.UI.Services;
using TunnelFlow.UI.ViewModels;

namespace TunnelFlow.Tests.UI;

public class LogViewModelTests
{
    [Fact]
    public void AddLine_WritesUiVisibleLineToAppLocalLogFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TunnelFlow-UiLogTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var sink = new UiFileLogSink(tempDir);
            var viewModel = new LogViewModel(sink);

            viewModel.AddLine("ui", "Info", "Hello log file");

            var logPath = Path.Combine(tempDir, "logs", "ui.log");
            Assert.True(File.Exists(logPath));

            var content = File.ReadAllText(logPath);
            Assert.Contains("[ui] Info: Hello log file", content);
            Assert.Single(viewModel.Lines);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for focused UI log tests.
            }
        }
    }

    [Fact]
    public void AddLine_WhenBaseDirectoryIsSystemSubfolder_WritesToSharedPortableLogsRoot()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TunnelFlow-UiLogTests", Guid.NewGuid().ToString("N"));
        var systemDir = Path.Combine(tempDir, "system");
        Directory.CreateDirectory(systemDir);

        try
        {
            var sink = new UiFileLogSink(systemDir);
            var viewModel = new LogViewModel(sink);

            viewModel.AddLine("ui", "Info", "Hello shared root");

            var logPath = Path.Combine(tempDir, "logs", "ui.log");
            Assert.True(File.Exists(logPath));
            Assert.DoesNotContain(Path.Combine(systemDir, "logs", "ui.log"), Directory.GetFiles(tempDir, "ui.log", SearchOption.AllDirectories));
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void AddLine_WhenLogSinkThrows_StillKeepsInMemoryUiLog()
    {
        var viewModel = new LogViewModel(new ThrowingUiLogSink());

        viewModel.AddLine("ui", "Error", "Still visible");

        Assert.Single(viewModel.Lines);
        Assert.Contains("Still visible", viewModel.Lines[0].Display);
    }

    private sealed class ThrowingUiLogSink : IUiLogSink
    {
        public void WriteLine(string line) => throw new IOException("Disk unavailable");
    }
}
