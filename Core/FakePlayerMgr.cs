/*
 * FakePlayers - Core/FakePlayerMgr.cs
 *
 * Le "chef d'orchestre" des alts : c'est ici qu'on les appelle, qu'on les retrouve et qu'on les supprime.
 * Toutes les autres classes passent par lui (la commande /fake n'appelle que FakePlayerMgr).
 *
 * Un "alt" est un personnage déjà créé sur le compte du joueur, chargé depuis la base de données
 * et piloté par le serveur. Il est sauvegardé comme un joueur, à sa position d'origine (voir FakeGamePlayer).
 *
 * Appel d'un alt, étape par étape (méthode Call) :
 *   1. vérifier les règles (son compte, son royaume, pas déjà en jeu, place dans le groupe) ;
 *   2. charger le personnage depuis la base et le placer, en mémoire seulement, à côté du joueur ;
 *   3. fabriquer un faux client réseau (FakeGameClient) et lui donner un numéro de session ;
 *   4. fabriquer le faux joueur (FakeGamePlayer) : le serveur charge alors tout seul son équipement,
 *      ses spécialisations, ses sorts... puis l'ajouter au monde ;
 *   5. démarrer l'envoi de sa position (FakePositionSender) et le suivi (FakeFollowAction),
 *      puis le grouper avec le joueur.
 *
 * Les suppressions automatiques (disband, déconnexion) sont déclenchées par Events/FakeGroupWatcher.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DOL.Database;
using DOL.GS.PacketHandler;
using log4net;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Appel, liste et suppression des alts.
	/// </summary>
	public static class FakePlayerMgr
	{
		/// <summary>Journal du serveur (console + fichier de log) pour écrire les erreurs.</summary>
		private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		/// <summary>
		/// Tous les alts en jeu sur le serveur.
		/// Toujours y accéder sous verrou (_lock) : la commande, les événements et les timers du serveur
		/// peuvent tourner en même temps sur des threads différents.
		/// </summary>
		private static readonly List<FakeGamePlayer> _fakes = new();

		/// <summary>Verrou qui protège la liste _fakes.</summary>
		private static readonly object _lock = new();

		// ================================================================= appel

		/// <summary>
		/// Appelle un personnage du compte du joueur, à côté de lui, et le groupe avec lui.
		/// </summary>
		/// <param name="owner">Le vrai joueur qui appelle son alt.</param>
		/// <param name="charName">Le nom du personnage à appeler (majuscules/minuscules ignorées).</param>
		/// <param name="error">En cas d'échec : la raison, en clair, à afficher au joueur. Sinon null.</param>
		/// <returns>L'alt en jeu, ou null en cas d'échec.</returns>
		public static FakeGamePlayer Call(GamePlayer owner, string charName, out string error)
		{
			// --- 1. Vérifications
			if (owner == null || owner.Client?.Account == null)
			{
				error = "joueur introuvable";
				return null;
			}
			if (string.IsNullOrWhiteSpace(charName))
			{
				error = "il faut donner le nom d'un de vos personnages";
				return null;
			}
			if (!CheckGroupRoom(owner, out error))
				return null;

			DOLCharacters dbChar = LoadCharacter(owner, charName, out error);
			if (dbChar == null)
				return null;

			// --- 2. Position : à côté du joueur. La position d'origine est gardée : c'est elle qui sera
			// sauvegardée (voir FakeGamePlayer.SaveIntoDatabase).
			var home = (dbChar.Region, dbChar.Xpos, dbChar.Ypos, dbChar.Zpos, dbChar.Direction);
			dbChar.Region = owner.CurrentRegionID;
			dbChar.Xpos = owner.Position.X;
			dbChar.Ypos = owner.Position.Y;
			dbChar.Zpos = owner.Position.Z;
			dbChar.Direction = owner.Position.Orientation.InHeading;

			// --- 3. Faux client, avec un compte fictif en mémoire.
			// On n'utilise PAS le vrai compte : sinon le serveur pourrait croire ce compte connecté deux fois.
			var client = new FakeGameClient(BuildAccount(owner, dbChar));
			if (WorldMgr.CreateSessionID(client) < 0)
			{
				error = "le serveur est plein (plus de numéro de session libre)";
				return null;
			}

			// --- 4. L'alt lui-même, ajouté au monde.
			FakeGamePlayer fake;
			try
			{
				// Le constructeur de GamePlayer charge tout depuis la base : inventaire, specs, sorts, artisanat...
				// Réglages de sa classe (table FakePlayerClass), relus en base à chaque appel.
				FakeClassSettings settings = FakeClassModes.GetSettings(dbChar.Class);
				fake = new FakeGamePlayer(client, dbChar)
				{
					Owner = owner,
					CombatMode = settings.CombatMode,
					HealMode = settings.HealMode,
					HealThreshold = settings.HealThreshold,
					EmergencyThreshold = settings.EmergencyThreshold,
					BuffMode = settings.BuffMode,
					AggroPercent = settings.AggroPercent,
					HomePosition = home,
				};
				client.Player = fake;
				client.ClientState = GameClient.eClientState.Playing; // le serveur le considère "en jeu"

				if (!fake.AddToWorld())
				{
					error = "impossible d'ajouter " + dbChar.Name + " au monde ici";
					ReleaseClient(client);
					return null;
				}
			}
			catch (Exception ex)
			{
				// La trace complète part dans la console du serveur, le joueur reçoit un message court.
				log.Error("[FakePlayers] erreur à l'appel de " + dbChar.Name, ex);
				error = "erreur à l'appel : " + ex.Message;
				ReleaseClient(client);
				return null;
			}

			// --- 5. Démarré seulement maintenant : l'alt est dans le monde, sa région est connue.
			FakePositionSender.Start(fake);
			FakeFollowAction.Start(fake);
			lock (_lock)
				_fakes.Add(fake);

			if (!JoinOwnerGroup(owner, fake))
			{
				Remove(fake);
				error = "impossible de grouper " + fake.Name + " (groupe plein ?)";
				return null;
			}

			error = null;
			return fake;
		}

		// ================================================================= recherche

		/// <summary>
		/// Liste de tous les alts encore en jeu.
		/// Retourne une copie : on peut la parcourir (et supprimer des alts pendant le parcours) sans risque.
		/// Les alts disparus entre-temps sont retirés au passage.
		/// </summary>
		public static List<FakeGamePlayer> GetFakes()
		{
			lock (_lock)
			{
				_fakes.RemoveAll(f => f.ObjectState == GameObject.eObjectState.Deleted);
				return _fakes.ToList();
			}
		}

		/// <summary>Les alts en jeu appelés par ce joueur.</summary>
		public static List<FakeGamePlayer> GetFakesOf(GamePlayer owner)
		{
			return GetFakes().Where(f => f.Owner == owner).ToList();
		}

		/// <summary>Cherche un alt en jeu par son nom (majuscules/minuscules ignorées).</summary>
		/// <returns>L'alt trouvé, ou null.</returns>
		public static FakeGamePlayer FindByName(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
				return null;
			return GetFakes().FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
		}

		// ================================================================= suppression

		/// <summary>
		/// Supprime un alt : le sort de son groupe, le retire du monde, libère son numéro de session.
		/// Sans effet (et sans erreur) s'il a déjà été supprimé : les événements de groupe peuvent
		/// demander plusieurs fois la même suppression.
		/// </summary>
		/// <returns>false s'il y a eu une erreur (détail dans la console du serveur).</returns>
		public static bool Remove(FakeGamePlayer fake)
		{
			if (fake == null)
				return false;

			lock (_lock)
			{
				if (!_fakes.Remove(fake))
					return true; // déjà supprimé
			}

			try
			{
				// Sauvegarde sa progression (niveau, expérience, argent, inventaire...) avant de le retirer.
				fake.SaveIntoDatabase();

				// Arrête le timer de position (il teste cet état à chaque passage).
				fake.Client.ClientState = GameClient.eClientState.Disconnected;

				// Sortir du groupe AVANT de quitter le monde, pour que la fenêtre de groupe se mette à jour.
				fake.Group?.RemoveMember(fake);

				if (fake.ObjectState != GameObject.eObjectState.Deleted)
					fake.Delete(); // retire du monde : les vrais joueurs voient l'alt disparaître

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

		/// <summary>Supprime tous les alts du serveur.</summary>
		/// <returns>Le nombre d'alts supprimés sans erreur.</returns>
		public static int RemoveAll()
		{
			return RemoveList(GetFakes());
		}

		/// <summary>Supprime tous les alts appelés par ce joueur.</summary>
		/// <returns>Le nombre d'alts supprimés sans erreur.</returns>
		public static int RemoveAllOf(GamePlayer owner)
		{
			return RemoveList(GetFakesOf(owner));
		}

		/// <summary>
		/// Supprime un alt un tout petit peu plus tard (au prochain tic de sa région).
		/// Utilisé par les événements de groupe : on laisse le serveur finir de mettre à jour le groupe
		/// avant de le modifier à nouveau, pour éviter les erreurs du type "Sequence contains no elements".
		/// </summary>
		public static void RemoveLater(FakeGamePlayer fake)
		{
			if (fake == null || fake.ObjectState == GameObject.eObjectState.Deleted)
				return;
			new RegionTimer(fake, t => { Remove(fake); return 0; }).Start(1);
		}

		/// <summary>
		/// Verrou du travail en arrière-plan : un seul appel/rappel à la fois (voir RunInBackground).
		/// </summary>
		private static readonly object _backgroundLock = new();

		/// <summary>
		/// Exécute un travail EN ARRIÈRE-PLAN (Task.Run), un seul à la fois.
		///
		/// Pourquoi en arrière-plan : appeler un alt le recharge depuis la base (inventaire, specs,
		/// quêtes...), ce qui prend du temps. Sur le fil d'une région, ça figerait tous ses mobs ;
		/// sur le fil des paquets du joueur, ça bloquerait ses commandes.
		/// Pourquoi un à la fois : avec plusieurs alts, le groupe ne doit jamais se retrouver un instant
		/// réduit au seul propriétaire (il serait dissous, et les alts déjà appelés supprimés).
		/// </summary>
		/// <param name="what">Description pour le log en cas d'erreur.</param>
		/// <param name="work">Le travail à faire.</param>
		public static void RunInBackground(string what, Action work)
		{
			Task.Run(() =>
			{
				try
				{
					lock (_backgroundLock)
						work();
				}
				catch (Exception ex)
				{
					log.Error("[FakePlayers] erreur en arrière-plan (" + what + ")", ex);
				}
			});
		}

		/// <summary>
		/// Supprime puis rappelle un alt à côté de son propriétaire, en arrière-plan.
		/// Utilisé quand le propriétaire change de région : un faux client ne peut pas faire
		/// le changement de région d'un vrai joueur (le serveur attend une confirmation du jeu).
		/// L'alt est sauvegardé au retrait puis rechargé depuis la base : il ne perd rien.
		/// Ses ordres (stay/follow, passive/fight) sont gardés.
		/// </summary>
		public static void Recall(FakeGamePlayer fake)
		{
			GamePlayer owner = fake.Owner;
			string name = fake.Name;
			bool following = fake.IsFollowing;
			bool passive = fake.IsPassive;

			RunInBackground("rappel de " + name, () =>
			{
				// Suppression ET rappel ensemble, sous le même verrou (voir RunInBackground).
				Remove(fake);
				if (owner == null || owner.ObjectState != GameObject.eObjectState.Active)
					return;

				FakeGamePlayer again = Call(owner, name, out string error);
				if (again == null)
					owner.Out.SendMessage(name + " n'a pas pu vous suivre : " + error + ".", eChatType.CT_System, eChatLoc.CL_SystemWindow);
				else
				{
					again.IsFollowing = following;
					again.IsPassive = passive;
				}
			});
		}

		// ================================================================= outils internes

		/// <summary>Supprime une liste d'alts et compte les réussites.</summary>
		private static int RemoveList(List<FakeGamePlayer> fakes)
		{
			int count = 0;
			foreach (FakeGamePlayer fake in fakes)
			{
				if (Remove(fake))
					count++;
			}
			return count;
		}

		/// <summary>
		/// Charge un personnage depuis la base et vérifie les règles :
		/// il appartient au compte du joueur, est de son royaume, n'est pas le personnage joué
		/// et n'est pas déjà en jeu (comme alt ou comme vrai joueur).
		/// </summary>
		/// <param name="error">La raison du refus, ou null.</param>
		/// <returns>Le personnage, ou null en cas de refus.</returns>
		private static DOLCharacters LoadCharacter(GamePlayer owner, string charName, out string error)
		{
			DOLCharacters dbChar = FindOwnCharacter(owner, charName, out error);
			if (dbChar == null)
				return null;

			if (FindByName(dbChar.Name) != null || WorldMgr.GetClientByPlayerName(dbChar.Name, true, false) != null)
			{
				error = dbChar.Name + " est déjà en jeu";
				return null;
			}

			error = null;
			return dbChar;
		}

		/// <summary>
		/// Cherche un personnage en base et vérifie qu'il peut servir d'alt à ce joueur :
		/// il appartient à son compte, est de son royaume et n'est pas le personnage joué.
		/// (Ne vérifie pas s'il est déjà en jeu : utilisé aussi pour composer l'équipe, voir FakeTeam.)
		/// </summary>
		/// <param name="error">La raison du refus, ou null.</param>
		/// <returns>Le personnage, ou null en cas de refus.</returns>
		public static DOLCharacters FindOwnCharacter(GamePlayer owner, string charName, out string error)
		{
			if (string.IsNullOrWhiteSpace(charName))
			{
				error = "il faut donner le nom d'un de vos personnages";
				return null;
			}

			DOLCharacters dbChar = DOLDB<DOLCharacters>.SelectObject(DB.Column(nameof(DOLCharacters.Name)).IsEqualTo(charName));

			// Même message que le personnage n'existe pas ou qu'il soit sur un autre compte :
			// on ne révèle pas les personnages des autres joueurs.
			if (dbChar == null || !string.Equals(dbChar.AccountName, owner.Client.Account.Name, StringComparison.OrdinalIgnoreCase))
			{
				error = "aucun personnage \"" + charName + "\" sur votre compte";
				return null;
			}
			if (string.Equals(dbChar.Name, owner.Name, StringComparison.OrdinalIgnoreCase))
			{
				error = "c'est le personnage avec lequel vous jouez";
				return null;
			}
			if (dbChar.Realm != (int)owner.Realm)
			{
				error = dbChar.Name + " n'est pas de votre royaume";
				return null;
			}

			error = null;
			return dbChar;
		}

		/// <summary>
		/// Vérifie qu'il y a de la place pour un alt dans le groupe du joueur :
		/// pas de groupe (il sera créé), ou un groupe dont il est le chef et qui n'est pas plein.
		/// </summary>
		private static bool CheckGroupRoom(GamePlayer owner, out string error)
		{
			Group group = owner.Group;
			if (group != null && group.Leader != owner)
			{
				error = "vous devez être chef de votre groupe";
				return false;
			}
			if (group != null && group.MemberCount >= ServerProperties.Properties.GROUP_MAX_MEMBER)
			{
				error = "votre groupe est plein";
				return false;
			}
			error = null;
			return true;
		}

		/// <summary>
		/// Ajoute l'alt au groupe du joueur. Si le joueur n'a pas de groupe, il est créé avec lui comme chef
		/// (même ordre que le serveur quand un joueur accepte une invitation).
		/// </summary>
		/// <returns>false si l'alt n'a pas pu être ajouté (groupe plein).</returns>
		private static bool JoinOwnerGroup(GamePlayer owner, FakeGamePlayer fake)
		{
			if (owner.Group == null)
			{
				var group = new Group(owner);
				GroupMgr.AddGroup(group);
				group.AddMember(owner);
			}
			return owner.Group.AddMember(fake);
		}

		/// <summary>Libère le numéro de session d'un faux client dont l'appel a échoué.</summary>
		private static void ReleaseClient(FakeGameClient client)
		{
			WorldMgr.RemoveClient(client);
			client.ClientState = GameClient.eClientState.Disconnected;
		}

		/// <summary>
		/// Fabrique le compte fictif du faux client, uniquement en mémoire.
		/// Niveau de droits "joueur" et même langue que le propriétaire.
		/// </summary>
		private static Account BuildAccount(GamePlayer owner, DOLCharacters dbChar)
		{
			return new Account
			{
				Name = "fake_" + dbChar.Name.ToLowerInvariant(),
				PrivLevel = (int)ePrivLevel.Player,
				Language = owner.Client.Account.Language,
				Characters = [dbChar],
			};
		}
	}
}
