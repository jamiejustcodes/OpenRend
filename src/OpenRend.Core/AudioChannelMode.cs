using System.Text.Json.Serialization;

namespace OpenRend.Core;

/// <summary>
/// Audio channel routing mode matching Vegas Pro ChannelRemapping (ScriptPortal.Vegas.ChannelRemapping).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AudioChannelMode>))]
public enum AudioChannelMode
{
    Both = 0,
    LeftOnly = 1,
    RightOnly = 2,
    Swap = 3
}
