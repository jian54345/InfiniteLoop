using System.Globalization;
using AscNet.Common.Database;
using AscNet.Common.MsgPack;
using AscNet.Common.Util;
using AscNet.GameServer.Handlers;
using AscNet.Table.V2.share.character;
using AscNet.Table.V2.share.character.enhanceskill;
using AscNet.Table.V2.share.character.grade;
using AscNet.Table.V2.share.character.quality;
using AscNet.Table.V2.share.exhibition;
namespace AscNet.GameServer.Commands;

[CommandName("character")]
internal sealed class CharacterCommand : Command
{
    public CharacterCommand(
        Session session,
        string[] args,
        bool validate = true)
        : base(session, args, false)
    {
        Op = args.Length > 0
            ? args[0]
            : string.Empty;

        Target = args.Length > 1
            ? args[1]
            : string.Empty;

        ModifyType = args.Length > 2
            ? args[2]
            : string.Empty;

        Value = args.Length > 3
            ? args[3]
            : string.Empty;
    }

    public override string Help =>
        "Usage:\n" +
        "character add <id|all>\n" +
        "character modify <id|all> level <value>\n" +
        "character modify <id|all> max";

    private string Op { get; set; } = string.Empty;

    private string Target { get; set; } = string.Empty;

    private string ModifyType { get; set; } = string.Empty;

    private string Value { get; set; } = string.Empty;

    public override void Execute()
    {
        if (string.IsNullOrWhiteSpace(Op)
            || string.IsNullOrWhiteSpace(Target))
        {
            throw new ArgumentException(
                Help);
        }

        switch (Op.ToLowerInvariant())
        {
            case "add":
                AddCharacter();
                break;

            case "modify":
                ModifyCharacter();
                break;

            default:
                throw new ArgumentException(
                    Help);
        }
    }

    private void AddCharacter()
    {
        if (Target.Equals(
                "all",
                StringComparison.OrdinalIgnoreCase))
        {
            HashSet<uint> ownedCharacterIds =
                session.character.Characters
                    .Select(character =>
                        character.Id)
                    .ToHashSet();

            IEnumerable<Reward> rewards =
                TableReaderV2
                    .Parse<CharacterTable>()
                    .Where(character =>
                        character.Id > 0
                        && Character.IsOwnableCharacter(
                            checked((uint)character.Id))
                        && !ownedCharacterIds.Contains(
                            checked((uint)character.Id)))
                    .Select(character =>
                        new Reward
                        {
                            Id = character.Id,
                            Type = RewardType.Character
                        });

            RewardHandler.GiveRewards(
                rewards,
                session);

            return;
        }

        if (!TryParseCharacterId(
                Target,
                out uint characterId))
        {
            throw new ArgumentException(
                "Invalid character ID!");
        }

        CharacterTable? characterTable =
            TableReaderV2
                .Parse<CharacterTable>()
                .FirstOrDefault(row =>
                    row.Id == checked((int)characterId));

        if (characterTable is null
            || !Character.IsOwnableCharacter(
                characterId))
        {
            throw new ArgumentException(
                "Character ID was not found or is not ownable!");
        }

        RewardHandler.GiveRewards(
            [
                new Reward
                {
                    Id = checked((int)characterId),
                    Type = RewardType.Character
                }
            ],
            session);
    }

    private void ModifyCharacter()
    {
        CharacterData[] characters =
            GetTargetCharacters();

        if (ModifyType.Equals(
                "max",
                StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(Value))
            {
                throw new ArgumentException(
                    "Usage: character modify <id|all> max");
            }

            ModifyMax(
                characters);

            return;
        }

        if (ModifyType.Equals(
                "level",
                StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParsePositiveInt(
                    Value,
                    out int level))
            {
                throw new ArgumentException(
                    "Usage: character modify <id|all> level <value>");
            }

            foreach (CharacterData character in characters)
            {
                character.Level = level;
                character.Exp = 0;
            }

            SaveAndNotify(
                characters);

            return;
        }

        throw new ArgumentException(
            Help);
    }

