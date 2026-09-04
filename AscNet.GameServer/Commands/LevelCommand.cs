using AscNet.Common.MsgPack;
using AscNet.Common.Util;
using AscNet.GameServer.Handlers;
using AscNet.Table.V2.share.player;

namespace AscNet.GameServer.Commands
{
    [CommandName("level")]
    internal class LevelCommand : Command
    {
        public LevelCommand(
            Session session,
            string[] args,
            bool validate = true)
            : base(session, args, validate)
        {
        }

        public override string Help =>
            "Command to change the Commandant level";

        [Argument(
            0,
            @"^[0-9]+$|^max$",
            "The target level, value is number or 'max'")]
        private string Level { get; set; } = string.Empty;

        public override void Execute()
        {
            List<PlayerTable> playerLevels =
                TableReaderV2.Parse<PlayerTable>();

            if (playerLevels.Count == 0)
            {
                throw new InvalidOperationException(
                    "Player level table is empty!");
            }

            int targetLevel;

            if (string.Equals(
                    Level,
                    "max",
                    StringComparison.OrdinalIgnoreCase))
            {
                targetLevel = playerLevels
                    .OrderByDescending(x => x.Level)
                    .First()
                    .Level;
            }
            else
            {
                targetLevel = Miscs.ParseIntOr(Level);

                if (targetLevel <= 0 ||
                    !playerLevels.Any(x => x.Level == targetLevel))
                {
                    throw new ArgumentException(
                        "Invalid Level!");
                }
            }

            int oldLevel =
                checked((int)session.player.PlayerData.Level);

            session.player.PlayerData.Level = targetLevel;

            session.ExpSanityCheck();

            session.player.SaveChecked();

            session.SendPush(
                AccountModule.BuildNotifyLogIn(session));

            if (oldLevel == targetLevel)
            {
                SendLevelRefresh();
            }
        }

        private void SendLevelRefresh()
        {
            session.SendPush(
                new NotifyPlayerLevel
                {
                    Level = checked(
                        (int)session.player.PlayerData.Level)
                });
        }
    }
}