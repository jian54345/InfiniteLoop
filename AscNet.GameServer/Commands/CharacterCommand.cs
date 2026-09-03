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
        "character modify <id|all> max" +
        "character modify <id|all> min";

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
        "min",
        StringComparison.OrdinalIgnoreCase))
{
    if (!string.IsNullOrWhiteSpace(Value))
    {
        throw new ArgumentException(
            "Usage: character modify <id|all> min");
    }

    ModifyMin(characters);

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
            .Where(row =>
                row.Id > 0)
            .GroupBy(row =>
                row.Id)
            .ToDictionary(
                group => group.Key,
                group => group.First());

    /*
     * 本次 max 的所有奖励通知统一累计。
     */
    RewardApplicationResult batchResult =
        new();

    foreach (CharacterData character in characters)
    {
        character.SkillList ??= new();
        character.EnhanceSkillList ??= new();
        character.MagicList ??= new();

        /*
         * 角色等级使用配置最高等级。
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
         * Quality 使用角色配置最高值。
         */
        int maxQuality =
            GetCharacterMaxQuality(
                checked((int)character.Id));

        if (maxQuality > 0)
        {
            character.Quality = maxQuality;
        }

        /*
         * Grade 使用角色配置最高值。
         */
        int maxGrade =
            GetCharacterMaxGrade(
                checked((int)character.Id));

        if (maxGrade > 0)
        {
            character.Grade = maxGrade;
        }

        /*
         * TrustLv 固定 6。
         */
        character.TrustLv = 6;
        character.TrustExp = 0;

        /*
         * Star 不修改。
         */

        /*
         * ★ 使用 ExhibitionModule 正式 GatherReward 流程
         *   逐级完成角色解放。
         */
        LiberateCharacterMax(
            character,
            batchResult);

        /*
         * 解锁品质限制技能。
         */
        session.character.UnlockQualityGatedSkills(
            character);

        /*
         * 解锁 EnhanceSkill。
         */
        if (UnlockEnhanceSkills(character))
        {
            character.IsEnhanceSkillNotice = true;
        }

        /*
         * 所有技能提升到各自配置允许的最高等级。
         */
        SetSkillLevel(
            character,
            int.MaxValue);
    }

    /*
     * 将最终角色状态加入统一 CharacterData 通知。
     */
    foreach (CharacterData character in characters)
    {
        if (batchResult.CharacterData.CharacterDataList
            .All(existing =>
                existing.Id != character.Id))
        {
            batchResult.CharacterData.CharacterDataList.Add(
                character);
        }
    }

    /*
     * 统一保存。
     */
    session.character.Save();
    session.inventory.Save();
    session.player.Save();

    /*
     * ★ 所有奖励通知最后一次性发送。
     */
    batchResult.SendPushes(
        session);
}

    private void ModifyMin(
    IReadOnlyCollection<CharacterData> characters)
{
    Dictionary<int, CharacterTable> characterRows =
        TableReaderV2
            .Parse<CharacterTable>()
            .Where(row =>
                row.Id > 0)
            .GroupBy(row =>
                row.Id)
            .ToDictionary(
                group => group.Key,
                group => group.First());

    foreach (CharacterData character in characters)
    {
        character.SkillList ??= new();
        character.EnhanceSkillList ??= new();
        character.MagicList ??= new();

        /*
         * ============================================================
         * 1. Level
         * ============================================================
         *
         * 使用 CharacterLevelUpTemplate 配置中的最低有效等级。
         */
        if (characterRows.TryGetValue(
                checked((int)character.Id),
                out CharacterTable? characterRow))
        {
            int minLevel =
                Character.characterLevelUpTemplates
                    .Where(row =>
                        row.Type ==
                            characterRow.LevelUpTemplateId
                        && row.Level > 0)
                    .Select(row =>
                        row.Level)
                    .DefaultIfEmpty(1)
                    .Min();

            character.Level = minLevel;
        }
        else
        {
            character.Level = 1;
        }

        character.Exp = 0;

        /*
         * ============================================================
         * 2. Quality
         * ============================================================
         */
        int minQuality =
            TableReaderV2
                .Parse<CharacterQualityTable>()
                .Where(row =>
                    row.CharacterId ==
                        checked((int)character.Id))
                .Select(row =>
                    row.Quality)
                .DefaultIfEmpty(1)
                .Min();

        character.Quality = minQuality;

        /*
         * ============================================================
         * 3. Grade
         * ============================================================
         */
        int minGrade =
            TableReaderV2
                .Parse<CharacterGradeTable>()
                .Where(row =>
                    row.CharacterId ==
                        checked((int)character.Id))
                .Select(row =>
                    row.Grade)
                .DefaultIfEmpty(1)
                .Min();

        character.Grade = minGrade;

        /*
         * ============================================================
         * 4. Trust
         * ============================================================
         */
        character.TrustLv = 1;
        character.TrustExp = 0;

        /*
         * ============================================================
         * 5. Star / Liberate / GatherRewards
         *
         * Star 不修改。
         * LiberateLv 不修改。
         * GatherRewards 不修改。
         * ============================================================
         */

        /*
         * ============================================================
         * 6. 普通技能恢复到“初始状态”
         * ============================================================
         *
         * 不直接 Clear 后留空。
         *
         * Character.cs 的正常角色初始化逻辑：
         *
         * CharacterSkillTable
         *      ↓
         * SkillGroupId
         *      ↓
         * CharacterSkillGroupTable
         *      ↓
         * 每个技能组的第一个 SkillId
         *      ↓
         * Level = 1
         *
         * 这里按照相同逻辑重新建立基础技能。
         */
        character.SkillList.Clear();

        var characterSkill =
            TableReaderV2
                .Parse<AscNet.Table.V2.share.character.skill.CharacterSkillTable>()
                .FirstOrDefault(row =>
                    row.CharacterId ==
                        checked((int)character.Id));

        if (characterSkill is not null)
        {
            foreach (int skillGroupId in
                characterSkill.SkillGroupId
                    .Where(id =>
                        id > 0)
                    .Distinct())
            {
                var skillGroup =
                    TableReaderV2
                        .Parse<AscNet.Table.V2.share.character.skill.CharacterSkillGroupTable>()
                        .FirstOrDefault(row =>
                            row.Id ==
                                skillGroupId);

                if (skillGroup is null)
                {
                    continue;
                }

                int skillId =
                    skillGroup.SkillId
                        .FirstOrDefault();

                if (skillId <= 0)
                {
                    continue;
                }

                character.SkillList.Add(
                    new()
                    {
                        Id = checked((uint)skillId),
                        Level = 1
                    });
            }
        }

        /*
         * ============================================================
         * 7. EnhanceSkill 全部恢复锁定
         * ============================================================
         *
         * Character.cs 的 NormalizeEnhanceSkillsForCharacter()
         * 明确规定：
         *
         * “Never grants locked groups”
         *
         * 因此清空后就是锁定状态。
         */
        character.EnhanceSkillList.Clear();

        /*
         * ============================================================
         * 8. MagicSkill 恢复为空
         * ============================================================
         */
        character.MagicList.Clear();

        /*
         * ============================================================
         * 9. EnhanceSkill 提示状态清除
         * ============================================================
         */
        character.IsEnhanceSkillNotice = false;
    }

    SaveAndNotify(
        characters);
}
	private void LiberateCharacterMax(
    CharacterData character,
    RewardApplicationResult batchResult)
{
    List<ExhibitionRewardTable> rewards =
        TableReaderV2
            .Parse<ExhibitionRewardTable>()
            .Where(row =>
                row.CharacterId ==
                    checked((int)character.Id))
            .OrderBy(row =>
                row.LevelId)
            .ThenBy(row =>
                row.Id)
            .ToList();

    foreach (ExhibitionRewardTable reward in rewards)
    {
        /*
         * 如果已经领取过：
         *
         * 不重复发奖励。
         * 但仍然同步 LiberateLv。
         */
        if (session.player.GatherRewards.Contains(
                reward.Id))
        {
            character.LiberateLv =
                Math.Max(
                    character.LiberateLv,
                    reward.LevelId);

            continue;
        }

        /*
         * GM 专用 Exhibition 流程。
         *
         * 这里不会检查普通玩家的
         * Ability / Resonance / Memory
         * 等解放条件。
         */
        if (!ExhibitionModule.PrepareGatherRewardForCommand(
                session,
                reward,
                out CharacterData? rewardCharacter,
                out RewardApplicationResult? result))
        {
            continue;
        }

        /*
         * 合并奖励产生的所有通知。
         */
        if (result is not null)
        {
            batchResult.AddPushes(
                result);
        }

        /*
         * 确保角色最终状态进入统一通知。
         */
        if (rewardCharacter is not null
            && batchResult.CharacterData.CharacterDataList
                .All(existing =>
                    existing.Id != rewardCharacter.Id))
        {
            batchResult.CharacterData.CharacterDataList.Add(
                rewardCharacter);
        }
    }
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
