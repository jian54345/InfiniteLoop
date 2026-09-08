using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;

namespace AscNet.Common.Database;

public partial class Player
{
    [BsonElement("purchase_last_buy_times")]
    [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
    public Dictionary<uint, long> PurchaseLastBuyTimes { get; set; } = new();

    [BsonElement("pending_purchase")]
    public PlayerPendingPurchase? PendingPurchase { get; set; }
}

public sealed class PlayerPendingPurchase
{
    public uint Id { get; set; }
    public int Count { get; set; }
    public int PreviousBuyTimes { get; set; }
    public long BuyTime { get; set; }
    public int ConsumeId { get; set; }
    public int ConsumeCount { get; set; }
    public List<AscNet.Common.MsgPack.RewardGoods> Goods { get; set; } = new();
}
