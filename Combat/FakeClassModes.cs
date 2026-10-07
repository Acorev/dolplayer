/*
 * FakePlayers - Combat/FakeClassModes.cs
 *
 * Remplit la table FakePlayerClass au démarrage et lit le mode de combat d'une classe.
 *
 * Remplissage : au démarrage, les classes ABSENTES de la table sont ajoutées avec les valeurs
 * de DEFAULT_MODES (choix validés dans la feuille FakePlayers_combat_palier1.xlsx).
 * Les lignes déjà présentes ne sont JAMAIS modifiées : vos changements en base sont conservés.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.Database;
using DOL.Events;
using log4net;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Mode de combat des classes (table FakePlayerClass).
	/// </summary>
	public static class FakeClassModes
	{
		/// <summary>Journal du serveur (console + fichier de log).</summary>
		private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		private const int M = FakePlayerClass.MODE_MELEE;
		private const int S = FakePlayerClass.MODE_SPELLS;

		/// <summary>
		/// Valeurs de départ, utilisées seulement pour remplir la table la première fois.
		/// Ensuite, c'est la table en base qui fait foi.
		/// </summary>
		private static readonly Dictionary<eCharacterClass, int> DEFAULT_MODES = new()
		{
			// Albion
			[eCharacterClass.Armsman] = M, [eCharacterClass.Mercenary] = M, [eCharacterClass.Infiltrator] = M,
			[eCharacterClass.Paladin] = M, [eCharacterClass.Reaver] = M, [eCharacterClass.Minstrel] = M,
			[eCharacterClass.Friar] = S, [eCharacterClass.Heretic] = S, [eCharacterClass.Scout] = S,
			[eCharacterClass.MaulerAlb] = M,
			[eCharacterClass.Cleric] = S, [eCharacterClass.Wizard] = S, [eCharacterClass.Sorcerer] = S,
			[eCharacterClass.Theurgist] = S, [eCharacterClass.Cabalist] = S, [eCharacterClass.Necromancer] = S,
			// Midgard
			[eCharacterClass.Warrior] = M, [eCharacterClass.Berserker] = M, [eCharacterClass.Savage] = M,
			[eCharacterClass.Shadowblade] = M, [eCharacterClass.Thane] = M, [eCharacterClass.Skald] = M,
			[eCharacterClass.Valkyrie] = M, [eCharacterClass.Hunter] = S, [eCharacterClass.MaulerMid] = M,
			[eCharacterClass.Healer] = S, [eCharacterClass.Shaman] = S, [eCharacterClass.Runemaster] = S,
			[eCharacterClass.Spiritmaster] = S, [eCharacterClass.Bonedancer] = S, [eCharacterClass.Warlock] = S,
			// Hibernia
			[eCharacterClass.Hero] = M, [eCharacterClass.Blademaster] = M, [eCharacterClass.Nightshade] = M,
			[eCharacterClass.Champion] = M, [eCharacterClass.Warden] = S, [eCharacterClass.Valewalker] = M,
			[eCharacterClass.Vampiir] = M, [eCharacterClass.Ranger] = S, [eCharacterClass.Bard] = S,
			[eCharacterClass.MaulerHib] = M,
			[eCharacterClass.Druid] = S, [eCharacterClass.Eldritch] = S, [eCharacterClass.Enchanter] = S,
			[eCharacterClass.Mentalist] = S, [eCharacterClass.Animist] = S, [eCharacterClass.Bainshee] = S,
		};

		/// <summary>
		/// Aggro par défaut de chaque classe (%), utilisée quand la colonne AggroPercent vaut 0.
		/// Les classes absentes de cette liste sont à 100 %.
		/// </summary>
		private static readonly Dictionary<eCharacterClass, int> DEFAULT_AGGRO = BuildDefaultAggro();

		private static Dictionary<eCharacterClass, int> BuildDefaultAggro()
		{
			var result = new Dictionary<eCharacterClass, int>();
			void Set(int percent, params eCharacterClass[] classes)
			{
				foreach (eCharacterClass c in classes)
					result[c] = percent;
			}

			// Tanks : gardent l'aggro en tapant.
			Set(200, eCharacterClass.Armsman, eCharacterClass.Warrior, eCharacterClass.Hero);
			// Hybrides.
			Set(130, eCharacterClass.Paladin, eCharacterClass.Reaver, eCharacterClass.Thane,
				eCharacterClass.Valkyrie, eCharacterClass.Champion);
			// Lanceurs.
			Set(70, eCharacterClass.Wizard, eCharacterClass.Sorcerer, eCharacterClass.Theurgist,
				eCharacterClass.Cabalist, eCharacterClass.Necromancer,
				eCharacterClass.Runemaster, eCharacterClass.Spiritmaster, eCharacterClass.Bonedancer,
				eCharacterClass.Warlock,
				eCharacterClass.Eldritch, eCharacterClass.Enchanter, eCharacterClass.Mentalist,
				eCharacterClass.Animist, eCharacterClass.Bainshee);
			// Soigneurs.
			Set(30, eCharacterClass.Cleric, eCharacterClass.Friar, eCharacterClass.Healer,
				eCharacterClass.Shaman, eCharacterClass.Druid, eCharacterClass.Bard, eCharacterClass.Warden);
			// Toutes les autres (mêlée, furtifs, archers, chanteurs...) : 100.
			return result;
		}

		/// <summary>Aggro par défaut (%) d'une classe.</summary>
		public static int DefaultAggro(int classId)
			=> DEFAULT_AGGRO.TryGetValue((eCharacterClass)classId, out int percent) ? percent : 100;

		/// <summary>
		/// Au démarrage : ajoute à la table les classes qui n'y sont pas encore, et remplit la colonne
		/// AggroPercent des lignes où elle vaut 0 (colonne tout juste ajoutée) avec la valeur par défaut,
		/// pour qu'elle soit visible et modifiable en base. Les autres valeurs ne sont jamais touchées.
		/// </summary>
		[ScriptLoadedEvent]
		public static void OnScriptLoaded(DOLEvent e, object sender, EventArgs args)
		{
			try
			{
				var existing = new HashSet<int>(DOLDB<FakePlayerClass>.SelectAllObjects().Select(r => r.ClassID));
				var missing = DEFAULT_MODES
					.Where(kv => !existing.Contains((int)kv.Key))
					.Select(kv => new FakePlayerClass
					{
						ClassID = (int)kv.Key, ClassName = kv.Key.ToString(), CombatMode = kv.Value,
						AggroPercent = DefaultAggro((int)kv.Key),
					})
					.ToList();

				if (missing.Count > 0)
				{
					GameServer.Database.AddObject(missing);
					log.Info("[FakePlayers] table FakePlayerClass : " + missing.Count + " classe(s) ajoutée(s).");
				}

				var noAggro = DOLDB<FakePlayerClass>.SelectAllObjects().Where(r => r.AggroPercent == 0).ToList();
				if (noAggro.Count > 0)
				{
					foreach (FakePlayerClass row in noAggro)
						row.AggroPercent = DefaultAggro(row.ClassID);
					GameServer.Database.SaveObject(noAggro);
					log.Info("[FakePlayers] table FakePlayerClass : AggroPercent rempli pour " + noAggro.Count + " classe(s).");
				}
			}
			catch (Exception ex)
			{
				log.Error("[FakePlayers] impossible de remplir la table FakePlayerClass", ex);
			}
		}

		/// <summary>Seuil de soin par défaut d'un soigneur (% de vie), si la colonne vaut 0.</summary>
		public const int DEFAULT_HEAL_THRESHOLD = 75;

		/// <summary>Seuil de soin d'urgence par défaut (% de vie), si la colonne vaut 0.</summary>
		public const int DEFAULT_EMERGENCY_THRESHOLD = 40;

		/// <summary>
		/// Réglages d'une classe, lus en base à chaque appel d'un alt (les changements en base s'appliquent
		/// au prochain /fake call). Les valeurs "auto" (0) sont résolues ici :
		///  - CombatMode invalide : lanceur de sorts = 2, sinon 1 ;
		///  - HealMode 0 : soigneur si CombatMode 2, urgence seulement si CombatMode 1 ;
		///  - seuils 0 (ou hors 1..99) : valeurs par défaut ;
		///  - BuffMode 0 (ou invalide) : buffe le groupe ;
		///  - AggroPercent 0 (ou hors 1..AGGRO_MAX) : valeur par défaut de la classe ;
		///  - AoeMinTargets 0 : 3 mobs ; 99 et plus : jamais de sort de zone (0 dans les réglages).
		/// </summary>
		public static FakeClassSettings GetSettings(int classId)
		{
			FakePlayerClass row = null;
			try
			{
				row = DOLDB<FakePlayerClass>.SelectObject(DB.Column(nameof(FakePlayerClass.ClassID)).IsEqualTo(classId));
			}
			catch (Exception ex)
			{
				log.Warn("[FakePlayers] lecture de FakePlayerClass impossible pour la classe " + classId, ex);
			}

			int combatMode = row != null && (row.CombatMode == FakePlayerClass.MODE_MELEE || row.CombatMode == FakePlayerClass.MODE_SPELLS)
				? row.CombatMode
				: CharacterClass.GetClass(classId).ClassType == eClassType.ListCaster ? FakePlayerClass.MODE_SPELLS : FakePlayerClass.MODE_MELEE;

			int healMode = row?.HealMode ?? FakePlayerClass.HEAL_AUTO;
			if (healMode != FakePlayerClass.HEAL_NEVER && healMode != FakePlayerClass.HEAL_EMERGENCY && healMode != FakePlayerClass.HEAL_HEALER)
				healMode = combatMode == FakePlayerClass.MODE_SPELLS ? FakePlayerClass.HEAL_HEALER : FakePlayerClass.HEAL_EMERGENCY;

			int buffMode = row?.BuffMode ?? FakePlayerClass.BUFF_GROUP;
			if (buffMode != FakePlayerClass.BUFF_NEVER && buffMode != FakePlayerClass.BUFF_SELF)
				buffMode = FakePlayerClass.BUFF_GROUP;

			return new FakeClassSettings(
				combatMode,
				healMode,
				Percent(row?.HealThreshold ?? 0, DEFAULT_HEAL_THRESHOLD),
				Percent(row?.EmergencyThreshold ?? 0, DEFAULT_EMERGENCY_THRESHOLD),
				buffMode,
				row != null && row.AggroPercent >= 1 && row.AggroPercent <= FakePlayerClass.AGGRO_MAX
					? row.AggroPercent
					: DefaultAggro(classId),
				AoeMin(row?.AoeMinTargets ?? 0));
		}

		/// <summary>Nombre minimum de mobs pour un sort de zone, si la colonne vaut 0.</summary>
		public const int DEFAULT_AOE_MIN_TARGETS = 3;

		/// <summary>AoeMinTargets résolu : 0 (ou négatif) = défaut, 99 et plus = jamais (renvoie 0).</summary>
		private static int AoeMin(int value)
			=> value <= 0 ? DEFAULT_AOE_MIN_TARGETS : value >= FakePlayerClass.AOE_NEVER ? 0 : value;

		/// <summary>Un pourcentage valide (1 à 99), sinon la valeur par défaut.</summary>
		private static int Percent(int value, int fallback)
			=> value >= 1 && value <= 99 ? value : fallback;
	}

	/// <summary>Les réglages d'une classe, une fois les valeurs "auto" résolues.</summary>
	/// <param name="CombatMode">1 = mêlée, 2 = sorts.</param>
	/// <param name="HealMode">FakePlayerClass.HEAL_NEVER, HEAL_EMERGENCY ou HEAL_HEALER (jamais AUTO).</param>
	/// <param name="HealThreshold">% de vie sous lequel un soigneur soigne.</param>
	/// <param name="EmergencyThreshold">% de vie d'un soin d'urgence.</param>
	/// <param name="BuffMode">FakePlayerClass.BUFF_GROUP, BUFF_NEVER ou BUFF_SELF.</param>
	/// <param name="AggroPercent">Aggro générée, en % de l'aggro normale (1 à AGGRO_MAX).</param>
	/// <param name="AoeMinTargets">Mobs minimum pour un sort de zone (0 = jamais de sort de zone).</param>
	public record FakeClassSettings(int CombatMode, int HealMode, int HealThreshold, int EmergencyThreshold, int BuffMode,
		int AggroPercent, int AoeMinTargets);
}
