
using DOL.GS.Commands;

namespace DOL.GS.Scripts.fakeplayers
{
    /// <summary>
	/// La commande /fakeplayer.
	/// </summary>
	[Cmd(
        "&fake",
        ePrivLevel.Player,
        "Crée des faux joueurs.",
        "/fake create [nom] - crée un faux joueurs près de vous",
        "/fake list - liste vos faux joueurs")]
    public class FakePlayerCommandHandler : AbstractCommandHandler, ICommandHandler
    {

        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client?.Player;
            if (args.Length < 2)
            {
                DisplaySyntax(client);
                return;
            }

            switch (args[1].ToLowerInvariant())
            {
                case "create":
                    FakePlayerMgr.Spawn(player, args[2], out _);
                    break;
                default:
                    DisplaySyntax(client);
                    break;
            }
        }
    }
}