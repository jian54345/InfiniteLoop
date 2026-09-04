using MessagePack;
using AscNet.Common.MsgPack;
using AscNet.Common.Util;
using AscNet.Table.V2.share.exhibition;
using AscNet.Table.V2.share.condition;
using AscNet.Table.V2.share.equip;
using AscNet.Table.V2.share.reward;

namespace AscNet.GameServer.Handlers
{
    #region MsgPackScheme
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    [MessagePackObject(true)]
    public class GatherRewardRequest
    {
        public int Id;
    }

    [MessagePackObject(true)]
    public class GatherRewardResponse
    {
        public int Code;
        public List<RewardGoods> RewardGoods { get; set; } = new();
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    #endregion

    internal class ExhibitionModule
    {
        private static bool MeetsPrerequisites(Session session, ExhibitionRewardTable reward, out CharacterData? character)
        {
            character = session.character.Characters.Find(candidate => candidate.Id == (uint)reward.CharacterId);
            if (character is null)
                return false;

            ExhibitionRewardTable? precedingReward = TableReaderV2.Parse<ExhibitionRewardTable>()
                .Where(candidate => candidate.CharacterId == reward.CharacterId && candidate.LevelId < reward.LevelId)
                .MaxBy(candidate => candidate.LevelId);
            if (precedingReward is not null
                && !session.player.GatherRewards.Contains(precedingReward.Id)
                && character.LiberateLv < precedingReward.LevelId)
                return false;

            if (reward.ConditionIds.Count > 0
                && !MeetsCharacterConditions(session, character, reward.ConditionIds))
                return false;
            return true;
        }

        internal static bool PrepareGatherRewardForCommand(
    Session session,
    ExhibitionRewardTable exhibitionReward,
    out CharacterData? character,
    out RewardApplicationResult? result)
{
    character =
        session.character.Characters.Find(
            candidate =>
                candidate.Id ==
                (uint)exhibitionReward.CharacterId);

    result = null;

    if (character is null)
    {
        return false;
    }

    /*
     * GM / character max 专用：
     *
     * 不检查 MeetsPrerequisites()。
     *
     * 普通玩家领取 ExhibitionReward 时，
     * 仍然由 GatherRewardRequest 使用
     * MeetsPrerequisites() 进行正常限制。
     *
     * GM max 的目的就是直接完成角色解放，
     * 所以这里不能再被 Ability / Resonance /
     * AwakenedMemory 等普通条件卡住。
     */

    /*
     * 已经领取过：
     *
     * 不重复发奖励，只同步 LiberateLv。
     */
    if (session.player.GatherRewards.Contains(
            exhibitionReward.Id))
    {
        character.LiberateLv =
            Math.Max(
                character.LiberateLv,
                exhibitionReward.LevelId);

        return true;
    }

    /*
     * 获取正式奖励配置。
     */
    List<RewardGoodsTable> rewardGoodsTables =
        exhibitionReward.RewardId is > 0
            ? RewardHandler.GetRewardGoods(
                exhibitionReward.RewardId.Value)
            : [];

    /*
     * Exhibition 配置了 RewardId，
     * 但找不到对应 RewardGoods 时，
     * 不标记领取。
     */
    if (exhibitionReward.RewardId is > 0
        && rewardGoodsTables.Count == 0)
    {
        return false;
    }

    /*
     * 标记 ExhibitionReward 已领取。
     */
    if (!session.player.AddGatherReward(
            exhibitionReward.Id))
    {
        return false;
    }

    /*
     * 使用 ApplyRewards 而不是 GiveRewards。
     *
     * ApplyRewards：
     *   执行奖励
     *   累计通知
     *   不立即 SendPushes
     *
     * 最后由 CharacterCommand 一次性发送。
     */
    result =
        RewardHandler.ApplyRewards(
            rewardGoodsTables,
            session);

    /*
     * 更新角色解放等级。
     */
    character.LiberateLv =
        Math.Max(
            character.LiberateLv,
            exhibitionReward.LevelId);

    /*
     * 记录 GatherReward 通知。
     */
    if (!result.GatherRewardIds.Contains(
            exhibitionReward.Id))
    {
        result.GatherRewardIds.Add(
            exhibitionReward.Id);
    }

    return true;
}
		internal static bool MeetsCharacterConditions(
            Session session,
            CharacterData character,
            IReadOnlyList<int> conditionIds)
        {
            return conditionIds.Count > 0 && conditionIds.All(conditionId =>
                TableReaderV2.Parse<ConditionTable>().FirstOrDefault(
                    candidate => candidate.Id == conditionId) is { } condition
                && condition.Type switch
                {
                    13103 when condition.Params.Count == 1 => character.Level >= condition.Params[0],
                    // A zero persisted Ability is the unknown server-computed sentinel; legitimate clients gate their locally calculated value before requesting.
                    13108 when condition.Params.Count == 1 => character.Ability == 0 || character.Ability >= condition.Params[0],
                    13109 when condition.Params.Count == 2 => CountBoundResonances(session, character, condition.Params[0]) >= condition.Params[1],
                    13114 when condition.Params.Count == 1 => TableReaderV2.Parse<ExhibitionRewardTable>().Any(
                        reward => reward.CharacterId == character.Id
                            && reward.LevelId >= condition.Params[0]
                            && session.player.GatherRewards.Contains(reward.Id)),
                    13118 when condition.Params.Count == 1 => CountAwakenedBoundMemoryResonances(session, character) >= condition.Params[0],
                    _ => false
                });
        }

