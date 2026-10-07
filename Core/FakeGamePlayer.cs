/*
 * FakePlayers - Core/FakeGamePlayer.cs
 *
 * L'alt lui-même. C'est un vrai GamePlayer du serveur, construit à partir d'un personnage
 * du compte du joueur (chargé depuis la base) : il a ses vraies stats, son équipement, ses specs...
 * Mais son "client" est un FakeGameClient sans connexion réseau.
 * Il est créé par FakePlayerMgr.Call, jamais directement.
 */

using DOL.Database;
using DOL.GS.Geometry;
using DOL.GS.PacketHandler;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Un alt : un vrai GamePlayer, piloté par le serveur, sauvegardé comme un joueur (mais à sa position d'origine).
	/// </summary>
	/// <param name="client">Son faux client réseau (FakeGameClient).</param>
	/// <param name="dbChar">Son personnage, chargé depuis la base.</param>
	public class FakeGamePlayer(GameClient client, DOLCharacters dbChar) : GamePlayer(client, dbChar)
	{
		/// <summary>
		/// Le vrai joueur qui a appelé cet alt.
		/// Les alts sont supprimés quand ce joueur se déconnecte (voir Events/FakeGroupWatcher),
		/// donc cette référence désigne toujours un joueur en jeu.
		/// </summary>
		public GamePlayer Owner { get; set; }

		/// <summary>
		/// true = l'alt suit son propriétaire (par défaut) ; false = il reste sur place (/fake stay).
		/// Utilisé par Movement/FakeFollowAction.
		/// </summary>
		public bool IsFollowing { get; set; } = true;

		/// <summary>
		/// true = aucun combat (/fake passive) ; false = combat selon les règles de Combat/FakeCombat (/fake fight).
		/// </summary>
		public bool IsPassive { get; set; }

		/// <summary>
		/// Mode de combat de sa classe, lu dans la table FakePlayerClass à l'appel :
		/// 1 = mêlée (va au contact), 2 = sorts (au palier 1 : se défend seulement).
		/// </summary>
		public int CombatMode { get; set; } = 1;

		/// <summary>
		/// Réglages de soin de sa classe (table FakePlayerClass, lus à l'appel) :
		/// FakePlayerClass.HEAL_NEVER, HEAL_EMERGENCY ou HEAL_HEALER. Voir Combat/FakeHeals.
		/// </summary>
		public int HealMode { get; set; } = FakePlayerClass.HEAL_EMERGENCY;

		/// <summary>Seuil de vie (%) sous lequel il soigne, en tant que soigneur.</summary>
		public int HealThreshold { get; set; } = FakeClassModes.DEFAULT_HEAL_THRESHOLD;

		/// <summary>Seuil de vie (%) d'un soin d'urgence.</summary>
		public int EmergencyThreshold { get; set; } = FakeClassModes.DEFAULT_EMERGENCY_THRESHOLD;

		/// <summary>Qui il buffe : FakePlayerClass.BUFF_GROUP, BUFF_NEVER ou BUFF_SELF. Voir Combat/FakeBuffs.</summary>
		public int BuffMode { get; set; } = FakePlayerClass.BUFF_GROUP;

		/// <summary>Aggro qu'il génère, en % de l'aggro normale (table FakePlayerClass). Voir Combat/FakeAggro.</summary>
		public int AggroPercent { get; set; } = 100;

		/// <summary>
		/// Niveau auquel sa liste de sorts a été lue pour la dernière fois (-1 = jamais).
		/// Quand il monte de niveau, la liste est relue (voir Combat/FakeSpellCast.KnownSpells).
		/// </summary>
		public int SpellsCheckedLevel { get; set; } = -1;

		/// <summary>Niveau auquel on a vérifié s'il connaît des soins (-1 = jamais). Voir Combat/FakeHeals.HasHealSpells.</summary>
		public int HealSpellsCheckedLevel { get; set; } = -1;

		/// <summary>true s'il connaît au moins un sort de soin utilisable (résultat de la dernière vérification).</summary>
		public bool KnowsHealSpells { get; set; }

		/// <summary>
		/// La cible désignée par le propriétaire avec /fake attack (null = pas d'ordre en cours).
		/// Passe avant tout le reste ; effacée quand la cible meurt ou disparaît, à la laisse,
		/// avec /fake passive ou un nouvel ordre. Voir Combat/FakeCombat.
		/// </summary>
		public GameLiving OrderedTarget { get; set; }

		// ================================================================= échange (voir Combat/FakeTrade)

		/// <summary>
		/// Un objet posé sur l'alt pour l'échange : accepté seulement de son propriétaire.
		/// L'alt accepte l'échange après lui (voir FakeTrade).
		/// </summary>
		public override bool ReceiveTradeItem(GamePlayer source, InventoryItem item)
		{
			if (!FakeTrade.IsAllowedPartner(this, source))
				return false;
			return base.ReceiveTradeItem(source, item);
		}

		/// <summary>De l'argent posé sur l'alt pour l'échange : accepté seulement de son propriétaire.</summary>
		public override bool ReceiveTradeMoney(GamePlayer source, long money)
		{
			if (!FakeTrade.IsAllowedPartner(this, source))
				return false;
			return base.ReceiveTradeMoney(source, money);
		}

		// ================================================================= sauvegarde

		/// <summary>Journal du serveur (console + fichier de log).</summary>
		private static readonly log4net.ILog FakeLog = log4net.LogManager.GetLogger(typeof(FakeGamePlayer));

		/// <summary>
		/// Position d'origine du personnage (là où il avait été déconnecté), lue en base à l'appel.
		/// C'est elle qui est sauvegardée, jamais la position de l'alt : en se connectant sur ce personnage,
		/// on le retrouve là où on l'avait laissé.
		/// </summary>
		public (int Region, int X, int Y, int Z, int Direction)? HomePosition { get; set; }

		/// <summary>
		/// Verrou entre le tic de l'alt (qui le déplace) et la sauvegarde : pendant la sauvegarde,
		/// la position en mémoire est remplacée un instant par la position d'origine.
		/// </summary>
		public readonly object SaveLock = new();

		/// <summary>
		/// Sauvegarde le personnage comme un vrai joueur (niveau, expérience, argent, inventaire, compétences...),
		/// mais avec sa position d'origine. Appelée par le serveur (montée de niveau, sauvegarde générale...)
		/// et au retrait de l'alt (Core/FakePlayerMgr.Remove).
		/// </summary>
		public override void SaveIntoDatabase()
		{
			lock (SaveLock)
			{
				// Le personnage en base de l'alt (le même objet que celui du GamePlayer, dont la propriété
				// DBCharacter n'est pas accessible aux scripts).
				DOLCharacters c = dbChar;
				if (c == null)
					return;

				if (HomePosition is not { } home)
				{
					base.SaveIntoDatabase();
					return;
				}

				// Position actuelle en mémoire, remise juste après la sauvegarde.
				var current = (c.Region, c.Xpos, c.Ypos, c.Zpos, c.Direction);
				try
				{
					c.Region = home.Region;
					c.Xpos = home.X;
					c.Ypos = home.Y;
					c.Zpos = home.Z;
					c.Direction = home.Direction;
					base.SaveIntoDatabase();
				}
				finally
				{
					(c.Region, c.Xpos, c.Ypos, c.Zpos, c.Direction) = current;
				}
			}
			RefreshOwnerCharacterList();
		}

		/// <summary>
		/// Le compte du propriétaire garde en mémoire la liste de ses personnages, lue à sa connexion.
		/// Sans mise à jour, en repassant par l'écran de sélection, il retrouverait ce personnage tel qu'il
		/// était à la connexion (ancien niveau), et le jouer écraserait la progression de l'alt.
		/// On y remplace donc ce personnage par sa version tout juste sauvegardée en base.
		/// </summary>
		private void RefreshOwnerCharacterList()
		{
			try
			{
				DOLCharacters[] characters = Owner?.Client?.Account?.Characters;
				if (characters == null)
					return;
				for (int i = 0; i < characters.Length; i++)
				{
					if (characters[i] != null && characters[i].ObjectId == InternalID)
					{
						DOLCharacters fresh = GameServer.Database.FindObjectByKey<DOLCharacters>(InternalID);
						if (fresh != null)
							characters[i] = fresh;
						return;
					}
				}
			}
			catch (System.Exception ex)
			{
				FakeLog.Warn("[FakePlayers] liste des personnages du compte non mise à jour pour " + Name, ex);
			}
		}

		// ================================================================= déplacement
		// Un être vivant de DOLSharp a un "mouvement" (Motion) : point de départ, destination, vitesse.
		// Sa position est calculée en continu à partir de ce mouvement : l'alt avance donc tout seul,
		// de façon fluide, entre deux tics de FakeFollowAction, et s'arrête pile sur la destination.

		/// <summary>Fait marcher l'alt en ligne droite vers un point, à cette vitesse.</summary>
		public void WalkTowards(Coordinate destination, short speed)
		{
			Motion = DOL.GS.Geometry.Motion.Create(Position, destination, speed);
		}

		/// <summary>Arrête l'alt là où il se trouve.</summary>
		public void StopWalking()
		{
			Motion = DOL.GS.Geometry.Motion.Create(Position, Coordinate.Nowhere, 0);
		}

		/// <summary>La destination du mouvement en cours (Coordinate.Nowhere à l'arrêt).</summary>
		public Coordinate WalkDestination => Motion.Destination;

		/// <summary>true si l'alt est en train de marcher.</summary>
		public bool IsWalking => Motion.Speed != 0 && Motion.Destination != Coordinate.Nowhere;
	}
}
