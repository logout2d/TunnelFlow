using System.Net;
using TunnelFlow.Capture.TcpRedirect;
using TunnelFlow.Capture.TcpRedirect.Interop;

namespace TunnelFlow.Tests.Capture;

public class WfpNativeInteropTests
{
    [Fact]
    public void ApplyEnvironmentDefaults_UsesConfiguredTestProcessAndRelayEndpoint()
    {
        const string testProcessPath = @"C:\Apps\Floorp\floorp.exe";
        const string relayAddress = "192.168.1.10";
        const string relayPort = "2070";

        string? previousProcessPath = Environment.GetEnvironmentVariable(WfpNativeInterop.TestProcessPathEnvVar);
        string? previousRelayAddress = Environment.GetEnvironmentVariable(WfpNativeInterop.RelayAddressEnvVar);
        string? previousRelayPort = Environment.GetEnvironmentVariable(WfpNativeInterop.RelayPortEnvVar);
        string? previousDetailedLogging = Environment.GetEnvironmentVariable(WfpNativeInterop.DetailedLoggingEnvVar);

        try
        {
            Environment.SetEnvironmentVariable(WfpNativeInterop.TestProcessPathEnvVar, testProcessPath);
            Environment.SetEnvironmentVariable(WfpNativeInterop.RelayAddressEnvVar, relayAddress);
            Environment.SetEnvironmentVariable(WfpNativeInterop.RelayPortEnvVar, relayPort);
            Environment.SetEnvironmentVariable(WfpNativeInterop.DetailedLoggingEnvVar, "true");

            var config = WfpNativeInterop.ApplyEnvironmentDefaults(new WfpRedirectConfig
            {
                UseWfpTcpRedirect = true
            });

            Assert.Equal(testProcessPath, config.TestProcessPath);
            Assert.Equal(new IPEndPoint(IPAddress.Parse(relayAddress), 2070), config.RelayEndpoint);
            Assert.True(config.EnableDetailedLogging);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WfpNativeInterop.TestProcessPathEnvVar, previousProcessPath);
            Environment.SetEnvironmentVariable(WfpNativeInterop.RelayAddressEnvVar, previousRelayAddress);
            Environment.SetEnvironmentVariable(WfpNativeInterop.RelayPortEnvVar, previousRelayPort);
            Environment.SetEnvironmentVariable(WfpNativeInterop.DetailedLoggingEnvVar, previousDetailedLogging);
        }
    }
}
