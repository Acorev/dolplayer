/*
 * FakePlayers - Commands/FakePlayerCommandHandler.cs
 *
 * La commande /fake tapée par le joueur dans le chat.
 * Cette classe ne fait que lire la commande, appeler FakePlayerMgr, puis afficher le résultat :
 * toute la logique (appel, suppression...) est dans FakePlayerMgr.
 */

using System.Collections.Generic;
using DOL.GS.Commands;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// La commande /fake : appel, liste et suppression des alts.
	/// L'attribut [Cmd] déclare la commande au serveur : son nom ("&amp;fake"), qui peut l'utiliser
	/// (ePrivLevel.Player = tous les joueurs) et les lignes d'aide affichées par DisplaySyntax.
	/// </summary>
	[Cmd(
		"&fake",
		ePrivLevel.Player,
		"Gère vos alts (personnages de votre compte joués par le serveur).",
		"/fake call <personnage> - appelle un de vos personnages et le groupe avec vous",
		"/fake list - liste les alts en jeu",
		"/fake remove - supprime l'alt sélectionné (sans sélection : tous)",
		"/fake remove <nom> - supprime un alt",
		"/fake remove all - supprime tous les alts",
		"/fake stay [nom] - l'alt ciblé (ou nommé, sans cible : tous les vôtres) reste sur place",
		"/fake follow [nom] - l'alt ciblé (ou nommé, sans cible : tous les vôtres) vous suit à nouveau",
		"/fake nav - teste le navmesh à votre position (et le chemin vers votre cible)")]
	public class FakePlayerCommandHandler : AbstractCommandHandler, ICommandHandler
	{
		/// <summary>
		/// Appelée par le serveur à chaque /fake.
		/// args[0] = "&amp;fake", args[1] = la sous-commande (call, list, remove, stay, follow, nav),
		/// args[2] = le paramètre éventuel.
		/// </summary>
		public void OnCommand(GameClient client, string[] args)
		{
			GamePlayer player = client?.Player;
			if (player == null)
				return;

			// Pas de sous-commande : on affiche l'aide.
			if (args.Length < 2)
			{
				DisplaySyntax(client);
				return;
			}

			switch (args[1].ToLowerInvariant())
			{
				case "call":
					Call(client, player, args);
					break;
				case "list":
					List(client);
					break;
				case "remove":
					Remove(client, player, args);
					break;
				case "stay":
					SetFollow(client, player, args, false);
					break;
				case "follow":
					SetFollow(client, player, args, true);
					break;
				case "nav":
					// Diagnostic du navmesh (voir Movement/FakeNavCheck).
					foreach (string line in FakeNavCheck.Check(player))
						DisplayMessage(client, line);
					break;
				default:
					DisplaySyntax(client); // sous-commande inconnue : on affiche l'aide
					break;
			}
		}

		/// <summary>
		/// /fake call &lt;personnage&gt; : appelle un personnage du compte à côté du joueur et le groupe avec lui.
		/// Affiche "arrive" (avec la classe et le niveau) ou la raison de l'échec.
		/// </summary>
		private void Call(GameClient client, GamePlayer player, string[] args)
		{
			if (args.Length < 3)
			{
				DisplayMessage(client, "Usage : /fake call <personnage>");
				return;
			}

			FakeGamePlayer fake = FakePlayerMgr.Call(player, args[2], out string error);
			if (fake == null)
				DisplayMessage(client, "Impossible d'appeler {0} : {1}.", args[2], error);
			else
				DisplayMessage(client, "{0} arrive : {1} niveau {2}.",
					fake.Name, fake.CharacterClass.GetSalutation(fake.Gender), fake.Level);
		}

		/// <summary>
		/// /fake list : affiche tous les alts en jeu, numérotés,
		/// avec leur classe, leur niveau, leur zone et le nom de celui qui les a appelés.
		/// </summary>
		private void List(GameClient client)
		{
			List<FakeGamePlayer> fakes = FakePlayerMgr.GetFakes();
			if (fakes.Count == 0)
			{
				DisplayMessage(client, "Aucun alt en jeu.");
				return;
			}

			DisplayMessage(client, "Alts en jeu ({0}) :", fakes.Count);
			for (int i = 0; i < fakes.Count; i++)
			{
				FakeGamePlayer f = fakes[i];
				DisplayMessage(client, "  {0}. {1} ({2} niveau {3}, {4}, appelé par {5})",
					i + 1, f.Name, f.CharacterClass.GetSalutation(f.Gender), f.Level,
					f.CurrentZone?.Description ?? "?", f.Owner?.Name ?? "?");
			}
		}

		/// <summary>
		/// /fake remove [nom | all]
		///  - avec un nom : supprime cet alt ;
		///  - "all" : supprime tous les alts ;
		///  - sans argument : supprime l'alt sélectionné,
		///    ou tous les alts s'il n'y a aucune sélection.
		///    Une cible qui n'est pas un alt (mob, PNJ, vrai joueur) ne supprime rien.
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

				// "as" donne null si la cible n'est pas un alt.
				fake = target as FakeGamePlayer;
				if (fake == null)
				{
					DisplayMessage(client, "{0} n'est pas un alt, rien n'a été supprimé.", target.Name);
					return;
				}
			}
			else if (args[2].ToLowerInvariant() == "all")
			{
				RemoveAll(client);
				return;
			}
			else
			{
				fake = FakePlayerMgr.FindByName(args[2]);
				if (fake == null)
				{
					DisplayMessage(client, "Aucun alt nommé {0} en jeu. Voir /fake list.", args[2]);
					return;
				}
			}

			// Nom gardé avant la suppression, pour le message.
			string name = fake.Name;
			if (FakePlayerMgr.Remove(fake))
				DisplayMessage(client, "{0} est reparti.", name);
			else
				DisplayMessage(client, "Erreur à la suppression de {0} (voir la console du serveur).", name);
		}

		/// <summary>
		/// /fake stay [nom] et /fake follow [nom] : change l'ordre de déplacement.
		///  - avec un nom : cet alt ;
		///  - sans nom : l'alt ciblé, ou tous vos alts s'il n'y a aucune cible.
		///    Une cible qui n'est pas un alt ne change rien.
		/// </summary>
		/// <param name="follow">true = suivre (/fake follow), false = rester (/fake stay).</param>
		private void SetFollow(GameClient client, GamePlayer player, string[] args, bool follow)
		{
			var fakes = new List<FakeGamePlayer>();

			if (args.Length >= 3)
			{
				FakeGamePlayer named = FakePlayerMgr.FindByName(args[2]);
				if (named == null)
				{
					DisplayMessage(client, "Aucun alt nommé {0} en jeu. Voir /fake list.", args[2]);
					return;
				}
				fakes.Add(named);
			}
			else if (player.TargetObject != null)
			{
				if (player.TargetObject is not FakeGamePlayer targeted)
				{
					DisplayMessage(client, "{0} n'est pas un alt.", player.TargetObject.Name);
					return;
				}
				fakes.Add(targeted);
			}
			else
			{
				fakes = FakePlayerMgr.GetFakesOf(player);
			}

			if (fakes.Count == 0)
			{
				DisplayMessage(client, "Vous n'avez aucun alt en jeu.");
				return;
			}

			foreach (FakeGamePlayer fake in fakes)
				fake.IsFollowing = follow;

			string names = string.Join(", ", fakes.ConvertAll(f => f.Name));
			DisplayMessage(client, follow ? "{0} : vous suit." : "{0} : reste sur place.", names);
		}

		/// <summary>Supprime tous les alts et affiche le nombre supprimé.</summary>
		private void RemoveAll(GameClient client)
		{
			int count = FakePlayerMgr.RemoveAll();
			DisplayMessage(client, "{0} alt(s) supprimé(s).", count);
		}
	}
}
