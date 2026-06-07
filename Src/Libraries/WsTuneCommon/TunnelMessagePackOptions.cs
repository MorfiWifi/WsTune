using MessagePack;

namespace WsTuneCommon;

/// <summary>
/// MessagePack options for SignalR tunnel traffic. LZ4 is disabled — binary payloads (RDP/VNC/TLS) rarely compress and LZ4 adds CPU cost.
/// </summary>
public static class TunnelMessagePackOptions
{
    public static readonly MessagePackSerializerOptions SignalR = MessagePackSerializerOptions.Standard;
}
