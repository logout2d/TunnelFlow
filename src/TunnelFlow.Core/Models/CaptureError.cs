namespace TunnelFlow.Core.Models;

/// <summary>
/// Describes a non-fatal capture-layer failure (for example driver disconnect) surfaced through <see cref="ICaptureEngine"/>.
/// </summary>
public record CaptureError
{
    public string? Code { get; init; }

    public required string Message { get; init; }
}
