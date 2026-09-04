using AscNet.Common;
using AscNet.Common.Database;
using AscNet.Common.MsgPack;
using AscNet.Common.Util;
using AscNet.GameServer.Handlers;
using AscNet.Table.V2.share.equip;
using AscNet.Table.V2.share.partner;

namespace AscNet.GameServer.Commands;

[CommandName("equip")]
internal class EquipCommand : Command
{
    public EquipCommand(
        Session session,
        string[] args,
        bool validate = true)
        : base(session, args, validate)
    {
    }

    public override string Help =>
        "Usage:\n" +
        "equip add <id|all>\n" +
        "equip prune\n" +
        "equip sync\n" +
        "equip modify <id|all> level <value|max>";

    [Argument(
        0,
        @"^add$|^prune$|^sync$|^modify$",
        "The operation selected",
        ArgumentFlags.IgnoreCase)]
    private string Op { get; set; } = string.Empty;

    [Argument(
        1,
        @"^[0-9]+$|^all$",
        "The target equip id or all",
        ArgumentFlags.IgnoreCase | ArgumentFlags.Optional)]
    private string Target { get; set; } = string.Empty;

    [Argument(
        2,
        @"^level$",
        "The modify operation",
        ArgumentFlags.IgnoreCase | ArgumentFlags.Optional)]
    private string ModifyType { get; set; } = string.Empty;

    [Argument(
        3,
        @"^[0-9]+$|^max$",
        "The value",
        ArgumentFlags.IgnoreCase | ArgumentFlags.Optional)]
    private string Value { get; set; } = string.Empty;

    public override void Execute()
    {
        if (Op.Equals(
                "sync",
                StringComparison.OrdinalIgnoreCase))
        {
            SyncEquipsFromDatabase();
            return;
        }

        if (Op.Equals(
                "modify",
                StringComparison.OrdinalIgnoreCase))
        {
            ModifyEquip();
            return;
        }

        NotifyEquipDataList notifyEquipData = new();

        switch (Op.ToLowerInvariant())
        {
            case "add":
                if (Target.Equals(
                        "all",
                        StringComparison.OrdinalIgnoreCase))
                {
                    HashSet<uint> ownedTemplateIds =
                        session.character.Equips
                            .Select(x => x.TemplateId)
                            .ToHashSet();

                    foreach (EquipTable equip in
                             TableReaderV2.Parse<EquipTable>()
                                 .Where(x =>
                                     !ownedTemplateIds.Contains(
                                         (uint)x.Id)))
                    {
                        EquipData? newEquip =
                            session.character.AddEquip(
                                (uint)equip.Id);

                        if (newEquip is not null)
                        {
                            ownedTemplateIds.Add(
                                newEquip.TemplateId);

                            notifyEquipData.EquipDataList.Add(
                                newEquip);
                        }
                    }
                }
                else
                {
                    EquipTable equip =
                        TableReaderV2.Parse<EquipTable>()
                            .Find(x =>
                                x.Id ==
                                Miscs.ParseIntOr(Target))
                        ?? throw new ServerCodeException(
                            "Equip by id not found",
                            20021001);

                    EquipData? newEquip =
                        session.character.AddEquip(
                            (uint)equip.Id);

                    if (newEquip is not null)
                    {
                        notifyEquipData.EquipDataList.Add(
                            newEquip);
                    }
                }

                break;

            case "prune":
                PruneDuplicateWeapons(
                    notifyEquipData);
                break;

            default:
                throw new InvalidOperationException(
                    "Invalid operation!");
        }

        if (notifyEquipData.EquipDataList.Count > 0
            || notifyEquipData.DeletedEquipIdList.Count > 0)
        {
            session.character.SaveChecked();
        }

        session.SendPush(
            notifyEquipData);
    }

