using AscNet.GameServer.Handlers;

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
            "Complete all guides")]
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

            session.log.Info(
                $"Completed {completedCount} guides for player.");
        }
    }
}