        private static int CountBoundResonances(Session session, CharacterData character, int minimumQuality)
        {
            Dictionary<uint, EquipTable> templates = TableReaderV2.Parse<EquipTable>()
                .ToDictionary(template => (uint)template.Id);
            return session.character.Equips
                .Where(equip => equip.CharacterId == character.Id
                    && templates.TryGetValue(equip.TemplateId, out EquipTable? template)
                    && template.Quality >= minimumQuality)
                .Sum(equip => equip.ResonanceInfo.Count(resonance => resonance.CharacterId == character.Id));
        }

        private static int CountAwakenedBoundMemoryResonances(Session session, CharacterData character)
        {
            Dictionary<uint, EquipTable> templates = TableReaderV2.Parse<EquipTable>()
                .ToDictionary(template => (uint)template.Id);
            return session.character.Equips
                .Where(equip => equip.CharacterId == character.Id
                    && templates.TryGetValue(equip.TemplateId, out EquipTable? template)
                    && template.Site is >= 1 and <= 6)
                .Sum(equip => equip.ResonanceInfo.Count(resonance =>
                    resonance.CharacterId == character.Id
                    && equip.AwakeSlotList.Any(slot => Convert.ToInt32(slot) == resonance.Slot)));
        }

        [RequestPacketHandler("GatherRewardRequest")]
        public static void HandleGatherRewardRequestHandler(Session session, Packet.Request packet)
        {
            GatherRewardRequest req = MessagePackSerializer.Deserialize<GatherRewardRequest>(packet.Content);
            ExhibitionRewardTable? exhibitionReward = TableReaderV2.Parse<ExhibitionRewardTable>().Find(x => x.Id == req.Id);
            if (exhibitionReward is null)
            {
                session.SendResponse(new GatherRewardResponse() { Code = 1 }, packet.Id);
                return;
            }

            var rewardGoodsTables = exhibitionReward.RewardId is > 0
                ? RewardHandler.GetRewardGoods(exhibitionReward.RewardId.Value)
                : [];
            if (exhibitionReward.RewardId is > 0 && rewardGoodsTables.Count == 0)
            {
                session.SendResponse(new GatherRewardResponse() { Code = 1 }, packet.Id);
                return;
            }
            if (!MeetsPrerequisites(session, exhibitionReward, out CharacterData? character))
            {
                session.SendResponse(new GatherRewardResponse() { Code = 1 }, packet.Id);
                return;
            }


            if (!session.player.AddGatherReward(req.Id))
            {
                session.SendResponse(new GatherRewardResponse() { Code = 1 }, packet.Id);
                return;
            }

            List<RewardGoods> rewardGoods = RewardHandler.GiveRewards(rewardGoodsTables, session);
            character!.LiberateLv = Math.Max(character.LiberateLv, exhibitionReward.LevelId);
            session.player.Save();
            session.inventory.Save();
            session.character.Save();

            GatherRewardResponse rsp = new()
            {
                Code = 0,
                RewardGoods = rewardGoods
            };

            session.SendPush(new NotifyCharacterDataList()
            {
                CharacterDataList = { character }
            });
            session.SendPush(new NotifyGatherReward() { Id = req.Id });
            session.SendResponse(rsp, packet.Id);
        }
    }
}