    private void ModifyMax(
    IReadOnlyCollection<CharacterData> characters)
{
    Dictionary<int, CharacterTable> characterRows =
        TableReaderV2
            .Parse<CharacterTable>()
            .Where(row => row.Id > 0)
            .GroupBy(row => row.Id)
            .ToDictionary(
                group => group.Key,
                group => group.First());

    foreach (CharacterData character in characters)
    {
        character.SkillList ??= [];
        character.EnhanceSkillList ??= [];
        character.MagicList ??= [];

        /*
         * Level
         */
        if (characterRows.TryGetValue(
                checked((int)character.Id),
                out CharacterTable? characterRow))
        {
            int maxLevel =
                Character.GetCharacterMaxLevel(
                    characterRow.LevelUpTemplateId);

            if (maxLevel > 0)
            {
                character.Level = maxLevel;
            }
        }

        character.Exp = 0;

        /*
         * Quality
         */
        character.Quality =
            GetCharacterMaxQuality(
                checked((int)character.Id));

        /*
         * Grade
         */
        character.Grade =
            GetCharacterMaxGrade(
                checked((int)character.Id));

        /*
         * Trust
         */
        character.TrustLv = 6;
        character.TrustExp = 0;

        /*
         * Star 不修改
         */

        /*
         * LiberateLv
         *
         * 一次性处理所有未领取阶段。
         */
        LiberateCharacterMax(
            character);

        /*
         * Quality 技能
         */
        session.character.UnlockQualityGatedSkills(
            character);

        /*
         * Enhance 技能
         */
        if (UnlockEnhanceSkills(character))
        {
            character.IsEnhanceSkillNotice = true;
        }

        /*
         * 技能配置最高
         */
        SetSkillLevel(
            character,
            int.MaxValue);
    }

    /*
     * 最后统一保存。
     */
    session.character.Save();
}

    private static int GetCharacterMaxQuality(
        int characterId)
    {
        return TableReaderV2
            .Parse<CharacterQualityTable>()
            .Where(row =>
                row.CharacterId == characterId)
            .Select(row =>
                row.Quality)
            .DefaultIfEmpty(1)
            .Max();
    }

    private static int GetCharacterMaxGrade(
        int characterId)
    {
        return TableReaderV2
            .Parse<CharacterGradeTable>()
            .Where(row =>
                row.CharacterId == characterId)
            .Select(row =>
                row.Grade)
            .DefaultIfEmpty(1)
            .Max();
    }

    private void LiberateCharacterMax(
    CharacterData character)
{
    List<ExhibitionRewardTable> rewards =
        TableReaderV2
            .Parse<ExhibitionRewardTable>()
            .Where(row =>
                row.CharacterId ==
                    checked((int)character.Id)
                && row.LevelId >
                    character.LiberateLv)
            .OrderBy(row =>
                row.LevelId)
            .ToList();

    if (rewards.Count == 0)
    {
        return;
    }

    List<int> gatheredRewardIds = [];

    foreach (ExhibitionRewardTable reward in rewards)
    {
        /*
         * 只处理当前角色尚未领取的解放奖励。
         */
        if (session.player.IsGatherRewardReceived(
                reward.Id))
        {
            continue;
        }

        /*
         * 发放该阶段奖励。
         *
         * 这里直接调用服务器现有 RewardHandler，
         * 不伪造 GatherRewardRequest。
         */
        if (reward.RewardId > 0)
        {
            List<RewardGoods> rewardGoods =
                RewardHandler.GetRewardGoods(
                    reward.RewardId);

            if (rewardGoods.Count > 0)
            {
                RewardHandler.GiveRewards(
                    rewardGoods,
                    session);
            }
        }

        /*
         * 标记 GatherReward 已领取。
         */
        session.player.AddGatherReward(
            reward.Id);

        gatheredRewardIds.Add(
            reward.Id);

        /*
         * 角色解放等级提升。
         */
        character.LiberateLv =
            Math.Max(
                character.LiberateLv,
                reward.LevelId);
    }

    if (gatheredRewardIds.Count == 0)
    {
        return;
    }

    /*
     * 保存一次。
     */
    session.player.Save();
    session.character.Save();

    /*
     * 所有解放阶段处理完成后，
     * 只发送一次角色数据。
     */
    session.SendPush(
        new NotifyCharacterDataList
        {
            CharacterDataList =
            {
                character
            }
        });

    /*
     * 一次性发送所有 GatherReward 状态。
     */
    foreach (int rewardId in gatheredRewardIds)
    {
        session.SendPush(
            new NotifyGatherReward
            {
                Id = rewardId
            });
    }
}
	
