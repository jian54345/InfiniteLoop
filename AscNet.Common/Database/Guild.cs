using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AscNet.Common.Database;

public sealed class Guild
{
    public static readonly IMongoCollection<Guild> collection = Common.db.GetCollection<Guild>("guilds");
    private static readonly IMongoCollection<BsonDocument> counters = Common.db.GetCollection<BsonDocument>("guild_counters");
    private static readonly Lazy<bool> indexes = new(() =>
    {
        collection.Indexes.CreateMany(
        [
            new CreateIndexModel<Guild>(Builders<Guild>.IndexKeys.Ascending(guild => guild.Name),
                new CreateIndexOptions { Name = "guild_name", Unique = true }),
            new CreateIndexModel<Guild>(Builders<Guild>.IndexKeys.Ascending(guild => guild.MemberIds),
                new CreateIndexOptions { Name = "guild_members", Unique = true })
        ]);
        return true;
    }, LazyThreadSafetyMode.PublicationOnly);

    [BsonId]
    [BsonRepresentation(BsonType.Int64)]
    public uint Id { get; set; }

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("icon_id")]
    public int IconId { get; set; }

    [BsonElement("level")]
    public int Level { get; set; }

    [BsonElement("leader_id")]
    public long LeaderId { get; set; }

    [BsonElement("declaration")]
    public string Declaration { get; set; } = string.Empty;

    [BsonElement("option")]
    public int Option { get; set; }

    [BsonElement("min_level")]
    public int MinLevel { get; set; }

    [BsonElement("created_at")]
    public long CreatedAt { get; set; }

    [BsonElement("creation_quota_period")]
    [BsonIgnoreIfNull]
    public long? CreationQuotaPeriod { get; set; }

    [BsonElement("active")]
    public bool Active { get; set; }

    [BsonElement("member_ids")]
    public List<long> MemberIds { get; set; } = [];

    [BsonElement("applications")]
    public List<GuildApplication> Applications { get; set; } = [];

    [BsonElement("max_members")]
    public int MaxMembers { get; set; }

    [BsonElement("max_tourists")]
    public int MaxTourists { get; set; }

    public static Guild? FindByMember(long playerId) =>
        collection.Find(guild => guild.Active && guild.MemberIds.Contains(playerId)).FirstOrDefault();

    public static Guild? FindById(uint id) =>
        collection.Find(guild => guild.Id == id).FirstOrDefault();

    public static Guild? FindPendingByFounder(long id) =>
        collection.Find(guild => !guild.Active && guild.LeaderId == id).FirstOrDefault();

    public static List<Guild> AllActive() => collection.Find(guild => guild.Active).ToList();

    public static Guild ReserveCreation(Guild requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentException.ThrowIfNullOrWhiteSpace(requested.Name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requested.LeaderId);
        _ = indexes.Value;
        Guild? existing = collection.Find(guild => guild.LeaderId == requested.LeaderId).FirstOrDefault();
        if (existing is not null)
            return existing;

        requested.Id = AllocateId();
        requested.Active = false;
        requested.MemberIds = [requested.LeaderId];
        requested.Applications = [];
        requested.CreationQuotaPeriod = null;
        try
        {
            collection.InsertOne(requested);
            return requested;
        }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Another request may have reserved or activated this founder while we allocated an ID.
            existing = collection.Find(guild => guild.LeaderId == requested.LeaderId).FirstOrDefault();
            if (existing is not null)
                return existing;
            throw;
        }
    }

    public static void EnsureCreationQuota(Guild guild, long resetPeriod, int dailyLimit)
    {
        ArgumentNullException.ThrowIfNull(guild);
        ArgumentOutOfRangeException.ThrowIfNegative(resetPeriod);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dailyLimit);
        Guild persisted = FindById(guild.Id)
            ?? throw new InvalidOperationException("Guild creation reservation is missing.");
        if (persisted.LeaderId != guild.LeaderId)
            throw new InvalidOperationException("Guild creation founder does not match.");
        if (persisted.CreationQuotaPeriod is not null)
        {
            guild.CreationQuotaPeriod = persisted.CreationQuotaPeriod;
            return;
        }

        var filter = Builders<BsonDocument>.Filter;
        var period = filter.Eq("_id", $"creation:{resetPeriod}");
        try
        {
            counters.UpdateOne(period, Builders<BsonDocument>.Update.SetOnInsert("founder_ids", new BsonArray()),
                new UpdateOptions { IsUpsert = true });
        }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Another creator initialized this reset period concurrently.
        }
        var room = new BsonDocument("$expr", new BsonDocument("$lt", new BsonArray
        {
            new BsonDocument("$size", "$founder_ids"), dailyLimit
        }));
        UpdateResult quota = counters.UpdateOne(
            period & (filter.AnyEq("founder_ids", guild.LeaderId) | room),
            Builders<BsonDocument>.Update.AddToSet("founder_ids", guild.LeaderId));
        if (quota.MatchedCount == 0)
            throw new InvalidOperationException("GuildCreateReachDailyLimit");

        // A crash here can conservatively consume a second day's slot on retry; neither
        // day's cap can be exceeded. Never remove a slot another creator may be using.
        Guild? recorded = collection.FindOneAndUpdate(
            Builders<Guild>.Filter.Where(value => value.Id == guild.Id && value.LeaderId == guild.LeaderId && value.CreationQuotaPeriod == null),
            Builders<Guild>.Update.Set(value => value.CreationQuotaPeriod, (long?)resetPeriod),
            new FindOneAndUpdateOptions<Guild> { ReturnDocument = ReturnDocument.After });
        guild.CreationQuotaPeriod = recorded?.CreationQuotaPeriod ?? FindById(guild.Id)?.CreationQuotaPeriod
            ?? throw new InvalidOperationException("Guild creation reservation is missing.");
    }

    private static uint AllocateId()
    {
        // EN XUiGuildRecommendation.lua:182-185 accepts exactly eight digits.
        const long minimum = 10_000_000;
        const long maximum = 99_999_999;
        var filter = Builders<BsonDocument>.Filter.Eq("_id", "guild");
        try
        {
            counters.UpdateOne(filter, Builders<BsonDocument>.Update.Max("sequence", minimum - 1),
                new UpdateOptions { IsUpsert = true });
        }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Another allocator initialized the counter concurrently.
        }
        BsonDocument? counter = counters.FindOneAndUpdate(
            filter & Builders<BsonDocument>.Filter.Lt("sequence", maximum),
            Builders<BsonDocument>.Update.Inc("sequence", 1L),
            new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After });
        if (counter is null)
            throw new InvalidOperationException("The eight-digit guild ID range is exhausted.");
        return checked((uint)counter["sequence"].AsInt64);
    }

    public static Guild? Activate(uint id, long founderId)
    {
        _ = indexes.Value;
        return collection.FindOneAndUpdate(
            Builders<Guild>.Filter.Where(guild => guild.Id == id && guild.LeaderId == founderId && guild.MemberIds.Contains(founderId)
                && guild.CreationQuotaPeriod != null),
            Builders<Guild>.Update.Set(guild => guild.Active, true),
            new FindOneAndUpdateOptions<Guild> { ReturnDocument = ReturnDocument.After });
    }

    public static Guild? TryAdmit(uint id, long playerId, int capacity, long? applicationCutoff)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerId);
        if (capacity <= 0)
            return null;
        _ = indexes.Value;
        var filter = Builders<Guild>.Filter;
        // Existing membership is a successful retry even if the guild has since filled up.
        var room = new BsonDocument("$expr", new BsonDocument("$lt", new BsonArray
        {
            new BsonDocument("$size", "$member_ids"), capacity
        }));
        // EN XGuildConfig.ApplySetting.NoneApply = 1; approval requires a still-live application.
        var admission = applicationCutoff is long cutoff
            ? filter.ElemMatch(guild => guild.Applications,
                application => application.PlayerId == playerId && application.CreatedAt > cutoff)
            : filter.Eq(guild => guild.Option, 1);
        try
        {
            return collection.FindOneAndUpdate(
                filter.Eq(guild => guild.Id, id) & filter.Eq(guild => guild.Active, true) &
                (filter.AnyEq(guild => guild.MemberIds, playerId) | (admission & room)),
                Builders<Guild>.Update.AddToSet(guild => guild.MemberIds, playerId)
                    .PullFilter(guild => guild.Applications, application => application.PlayerId == playerId),
                new FindOneAndUpdateOptions<Guild> { ReturnDocument = ReturnDocument.After });
        }
        catch (MongoCommandException exception) when (exception.Code == 11000)
        {
            return null;
        }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return null;
        }
    }

    public static Guild? AddApplication(uint id, long playerId, long now, int maxCount, long timeoutSecs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerId);
        ArgumentOutOfRangeException.ThrowIfNegative(now);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutSecs);
        _ = indexes.Value;
        long cutoff = checked(now - timeoutSecs);
        var liveApplications = new BsonDocument("$filter", new BsonDocument
        {
            { "input", "$applications" },
            { "as", "application" },
            { "cond", new BsonDocument("$gt", new BsonArray { "$$application.created_at", cutoff }) }
        });
        var filter = Builders<Guild>.Filter;
        var room = new BsonDocument("$expr", new BsonDocument("$lt", new BsonArray
        {
            new BsonDocument("$size", liveApplications), maxCount
        }));
        var existing = filter.ElemMatch(guild => guild.Applications,
            application => application.PlayerId == playerId && application.CreatedAt > cutoff);
        PipelineDefinition<Guild, Guild> pipeline = new BsonDocument[]
        {
            new("$set", new BsonDocument("applications", liveApplications)),
            new("$set", new BsonDocument("applications", new BsonDocument("$cond", new BsonArray
            {
                new BsonDocument("$in", new BsonArray { playerId, "$applications.player_id" }),
                "$applications",
                new BsonDocument("$concatArrays", new BsonArray
                {
                    "$applications",
                    new BsonArray { new BsonDocument { { "player_id", playerId }, { "created_at", now } } }
                })
            })))
        };
        return collection.FindOneAndUpdate(
            filter.Where(guild => guild.Id == id && guild.Active && !guild.MemberIds.Contains(playerId)) &
                (existing | room),
            Builders<Guild>.Update.Pipeline(pipeline),
            new FindOneAndUpdateOptions<Guild> { ReturnDocument = ReturnDocument.After });
    }

    public static Guild? RemoveApplication(uint id, long playerId)
    {
        _ = indexes.Value;
        return collection.FindOneAndUpdate(Builders<Guild>.Filter.Where(guild => guild.Id == id && guild.Active),
            Builders<Guild>.Update.PullFilter(guild => guild.Applications, application => application.PlayerId == playerId),
            new FindOneAndUpdateOptions<Guild> { ReturnDocument = ReturnDocument.After });
    }
}

public sealed class GuildApplication
{
    [BsonElement("player_id")]
    public long PlayerId { get; set; }

    [BsonElement("created_at")]
    public long CreatedAt { get; set; }
}
