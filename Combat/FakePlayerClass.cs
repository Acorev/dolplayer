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
	}
}
