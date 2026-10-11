
using System;
using System.Collections.Generic;
using DOL.GS.Commands;
using DOL.GS.PacketHandler;

namespace DOL.GS.Scripts
{
    [Cmd(
        "&fake",
        ePrivLevel.Player,
        "Gère les faux joueurs (create, remove, removeall, list)",
        "/fake create <nom> <class> - crée un faux joueur près de vous",
        "/fake remove - supprime le faux joueur sélectionné (sans sélection : tous)",
        "/fake list - liste les faux joueurs du serveur")]

    /// <summary>
    /// Appelée par le serveur à chaque /fake.
    /// args[0] = "fake".
    /// args[1] = create, list, remove.
    /// args[2] = nom.
    /// args[3] = class.
    /// </summary>
    public class FakePlayerCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            if (args.Length < 2)
            {
                DisplaySyntax(client);
                return;
            }

            GamePlayer player = client?.Player;
            if (player == null)
                return;

            try
            {
                switch (args[1])
                {
                    case "create": Create(client, args); break;
                    case "remove": Remove(client, player, args); break;
                    case "list": List(client); break;
                    default: DisplaySyntax(client); break;
                }


            }
            catch (Exception ex)
            {
                client.Out.SendMessage(ex.ToString(), eChatType.CT_System, eChatLoc.CL_ChatWindow);
                DisplaySyntax(client);
            }
        }

        /// <summary>/fake create nom Class</summary>
        private void Create(GameClient client, string[] args)
        {
            if (args.Length < 3)
            {
                DisplaySyntax(client);
                return;
            }

            switch (args[3])
            {
                case "armsman": FakePlayer.SpawnFake(client.Player, new FakeArmsman(client.Player, args[2])); break;
                default: DisplaySyntax(client); break;
            }
        }

        private void List(GameClient client)
        {
            List<FakeGamePlayer> fakes = FakePlayer.GetFakes();
            if (fakes.Count == 0)
            {
                DisplayMessage(client, "Aucun faux joueur en jeu.");
                return;
            }

            DisplayMessage(client, "Faux joueurs en jeu ({0}) :", fakes.Count);
            for (int i = 0; i < fakes.Count; i++)
            {
                FakeGamePlayer f = fakes[i];
                DisplayMessage(client, "  {0}. {1} (niveau {2}, {3}, créé par {4})",
                    i + 1, f.Name, f.Level, f.CurrentZone?.Description ?? "?", f.Owner?.Name ?? "?");
            }
        }

        private void Remove(GameClient client, GamePlayer player, string[] args)
        {
            FakeGamePlayer fake;

            if (args.Length < 3)
            {
                // Pas de paramètre : on regarde la cible sélectionnée en jeu.
                GameObject target = player.TargetObject;
                if (target == null)
                {
                    RemoveAll(client);
                    return;
                }

                // "as" donne null si la cible n'est pas un faux joueur.
                fake = target as FakeGamePlayer;
                if (fake == null)
                {
                    DisplayMessage(client, "{0} n'est pas un faux joueur, rien n'a été supprimé.", target.Name);
                    return;
                }

                string name = fake.Name;
                if (FakePlayer.Remove(fake))
                    DisplayMessage(client, "Faux joueur {0} supprimé.", name);
                else
                    DisplayMessage(client, "Erreur à la suppression de {0} (voir la console du serveur).", name);
            }
        }

        /// <summary>Supprime tous les faux joueurs et affiche le nombre supprimé.</summary>
		private void RemoveAll(GameClient client)
        {
            int count = FakePlayer.RemoveAll();
            DisplayMessage(client, "{0} faux joueur(s) supprimé(s).", count);
        }
    }
}