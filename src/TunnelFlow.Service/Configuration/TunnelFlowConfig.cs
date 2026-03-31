using TunnelFlow.Core.Models;

namespace TunnelFlow.Service.Configuration;

public class TunnelFlowConfig
{
    public List<AppRule> Rules { get; set; } = [];
    public List<VlessProfile> Profiles { get; set; } = [];
    public Guid? ActiveProfileId { get; set; }
    public int SocksPort { get; set; } = 2080;
    public bool StartCaptureOnServiceStart { get; set; } = false;
    public bool UseWfpTcpRedirect { get; set; } = false;
}