    private void ModifyEquip()
    {
        EquipData[] equips =
            GetTargetEquips();

        if (!ModifyType.Equals(
                "level",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                Help);
        }

        bool isMax =
            Value.Equals(
                "max",
                StringComparison.OrdinalIgnoreCase);

        if (isMax)
        {
            foreach (EquipData equip in equips)
            {
                SetEquipMaxLevel(
                    equip);
            }

            /*
             * /equip modify all level max
             *
             * 同时把所有 Partner 按照
             * PartnerBreakThroughTable 配置
             * 自动升级 + 突破到最高。
             */
            if (Target.Equals(
                    "all",
                    StringComparison.OrdinalIgnoreCase))
            {
                MaxAllPartners();
            }
        }
        else if (int.TryParse(
                     Value,
                     out int level)
                 && level > 0)
        {
            foreach (EquipData equip in equips)
            {
                SetEquipLevel(
                    equip,
                    level);
            }

            /*
             * /equip modify all level <value>
             *
             * Partner 只在 all 模式下同步修改。
             * 不超过当前突破阶段的 LevelLimit。
             */
            if (Target.Equals(
                    "all",
                    StringComparison.OrdinalIgnoreCase))
            {
                SetAllPartnerLevel(
                    level);
            }
        }
        else
        {
            throw new ArgumentException(
                "Usage: equip modify <id|all> level <value|max>");
        }

        SaveAndNotify(
            equips,
            Target.Equals(
                "all",
                StringComparison.OrdinalIgnoreCase));
    }

    private EquipData[] GetTargetEquips()
    {
        if (Target.Equals(
                "all",
                StringComparison.OrdinalIgnoreCase))
        {
            EquipData[] equips =
                session.character.Equips
                    .Where(x => x is not null)
                    .ToArray();

            if (equips.Length == 0)
            {
                throw new ArgumentException(
                    "No equips found!");
            }

            return equips;
        }

        uint equipId =
            checked((uint)Miscs.ParseIntOr(Target));

        EquipData[] target =
            session.character.Equips
                .Where(x =>
                    x.Id == equipId)
                .ToArray();

        if (target.Length == 0)
        {
            throw new ArgumentException(
                "Target equip was not found!");
        }

        return target;
    }

    private static void SetEquipLevel(
        EquipData equip,
        int level)
    {
        equip.Level =
            Math.Max(
                level,
                1);

        equip.Exp = 0;
    }

    private static void SetEquipMaxLevel(
        EquipData equip)
    {
        EquipBreakThroughTable? breakthrough =
            Character.ResolveEquipBreakThrough(
                equip.TemplateId,
                equip.Breakthrough);

        if (breakthrough is null)
        {
            return;
        }

        equip.Level =
            Math.Max(
                breakthrough.LevelLimit,
                1);

        equip.Exp = 0;
    }

    private void SetAllPartnerLevel(
    int level)
{
    if (session.character.Partners is null)
    {
        return;
    }

    foreach (PartnerData partner in
             session.character.Partners)
    {
        partner.Level =
            Math.Max(
                level,
                1);

        partner.Exp = 0;
    }
}

    /*
     * Partner Max。
     *
     * 每一阶段：
     *
     * 1. 当前突破阶段等级 → LevelLimit
     * 2. 如果存在下一突破配置：
     *      BreakThrough++
     *      Level = 1
     * 3. 继续下一阶段
     *
     * 不消耗突破材料。
     */
    private void MaxAllPartners()
    {
        if (session.character.Partners is null)
        {
            return;
        }

        foreach (PartnerData partner in
                 session.character.Partners)
        {
            MaxPartner(
                partner);
        }
    }

    private static void MaxPartner(
        PartnerData partner)
    {
        while (true)
        {
            PartnerBreakThroughTable? current =
                FindPartnerBreakThrough(
                    partner);

            if (current is null)
            {
                return;
            }

            /*
             * 当前突破阶段达到配置最高等级。
             */
            partner.Level =
                Math.Max(
                    current.LevelLimit,
                    1);

            partner.Exp = 0;

            /*
             * 查找下一突破阶段。
             */
            PartnerBreakThroughTable? next =
                TableReaderV2
                    .Parse<PartnerBreakThroughTable>()
                    .Where(row =>
                        row.PartnerId ==
                            partner.TemplateId
                        && row.BreakTimes >
                            partner.BreakThrough)
                    .OrderBy(row =>
                        row.BreakTimes)
                    .FirstOrDefault();

            /*
             * 没有下一阶段，
             * 当前就是配置最高突破。
             */
            if (next is null)
            {
                return;
            }

            /*
             * GM max 不消耗突破材料。
             *
             * 正常 PartnerBreakThroughRequest
             * 是在满足材料和等级条件后：
             *
             * BreakThrough++
             * Level = 1
             * Exp = 0
             */
            partner.BreakThrough =
                next.BreakTimes;

            partner.Level = 1;
            partner.Exp = 0;
        }
    }

