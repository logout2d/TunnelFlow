using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using TunnelFlow.Core.IPC.Responses;

namespace TunnelFlow.UI.ViewModels;

public partial class SessionsViewModel : ObservableObject
{
    [ObservableProperty] private bool _isAvailable = true;
    [ObservableProperty] private string _unavailableMessage =
        "Sessions are available only in legacy transparent-proxy mode.";

    public ObservableCollection<SessionItemViewModel> Sessions { get; } = [];

    public void SetMode(TunnelStatusMode selectedMode)
    {
        IsAvailable = selectedMode == TunnelStatusMode.Legacy;
    }

    public void AddSessionFromJson(JsonElement payload)
    {
        // Clone before capture: JsonElement references the underlying JsonDocument
        // which may be disposed after this call returns (background thread).
        var captured = payload.Clone();
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ulong flowId = captured.TryGetProperty("flowId", out var fi) ? fi.GetUInt64() : 0;
            string processPath = captured.TryGetProperty("processPath", out var pp) ? pp.GetString() ?? "" : "";
            string protocol = captured.TryGetProperty("protocol", out var pr) ? pr.GetString() ?? "" : "";
            string state = captured.TryGetProperty("state", out var st) ? st.GetString() ?? "" : "";

            string destination = "";
            if (captured.TryGetProperty("originalDestination", out var dest))
            {
                string addr = dest.TryGetProperty("address", out var a) ? a.GetString() ?? "" : "";
                int port = dest.TryGetProperty("port", out var p) ? p.GetInt32() : 0;
                destination = $"{addr}:{port}";
            }

            DateTime createdAt = DateTime.UtcNow;
            if (captured.TryGetProperty("createdAt", out var ca) && ca.GetString() is string caStr)
                DateTime.TryParse(caStr, out createdAt);

            Sessions.Add(new SessionItemViewModel(flowId, processPath, protocol, destination, state, createdAt));
        });
    }

    public void RemoveSession(ulong flowId)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var item = Sessions.FirstOrDefault(s => s.FlowId == flowId);
            if (item is not null) Sessions.Remove(item);
        });
    }
}

public sealed class SessionItemViewModel
{
    public ulong FlowId { get; }
    public string ProcessName { get; }
    public string Protocol { get; }
    public string Destination { get; }
    public string State { get; }
    public DateTime CreatedAt { get; }

    public SessionItemViewModel(ulong flowId, string processPath, string protocol,
        string destination, string state, DateTime createdAt)
    {
        FlowId = flowId;
        ProcessName = System.IO.Path.GetFileName(processPath);
        Protocol = protocol;
        Destination = destination;
        State = state;
        CreatedAt = createdAt;
    }
}