	private static void SetSkillLevel(
        CharacterData character,
        int requestedLevel)
    {
        character.SkillList ??= new();
        character.EnhanceSkillList ??= new();
        character.MagicList ??= new();

        requestedLevel =
            Math.Max(
                requestedLevel,
                1);

        SetSkillListLevel(
            character.SkillList,
            requestedLevel,
            Character.CharacterSkillMaxLevel);

        SetSkillListLevel(
            character.EnhanceSkillList,
            requestedLevel,
            Character.EnhanceSkillMaxLevel);

        SetSkillListLevel(
            character.MagicList,
            requestedLevel,
            Character.CharacterSkillMaxLevel);
    }

    private static void SetSkillListLevel(
        IEnumerable<CharacterSkill>? skills,
        int requestedLevel,
        Func<int, int> getMaxLevel)
    {
        if (skills is null)
        {
            return;
        }

        foreach (CharacterSkill skill in skills)
        {
            if (skill is null || skill.Id == 0)
            {
                continue;
            }

            int maxLevel =
                getMaxLevel(
                    checked((int)skill.Id));

            if (maxLevel <= 0)
            {
                continue;
            }

            skill.Level =
                Math.Clamp(
                    requestedLevel,
                    1,
                    maxLevel);
        }
    }

    private static bool UnlockEnhanceSkills(
        CharacterData character)
    {
        character.EnhanceSkillList ??= new();

        int characterId =
            checked((int)character.Id);

        HashSet<int> groupIds =
            TableReaderV2
                .Parse<EnhanceSkillTable>()
                .Where(row =>
                    row.CharacterId == characterId)
                .SelectMany(row =>
                    row.SkillGroupId)
                .Where(groupId =>
                    groupId > 0)
                .ToHashSet();

        List<EnhanceSkillGroupTable> groups =
            TableReaderV2
                .Parse<EnhanceSkillGroupTable>()
                .Where(row =>
                    groupIds.Contains(row.Id))
                .ToList();

        bool changed = false;

        foreach (EnhanceSkillGroupTable group in groups)
        {
            int skillId =
                group.SkillId
                    .Where(id =>
                        id > 0)
                    .Distinct()
                    .FirstOrDefault();

            if (skillId <= 0)
            {
                continue;
            }

            if (character.EnhanceSkillList.Any(
                    skill =>
                        skill.Id == (uint)skillId))
            {
                continue;
            }

            if (Character.OrderedEnhanceSkillUpgrades(
                    skillId).Count == 0)
            {
                continue;
            }

            character.EnhanceSkillList.Add(
                new CharacterSkill
                {
                    Id = (uint)skillId,
                    Level = 1
                });

            changed = true;
        }

        return changed;
    }

    private CharacterData[] GetTargetCharacters()
    {
        if (Target.Equals(
                "all",
                StringComparison.OrdinalIgnoreCase))
        {
            CharacterData[] characters =
                session.character.Characters
                    .Where(character =>
                        character is not null)
                    .ToArray();

            if (characters.Length == 0)
            {
                throw new ArgumentException(
                    "No owned characters were found!");
            }

            return characters;
        }

        if (!TryParseCharacterId(
                Target,
                out uint characterId))
        {
            throw new ArgumentException(
                "Invalid character ID!");
        }

        CharacterData[] target =
            session.character.Characters
                .Where(character =>
                    character.Id == characterId)
                .ToArray();

        if (target.Length == 0)
        {
            throw new ArgumentException(
                "Target character was not found!");
        }

        return target;
    }

    private void SaveAndNotify(
        IEnumerable<CharacterData> characters)
    {
        CharacterData[] target =
            characters
                .Where(character =>
                    character is not null)
                .ToArray();

        if (target.Length == 0)
        {
            throw new ArgumentException(
                "No characters modified!");
        }

        session.character.Save();

        NotifyCharacterDataList notify =
            new();

        foreach (CharacterData character in target)
        {
            notify.CharacterDataList.Add(
                character);
        }

        session.SendPush(
            notify);
    }

    private static bool TryParseCharacterId(
        string value,
        out uint characterId)
    {
        return uint.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out characterId)
            && characterId > 0;
    }

    private static bool TryParsePositiveInt(
        string value,
        out int result)
    {
        return int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out result)
            && result > 0;
    }
}