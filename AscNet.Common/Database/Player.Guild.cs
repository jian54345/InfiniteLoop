using MongoDB.Bson.Serialization.Attributes;

namespace AscNet.Common.Database;

public partial class Player
{
    // Historical join/create event receipt; pending applications never change this field.
    [BsonElement("guild_progress_recorded_id")]
    public uint GuildProgressRecordedId { get; set; }
}