    private static PartnerBreakThroughTable?
        FindPartnerBreakThrough(
            PartnerData partner)
    {
        return TableReaderV2
            .Parse<PartnerBreakThroughTable>()
            .FirstOrDefault(row =>
                row.PartnerId ==
                    partner.TemplateId
                && row.BreakTimes ==
                    partner.BreakThrough);
    }

    private void SaveAndNotify(
        IEnumerable<EquipData> equips,
        bool notifyPartners)
    {
        EquipData[] target =
            equips
                .Where(x => x is not null)
                .ToArray();

        if (target.Length == 0)
        {
            throw new ArgumentException(
                "No equips modified!");
        }

        session.character.SaveChecked();

        /*
         * 装备数据刷新。
         */
        NotifyEquipDataList equipNotify =
            new();

        foreach (EquipData equip in target)
        {
            equipNotify.EquipDataList.Add(
                equip);
        }

        session.SendPush(
            equipNotify);

        /*
         * 刷新装备所绑定角色的数据。
         */
        int[] characterIds =
            target
                .Where(x =>
                    x.CharacterId > 0)
                .Select(x =>
                    x.CharacterId)
                .Distinct()
                .ToArray();

        if (characterIds.Length > 0)
        {
            NotifyCharacterDataList characterNotify =
                new();

            foreach (int characterId in characterIds)
            {
                CharacterData? character =
                    session.character.Characters
                        .FirstOrDefault(
                            x =>
                                x.Id == characterId);

                if (character is not null)
                {
                    characterNotify.CharacterDataList.Add(
                        character);
                }
            }

            if (characterNotify.CharacterDataList.Count > 0)
            {
                session.SendPush(
                    characterNotify);
            }
        }

        /*
         * /equip modify all level
         * /equip modify all level max
         *
         * 刷新 Partner。
         */
        if (notifyPartners
            && session.character.Partners is { Count: > 0 })
        {
            session.SendPush(
                new NotifyPartnerDataList
                {
                    PartnerDataList =
                        session.character.Partners.ToList(),

                    /*
                     * PartnerLevelUp / BreakThrough
                     * 都属于更新已有 Partner。
                     */
                    OperateTypes =
                        Enumerable
                            .Repeat(
                                2,
                                session.character.Partners.Count)
                            .ToList()
                });
        }
    }

    private void SyncEquipsFromDatabase()
    {
        session.character =
            Character.FromUid(
                session.player.PlayerData.Id);

        AccountModule.SendLoginState(
            session);
    }

    private void PruneDuplicateWeapons(
        NotifyEquipDataList notifyEquipData)
    {
        Dictionary<uint, EquipTable> equipRowsById =
            TableReaderV2.Parse<EquipTable>()
                .ToDictionary(
                    row =>
                        (uint)row.Id);

        foreach (IGrouping<uint, EquipData> duplicates in
                 session.character.Equips
                     .Where(equip =>
                         equipRowsById.TryGetValue(
                             equip.TemplateId,
                             out EquipTable? row)
                         && row.Site == 0)
                     .GroupBy(equip =>
                         equip.TemplateId))
        {
            if (duplicates.Count() < 2)
            {
                continue;
            }

            List<EquipData> removable =
                duplicates
                    .Where(
                        IsUninvestedDuplicateWeapon)
                    .OrderBy(
                        x => x.Id)
                    .ToList();

            int pristineToKeep =
                duplicates.Count() ==
                removable.Count
                    ? 1
                    : 0;

            foreach (EquipData equip in
                     removable.Skip(
                         pristineToKeep))
            {
                session.character.Equips.Remove(
                    equip);

                notifyEquipData.DeletedEquipIdList.Add(
                    equip.Id);
            }
        }
    }

    private bool IsUninvestedDuplicateWeapon(
        EquipData equip)
    {
        return equip.CharacterId == 0
            && !equip.IsLock
            && !equip.IsRecycle
            && !session.player.IsEquipInTeamPrefab(
                equip.Id)
            && equip.Level <= 1
            && equip.Exp <= 0
            && equip.Breakthrough <= 0
            && equip.ResonanceInfo.Count == 0
            && equip.UnconfirmedResonanceInfo.Count == 0
            && equip.AwakeSlotList.Count == 0
            && equip.WeaponOverrunData.Level <= 0
            && equip.WeaponOverrunData.ActiveSuits.Count == 0
            && equip.WeaponOverrunData.ChoseSuit <= 0;
    }
}