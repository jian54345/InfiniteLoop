using AscNet.Common.MsgPack;
using AscNet.Common.Util;
using AscNet.GameServer.Handlers;
using AscNet.Table.V2.share.photomode;
using AscNet.Table.V2.share.fuben;

namespace AscNet.GameServer.Commands
{
    [CommandName("guide")]
    internal class GuideCommand : Command
    {
        public GuideCommand(
            Session session,
            string[] args,
            bool validate = true)
            : base(session, args, validate)
        {
        }

        public override string Help =>
            "Command to modify guide progress";

        [Argument(
            0,
            @"^all$",
            "Complete all scenes, stages and guides")]
        private string Action { get; set; } = string.Empty;

        public override void Execute()
        {
            if (!string.Equals(
                    Action,
                    "all",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Invalid guide command!");
            }

            /*
             * /guide all 的正确执行顺序：
             *
             * 1. Scene unlock all
             * 2. Stage all
             * 3. 根据已完成 Stage 同步 Guide
             * 4. 完成剩余 Guide
             *
             * 不直接调用 SceneCommand / StageCommand，
             * 避免 Command 套 Command。
             */

            CompleteAllScenes();

            CompleteAllStages();

            // Stage 已经 Passed 后，再同步依赖 Stage 的 Guide。
            GuideModule.ReconcileStageCompletedGuides(
                session.player,
                session.stage);

            int completedCount;

            try
            {
                completedCount =
                    GuideModule.CompleteAllGuides(session);
            }
            catch (Exception exception)
            {
                session.log.Error(
                    $"Failed to complete all guides: {exception}");

                throw new InvalidOperationException(
                    "Failed to complete all guides!",
                    exception);
            }

            session.player.SaveChecked();

            session.log.Info(
                $"Completed all scenes, stages and {completedCount} guides.");
        }

        private void CompleteAllScenes()
        {
            List<int> catalogIds =
                TableReaderV2.Parse<BackgroundTable>()
                    .Where(background =>
                        background.Id > 0 &&
                        background.SceneModelId > 0)
                    .Select(background => background.Id)
                    .Distinct()
                    .Order()
                    .ToList();

            List<int> owned =
                session.player.OwnedBackgroundIds
                ?? new List<int>();

            List<int> added =
                catalogIds
                    .Where(id => !owned.Contains(id))
                    .ToList();

            if (added.Count == 0)
                return;

            List<int> original = owned.ToList();

            session.player.OwnedBackgroundIds =
                owned
                    .Union(catalogIds)
                    .Distinct()
                    .Order()
                    .ToList();

            try
            {
                session.player.SaveChecked();
            }
            catch
            {
                session.player.OwnedBackgroundIds = original;
                throw new CommandMessageCallbackException(
                    "Failed to persist scene unlocks.");
            }

            foreach (int id in added)
            {
                session.SendPush(
                    new NotifyAddBackground
                    {
                        BackgroundId = id
                    });
            }
        }

        private void CompleteAllStages()
{
    session.stage.Stages.Clear();

    foreach (StageTable stageData in
        TableReaderV2.Parse<StageTable>()
            .Where(x =>
                x.StageId >= 10000000 &&
                x.StageId <= 20000000))
    {
        session.stage.Stages.Add(
            stageData.StageId,
            new()
            {
                StageId = stageData.StageId,
                StarsMark = 7,
                Passed = true,
                PassTimesToday = 0,
                PassTimesTotal = 1,
                BuyCount = 0,
                Score = 0,
                LastPassTime =
                    DateTimeOffset.Now.ToUnixTimeSeconds(),
                RefreshTime =
                    DateTimeOffset.Now.ToUnixTimeSeconds(),
                CreateTime =
                    DateTimeOffset.Now.ToUnixTimeSeconds(),
                BestRecordTime = 0,
                LastRecordTime = 0,
                BestCardIds =
                    new List<long> { 1021001 },
                LastCardIds =
                    new List<long> { 1021001 }
            });
    }

    // 先持久化 Stage，再进行 Guide 同步
    session.stage.Save();

    session.SendPush(
        new NotifyStageData
        {
            StageList =
                session.stage.Stages
                    .Select(x => x.Value)
                    .ToList()
        });
}
    }
}