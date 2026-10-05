/*
 * FakePlayers - Core/FakePlayerMgr.cs
 *
 * Le "chef d'orchestre" des faux joueurs : c'est ici qu'on les crée, qu'on les retrouve et qu'on les supprime.
 * Toutes les autres classes passent par lui (la commande /fake n'appelle que FakePlayerMgr).
 *
 * Création d'un faux joueur, étape par étape (méthode Spawn) :
 *   1. vérifier le nom ;
 *   2. fabriquer en mémoire un personnage (DOLCharacters) et un compte (Account), copiés sur le créateur ;
 *   3. fabriquer un faux client réseau (FakeGameClient) et lui donner un numéro de session ;
 *   4. fabriquer le faux joueur (FakeGamePlayer) et l'ajouter au monde ;
 *   5. démarrer l'envoi de sa position (FakePositionSender) et l'ajouter à la liste.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.Database;
using log4net;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Création, liste et suppression des faux joueurs.
	/// Les faux joueurs restent en jeu même quand leur créateur se déconnecte :
	/// seule la commande /fake remove les supprime.
	/// TEMPORAIRE (tests) : une seule liste pour tout le serveur, n'importe quel joueur peut tout lister/supprimer.
	/// </summary>
	public static class FakePlayerMgr
	{
		/// <summary>Journal du serveur (console + fichier de log) pour écrire les erreurs.</summary>
		private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		/// <summary>Longueur minimale d'un nom de faux joueur.</summary>
		public const int NAME_MIN = 3;

		/// <summary>Longueur maximale d'un nom de faux joueur.</summary>
		public const int NAME_MAX = 20;

		/// <summary>
		/// Tous les faux joueurs du serveur.
		/// Toujours y accéder sous verrou (_lock) : la commande et les timers du serveur
		/// peuvent tourner en même temps sur des threads différents.
		/// </summary>
		private static readonly List<FakeGamePlayer> _fakes = new();

		/// <summary>Verrou qui protège la liste _fakes.</summary>
		private static readonly object _lock = new();

		// ================================================================= création

		/// <summary>
		/// Crée un faux joueur à côté du propriétaire, avec son apparence et ses caractéristiques.
		/// </summary>
		/// <param name="owner">Le vrai joueur qui crée le faux joueur (sert de modèle et de position).</param>
		/// <param name="name">Le nom voulu (il sera mis en forme : "bOB" devient "Bob").</param>
		/// <param name="error">En cas d'échec : la raison, en clair, à afficher au joueur. Sinon null.</param>
		/// <returns>Le faux joueur créé, ou null en cas d'échec.</returns>
		public static FakeGamePlayer Spawn(GamePlayer owner, string name, out string error)
		{
			// --- 1. Vérifications
			if (owner == null || owner.Client == null)
			{
				error = "joueur introuvable";
				return null;
			}
			if (!IsValidName(name, out error))
				return null;

			name = FormatName(name);
			if (FindByName(name) != null || WorldMgr.GetClientByPlayerName(name, true, false) != null)
			{
				error = "le nom " + name + " est déjà utilisé par un joueur en jeu";
				return null;
			}

			// --- 2 et 3. Personnage, compte et faux client, uniquement en mémoire (rien en base)
			DOLCharacters dbChar = BuildCharacter(owner, name);
			var client = new FakeGameClient(BuildAccount(owner, dbChar));

			// Numéro de session : nécessaire pour que le serveur traite le faux client comme un vrai.
			if (WorldMgr.CreateSessionID(client) < 0)
			{
				error = "le serveur est plein (plus de numéro de session libre)";
				return null;
			}

			// --- 4. Le faux joueur lui-même, ajouté au monde
			FakeGamePlayer fake;
			try
			{
				fake = new FakeGamePlayer(client, dbChar) { Owner = owner };
				client.Player = fake;
				client.ClientState = GameClient.eClientState.Playing; // le serveur le considère "en jeu"

				if (!fake.AddToWorld())
				{
					error = "impossible d'ajouter le joueur au monde ici";
					ReleaseClient(client);
					return null;
				}
			}
			catch (Exception ex)
			{
				// La trace complète part dans la console du serveur, le joueur reçoit un message court.
				log.Error("[FakePlayers] erreur à la création de " + name, ex);
				error = "erreur à la création : " + ex.Message;
				ReleaseClient(client);
				return null;
			}

			// --- 5. Démarré seulement maintenant : le faux joueur est dans le monde, sa région est connue.
			FakePositionSender.Start(fake);
			lock (_lock)
				_fakes.Add(fake);

			error = null;
			return fake;
		}

		// ================================================================= recherche

		/// <summary>
		/// Liste de tous les faux joueurs encore en jeu.
		/// Retourne une copie : on peut la parcourir (et supprimer des faux joueurs pendant le parcours)
		/// sans risque. Les faux joueurs disparus entre-temps sont retirés au passage.
		/// </summary>
		public static List<FakeGamePlayer> GetFakes()
		{
			lock (_lock)
			{
				_fakes.RemoveAll(f => f.ObjectState == GameObject.eObjectState.Deleted);
				return _fakes.ToList();
			}
		}

		/// <summary>Cherche un faux joueur par son nom (majuscules/minuscules ignorées).</summary>
		/// <returns>Le faux joueur trouvé, ou null.</returns>
		public static FakeGamePlayer FindByName(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
				return null;
			return GetFakes().FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
		}

		// ================================================================= suppression

		/// <summary>
		/// Supprime un faux joueur : le sort de son groupe, le retire du monde, libère son numéro de session.
		/// </summary>
		/// <returns>false s'il y a eu une erreur (détail dans la console du serveur).</returns>
		public static bool Remove(FakeGamePlayer fake)
		{
			if (fake == null)
				return false;

			lock (_lock)
				_fakes.Remove(fake);

			try
			{
				// Arrête le timer de position (il teste cet état à chaque passage).
				fake.Client.ClientState = GameClient.eClientState.Disconnected;

				// Sortir du groupe AVANT de quitter le monde, pour que la fenêtre de groupe se mette à jour.
				fake.Group?.RemoveMember(fake);

				if (fake.ObjectState != GameObject.eObjectState.Deleted)
					fake.Delete(); // retire du monde : les vrais joueurs voient le faux joueur disparaître

				// Rend le numéro de session au serveur (il pourra resservir).
				WorldMgr.RemoveClient(fake.Client);
				return true;
			}
			catch (Exception ex)
			{
				log.Error("[FakePlayers] erreur à la suppression de " + fake.Name, ex);
				return false;
			}
		}

		/// <summary>Supprime tous les faux joueurs du serveur.</summary>
		/// <returns>Le nombre de faux joueurs supprimés sans erreur.</returns>
		public static int RemoveAll()
		{
			int count = 0;
			foreach (FakeGamePlayer fake in GetFakes()) // GetFakes renvoie une copie : on peut supprimer pendant la boucle
			{
				if (Remove(fake))
					count++;
			}
			return count;
		}

		// ================================================================= outils internes

		/// <summary>Libère le numéro de session d'un faux client dont la création a échoué.</summary>
		private static void ReleaseClient(FakeGameClient client)
		{
			WorldMgr.RemoveClient(client);
			client.ClientState = GameClient.eClientState.Disconnected;
		}

		/// <summary>
		/// Vérifie un nom : lettres uniquement, entre NAME_MIN et NAME_MAX caractères,
		/// et pas "all" (réservé à /fake remove all).
		/// </summary>
		/// <param name="error">La raison du refus, ou null si le nom est valide.</param>
		/// <returns>true si le nom est accepté.</returns>
		private static bool IsValidName(string name, out string error)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				error = "il faut donner un nom";
				return false;
			}
			if (name.Length < NAME_MIN || name.Length > NAME_MAX)
			{
				error = "le nom doit faire entre " + NAME_MIN + " et " + NAME_MAX + " lettres";
				return false;
			}
			if (!name.All(char.IsLetter))
			{
				error = "le nom ne doit contenir que des lettres";
				return false;
			}
			if (name.Equals("all", StringComparison.OrdinalIgnoreCase))
			{
				error = "le nom \"all\" est réservé à /fake remove all";
				return false;
			}
			error = null;
			return true;
		}

		/// <summary>Met la première lettre en majuscule et le reste en minuscules : "bOB" devient "Bob".</summary>
		private static string FormatName(string name)
		{
			return char.ToUpperInvariant(name[0]) + name.Substring(1).ToLowerInvariant();
		}

		/// <summary>
		/// Fabrique le personnage du faux joueur, uniquement en mémoire (jamais écrit en base).
		/// Tout est copié sur le propriétaire : royaume, race, classe, niveau, apparence, caractéristiques,
		/// et position (le faux joueur apparaît là où se trouve le propriétaire).
		/// </summary>
		private static DOLCharacters BuildCharacter(GamePlayer owner, string name)
		{
			return new DOLCharacters
			{
				Name = name,
				AccountName = "fake_" + name.ToLowerInvariant(),
				Realm = (int)owner.Realm,
				Race = owner.Race,
				Gender = (int)owner.Gender,
				Class = owner.CharacterClass.ID,
				Level = owner.Level,
				CreationModel = owner.Model,
				CurrentModel = owner.Model,
				Strength = owner.Strength,
				Constitution = owner.Constitution,
				Dexterity = owner.Dexterity,
				Quickness = owner.Quickness,
				Intelligence = owner.Intelligence,
				Piety = owner.Piety,
				Empathy = owner.Empathy,
				Charisma = owner.Charisma,
				Region = owner.CurrentRegionID,
				Xpos = owner.Position.X,
				Ypos = owner.Position.Y,
				Zpos = owner.Position.Z,
				Direction = owner.Position.Orientation.InHeading,
			};
		}

		/// <summary>
		/// Fabrique le compte du faux joueur, uniquement en mémoire.
		/// Niveau de droits "joueur" et même langue que le propriétaire.
		/// </summary>
		private static Account BuildAccount(GamePlayer owner, DOLCharacters dbChar)
		{
			return new Account
			{
				Name = dbChar.AccountName,
				PrivLevel = (int)ePrivLevel.Player,
				Language = owner.Client.Account.Language,
				Characters = [dbChar],
			};
		}
	}
}
