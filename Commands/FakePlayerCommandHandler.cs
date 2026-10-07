/*
 * FakePlayers - Commands/FakePlayerCommandHandler.cs
 *
 * La commande /fake tapée par le joueur dans le chat.
 * Cette classe ne fait que lire la commande, appeler FakePlayerMgr, puis afficher le résultat :
 * toute la logique (création, suppression...) est dans FakePlayerMgr.
 */

using System;
using System.Collections.Generic;
using DOL.GS.Commands;
using DOL.GS.PacketHandler;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// La commande /fake : création, liste et suppression des faux joueurs.
	/// L'attribut [Cmd] déclare la commande au serveur : son nom ("&amp;fake"), qui peut l'utiliser
	/// (ePrivLevel.Player = tous les joueurs) et les lignes d'aide affichées par DisplaySyntax.
	/// </summary>
	[Cmd(
		"&fake",
		ePrivLevel.Player,
		"Gère les faux joueurs.",
		"/fake create <nom> - crée un faux joueur près de vous",
		"/fake remove - supprime le faux joueur sélectionné (sans sélection : tous)",
		"/fake list - liste les faux joueurs du serveur")]
	public class FakePlayerCommandHandler : AbstractCommandHandler, ICommandHandler
	{
		/// <summary>
		/// Appelée par le serveur à chaque /fake.
		/// args[0] = "&amp;fake", args[1] = la sous-commande (create, list, remove).
		/// </summary>
		public void OnCommand(GameClient client, string[] args)
		{
			// Pas de sous-commande : on affiche l'aide.
			if (args.Length < 2)
			{
				DisplaySyntax(client);
				return;
			}

			GamePlayer player = client?.Player;
			if (player == null)
				return;

			switch (args[1].ToLowerInvariant())
			{
				case "create": Create(client, player, args); break;
				case "list": List(client); break;
				case "remove": Remove(client, player, args); break;
				default: DisplaySyntax(client); break;
			}
		}

		/// <summary>
		/// /fake create &lt;nom&gt; : crée un faux joueur à côté du joueur, à son image.
		/// Affiche "créé" ou la raison de l'échec.
		/// </summary>
		private void Create(GameClient client, GamePlayer player, string[] args)
		{
			if (args.Length < 3)
			{
				DisplayMessage(client, "Usage : /fake create <nom>");
				return;
			}

			FakeGamePlayer fake = FakePlayerMgr.Spawn(player, args[2], out string error);
			if (fake == null)
				DisplayMessage(client, "Impossible de créer le faux joueur : {0}.", error);
			else
				DisplayMessage(client, "Faux joueur {0} créé.", fake.Name);
		}

		/// <summary>
		/// /fake list : affiche tous les faux joueurs en jeu, numérotés,
		/// avec leur niveau, leur zone et le nom de leur créateur.
		/// </summary>
		private void List(GameClient client)
		{
			List<FakeGamePlayer> fakes = FakePlayerMgr.GetFakes();
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

		/// <summary>
		/// /fake remove [nom | all]
		///  - avec un nom : supprime ce faux joueur ;
		///  - "all" : supprime tous les faux joueurs ;
		///  - sans argument : supprime le faux joueur sélectionné,
		///    ou tous les faux joueurs s'il n'y a aucune sélection.
		///    Une cible qui n'est pas un faux joueur (mob, PNJ, vrai joueur) ne supprime rien.
		/// 
		/// "/fake remove - supprime le faux joueur sélectionné (sans sélection : tous)",
		/// "/fake remove <nom> - supprime un faux joueur",
		/// </summary>
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
				if (FakePlayerMgr.Remove(fake))
					DisplayMessage(client, "Faux joueur {0} supprimé.", name);
				else
					DisplayMessage(client, "Erreur à la suppression de {0} (voir la console du serveur).", name);
			}
		}

		/// <summary>Supprime tous les faux joueurs et affiche le nombre supprimé.</summary>
		private void RemoveAll(GameClient client)
		{
			int count = FakePlayerMgr.RemoveAll();
			DisplayMessage(client, "{0} faux joueur(s) supprimé(s).", count);
		}
	}
}
