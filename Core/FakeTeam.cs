/*
 * FakePlayers - Core/FakeTeam.cs
 *
 * L'équipe d'un personnage : la liste des alts qu'il appelle d'un coup avec /fake team.
 *
 * Une équipe par PERSONNAGE (chaque personnage principal a la sienne), 7 alts au plus
 * (un groupe compte 8 membres). L'ordre de la liste est l'ordre d'appel, donc l'ordre de la file indienne.
 *
 * Stockage : table DOLSharp "DOLCharactersXCustomParam" (réglages personnalisés d'un personnage),
 * une ligne avec la clé TEAM_KEY et les noms séparés par ';'.
 *   - liée au personnage : supprimer le personnage supprime aussi son équipe ;
 *   - la sauvegarde normale du personnage ne réécrit que les lignes modifiées : elle n'écrase pas l'équipe.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Lecture, modification et appel de l'équipe d'un personnage.
	/// </summary>
	public static class FakeTeam
	{
		/// <summary>Clé de la ligne de réglage qui contient l'équipe.</summary>
		public const string TEAM_KEY = "FakePlayers.Team";

		/// <summary>Séparateur des noms dans la valeur stockée.</summary>
		private const char SEPARATOR = ';';

		/// <summary>Nombre maximal d'alts dans une équipe (groupe de 8 = le joueur + 7).</summary>
		public static int MaxSize => Math.Max(1, ServerProperties.Properties.GROUP_MAX_MEMBER - 1);

		// ================================================================= lecture

		/// <summary>Les noms de l'équipe du joueur, dans l'ordre (liste vide s'il n'en a pas).</summary>
		public static List<string> Get(GamePlayer owner)
		{
			DOLCharactersXCustomParam row = FindRow(owner);
			if (row == null || string.IsNullOrWhiteSpace(row.Value))
				return new List<string>();
			return row.Value.Split(SEPARATOR, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
		}

		// ================================================================= modification

		/// <summary>
		/// Ajoute un personnage à la fin de l'équipe.
		/// Il doit être du compte et du royaume du joueur (mêmes règles que /fake call).
		/// </summary>
		/// <param name="added">Le nom exact du personnage ajouté (avec ses majuscules).</param>
		/// <returns>false et la raison dans "error" en cas de refus.</returns>
		public static bool Add(GamePlayer owner, string charName, out string added, out string error)
		{
			added = null;
			DOLCharacters dbChar = FakePlayerMgr.FindOwnCharacter(owner, charName, out error);
			if (dbChar == null)
				return false;

			List<string> team = Get(owner);
			if (team.Any(n => n.Equals(dbChar.Name, StringComparison.OrdinalIgnoreCase)))
			{
				error = dbChar.Name + " est déjà dans votre équipe";
				return false;
			}
			if (team.Count >= MaxSize)
			{
				error = "votre équipe est complète (" + MaxSize + " alts au plus)";
				return false;
			}

			team.Add(dbChar.Name);
			if (!Save(owner, team))
			{
				error = "erreur d'écriture en base (voir la console du serveur)";
				return false;
			}

			added = dbChar.Name;
			error = null;
			return true;
		}

		/// <summary>Retire un personnage de l'équipe (n'enlève pas l'alt s'il est en jeu).</summary>
		/// <param name="removed">Le nom exact retiré.</param>
		/// <returns>false et la raison dans "error" s'il n'y était pas.</returns>
		public static bool Remove(GamePlayer owner, string charName, out string removed, out string error)
		{
			removed = null;
			List<string> team = Get(owner);
			string found = team.FirstOrDefault(n => n.Equals(charName, StringComparison.OrdinalIgnoreCase));
			if (found == null)
			{
				error = charName + " n'est pas dans votre équipe";
				return false;
			}

			team.Remove(found);
			if (!Save(owner, team))
			{
				error = "erreur d'écriture en base (voir la console du serveur)";
				return false;
			}

			removed = found;
			error = null;
			return true;
		}

		// ================================================================= appel

		/// <summary>
		/// Appelle toute l'équipe, en arrière-plan, un alt après l'autre, dans l'ordre de la liste.
		/// Les alts déjà en jeu sont ignorés. Chaque arrivée ou échec est annoncé au joueur.
		/// </summary>
		/// <returns>false si l'équipe est vide.</returns>
		public static bool CallAll(GamePlayer owner)
		{
			List<string> team = Get(owner);
			if (team.Count == 0)
				return false;

			FakePlayerMgr.RunInBackground("appel de l'équipe de " + owner.Name, () =>
			{
				foreach (string name in team)
				{
					// Le joueur s'est déconnecté entre-temps : on arrête.
					if (owner.ObjectState != GameObject.eObjectState.Active)
						return;

					// Déjà là : rien à faire.
					if (FakePlayerMgr.GetFakesOf(owner).Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
						continue;

					FakeGamePlayer fake = FakePlayerMgr.Call(owner, name, out string error);
					if (fake == null)
						Tell(owner, "Impossible d'appeler " + name + " : " + error + ".");
					else
						Tell(owner, fake.Name + " arrive : " + fake.CharacterClass.GetSalutation(fake.Gender) + " niveau " + fake.Level + ".");
				}
			});
			return true;
		}

		// ================================================================= outils internes

		/// <summary>La ligne de réglage de l'équipe du joueur, ou null s'il n'en a pas.</summary>
		private static DOLCharactersXCustomParam FindRow(GamePlayer owner)
		{
			return DOLDB<DOLCharactersXCustomParam>.SelectObject(
				DB.Column(nameof(DOLCharactersXCustomParam.DOLCharactersObjectId)).IsEqualTo(owner.ObjectId)
				.And(DB.Column(nameof(DOLCharactersXCustomParam.KeyName)).IsEqualTo(TEAM_KEY)));
		}

		/// <summary>
		/// Enregistre l'équipe en base : crée la ligne si besoin, la met à jour, ou la supprime si l'équipe est vide.
		/// </summary>
		/// <returns>false en cas d'erreur d'écriture.</returns>
		private static bool Save(GamePlayer owner, List<string> team)
		{
			DOLCharactersXCustomParam row = FindRow(owner);
			string value = string.Join(SEPARATOR, team);

			if (team.Count == 0)
				return row == null || GameServer.Database.DeleteObject(row);

			if (row == null)
				return GameServer.Database.AddObject(new DOLCharactersXCustomParam(owner.ObjectId, TEAM_KEY, value));

			row.Value = value;
			return GameServer.Database.SaveObject(row);
		}

		/// <summary>Message au joueur dans la fenêtre système.</summary>
		private static void Tell(GamePlayer owner, string message)
		{
			owner.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
		}
	}
}
