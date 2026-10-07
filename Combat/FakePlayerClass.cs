/*
 * FakePlayers - Combat/FakePlayerClass.cs
 *
 * La table "FakePlayerClass" de la base de données : le mode de combat des alts, par classe.
 * DOLSharp crée la table tout seul au démarrage (attribut [DataTable] dans un script),
 * et l'adapte si on ajoute des colonnes plus tard, sans perdre les données.
 *
 * MODIFIABLE DIRECTEMENT EN BASE : la valeur est relue à chaque /fake call (et /fake team),
 * donc un changement en base s'applique au prochain appel d'un alt, sans redémarrer.
 */

using DOL.Database;
using DOL.Database.Attributes;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Une ligne de la table : une classe et son mode de combat.
	/// </summary>
	[DataTable(TableName = "FakePlayerClass")]
	public class FakePlayerClass : DataObject
	{
		/// <summary>Mode de combat : au contact (mêlée).</summary>
		public const int MODE_MELEE = 1;

		/// <summary>Mode de combat : sorts (au palier 1 : suit le joueur et se défend seulement).</summary>
		public const int MODE_SPELLS = 2;

		private int m_classID;
		private string m_className = "";
		private int m_combatMode = MODE_MELEE;

		/// <summary>Numéro de la classe (eCharacterClass). Clé de la table.</summary>
		[PrimaryKey]
		public int ClassID
		{
			get => m_classID;
			set { Dirty = true; m_classID = value; }
		}

		/// <summary>Nom de la classe, pour s'y retrouver dans la base (pas utilisé par le code).</summary>
		[DataElement(AllowDbNull = false, Varchar = 50)]
		public string ClassName
		{
			get => m_className;
			set { Dirty = true; m_className = value; }
		}

		/// <summary>1 = mêlée, 2 = sorts.</summary>
		[DataElement(AllowDbNull = false)]
		public int CombatMode
		{
			get => m_combatMode;
			set { Dirty = true; m_combatMode = value; }
		}

		// ----------------------------------------------------------------- soins (palier 3)
		// 0 veut toujours dire "automatique / valeur par défaut" : quand DOLSharp ajoute ces colonnes
		// à une table existante, il y met 0, et tout fonctionne sans rien remplir.

		/// <summary>Soins : 0 = auto (mode 2 → soigneur, mode 1 → urgence), 1 = jamais, 2 = urgence seulement, 3 = soigneur.</summary>
		public const int HEAL_AUTO = 0;
		public const int HEAL_NEVER = 1;
		public const int HEAL_EMERGENCY = 2;
		public const int HEAL_HEALER = 3;

		private int m_healMode;
		private int m_healThreshold;
		private int m_emergencyThreshold;

		/// <summary>Qui soigne et comment (voir les constantes HEAL_...).</summary>
		[DataElement(AllowDbNull = false)]
		public int HealMode
		{
			get => m_healMode;
			set { Dirty = true; m_healMode = value; }
		}

		/// <summary>Seuil de vie (%) sous lequel un soigneur soigne. 0 = défaut (75).</summary>
		[DataElement(AllowDbNull = false)]
		public int HealThreshold
		{
			get => m_healThreshold;
			set { Dirty = true; m_healThreshold = value; }
		}

		/// <summary>Seuil de vie (%) d'un soin d'urgence. 0 = défaut (40).</summary>
		[DataElement(AllowDbNull = false)]
		public int EmergencyThreshold
		{
			get => m_emergencyThreshold;
			set { Dirty = true; m_emergencyThreshold = value; }
		}

		// ----------------------------------------------------------------- buffs (palier 3)

		/// <summary>Buffs : 0 = auto (buffe le groupe), 1 = jamais, 2 = seulement lui-même.</summary>
		public const int BUFF_GROUP = 0;
		public const int BUFF_NEVER = 1;
		public const int BUFF_SELF = 2;

		private int m_buffMode;

		/// <summary>Qui l'alt buffe (voir les constantes BUFF_...). 0 = auto : tout le groupe.</summary>
		[DataElement(AllowDbNull = false)]
		public int BuffMode
		{
			get => m_buffMode;
			set { Dirty = true; m_buffMode = value; }
		}

		// ----------------------------------------------------------------- aggro

		/// <summary>Pourcentage d'aggro maximum accepté ; au-delà (faute de frappe), la valeur par défaut s'applique.</summary>
		public const int AGGRO_MAX = 500;

		private int m_aggroPercent;

		/// <summary>
		/// Aggro générée par l'alt (dégâts et soins), en % de l'aggro normale : 30 = trois fois moins,
		/// 200 = deux fois plus. 0 = valeur par défaut de la classe (voir Combat/FakeClassModes).
		/// Valeurs acceptées : 1 à AGGRO_MAX. Voir Combat/FakeAggro.
		/// </summary>
		[DataElement(AllowDbNull = false)]
		public int AggroPercent
		{
			get => m_aggroPercent;
			set { Dirty = true; m_aggroPercent = value; }
		}

		// ----------------------------------------------------------------- sorts de zone

		/// <summary>AoeMinTargets à partir duquel l'alt ne lance jamais de sort de zone.</summary>
		public const int AOE_NEVER = 99;

		private int m_aoeMinTargets;

		/// <summary>
		/// Nombre minimum de mobs (qui se battent déjà contre le groupe) dans la zone pour lancer un sort de zone.
		/// 0 = défaut (3), 99 = jamais de sort de zone. Voir Combat/FakeDamage.
		/// </summary>
		[DataElement(AllowDbNull = false)]
		public int AoeMinTargets
		{
			get => m_aoeMinTargets;
			set { Dirty = true; m_aoeMinTargets = value; }
		}
	}
}
