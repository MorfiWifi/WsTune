using System.Text.Json.Serialization;
using WsTuneCommon.Models;

namespace WsTune.Droid;

// Source-generated JSON metadata for SignalR payloads.
// Mirrors Src/WsTuneCli.Listener/WsAllInOneJsonContext.cs so the Android
// client speaks exactly the same wire protocol as the desktop listener.
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(ForwardModelV4))]
[JsonSerializable(typeof(ForwardModel))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(DataPacket))]
[JsonSerializable(typeof(List<TunnelConfigDto>))]
[JsonSerializable(typeof(TunnelConfigDto))]
[JsonSerializable(typeof(ConnectionPacket))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(AppState))]
[JsonSerializable(typeof(ProfileEntry))]
[JsonSerializable(typeof(List<ProfileEntry>))]
public partial class WsDroidJsonContext : JsonSerializerContext;
