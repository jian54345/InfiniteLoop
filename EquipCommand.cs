using AscNet.Common;
using AscNet.Common.Database;
using AscNet.Common.MsgPack;
using AscNet.Common.Util;
using AscNet.GameServer.Handlers;
using AscNet.Table.V2.share.equip;
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
        if (Value.Equals(
                "max",
                StringComparison.OrdinalIgnoreCase))
        {
            foreach (EquipData equip in equips)
            {
                SetEquipMaxLevel(equip);
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
        }
        else
        {
            throw new ArgumentException(
                "Usage: equip modify <id|all> level <value|max>");
        }
        SaveAndNotify(equips);
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
    private void SaveAndNotify(
    IEnumerable<EquipData> equips)
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
    // 装备数据刷新
    NotifyEquipDataList equipNotify =
        new();
    foreach (EquipData equip in target)
    {
        equipNotify.EquipDataList.Add(
            equip);
    }
    session.SendPush(
        equipNotify);
    // 刷新装备所绑定角色的数据
    int[] characterIds =
        target
            .Where(x => x.CharacterId > 0)
            .Select(x => x.CharacterId)
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
                        x => x.Id == characterId);
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
                    row => (uint)row.Id);
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
                    .Where(IsUninvestedDuplicateWeapon)
                    .OrderBy(x => x.Id)
                    .ToList();
            int pristineToKeep =
                duplicates.Count() ==
                removable.Count
                    ? 1
                    : 0;
            foreach (EquipData equip in
                     removable.Skip(pristineToKeep))
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
            && !session.player.IsEquipInTeamPrefab(equip.Id)
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