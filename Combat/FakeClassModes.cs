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

		/// <summary>Au démarrage : ajoute à la table les classes qui n'y sont pas encore.</summary>
		[ScriptLoadedEvent]
		public static void OnScriptLoaded(DOLEvent e, object sender, EventArgs args)
		{
			try
			{
				var existing = new HashSet<int>(DOLDB<FakePlayerClass>.SelectAllObjects().Select(r => r.ClassID));
				var missing = DEFAULT_MODES
					.Where(kv => !existing.Contains((int)kv.Key))
					.Select(kv => new FakePlayerClass { ClassID = (int)kv.Key, ClassName = kv.Key.ToString(), CombatMode = kv.Value })
					.ToList();

				if (missing.Count > 0)
				{
					GameServer.Database.AddObject(missing);
					log.Info("[FakePlayers] table FakePlayerClass : " + missing.Count + " classe(s) ajoutée(s).");
				}
			}
			catch (Exception ex)
			{
				log.Error("[FakePlayers] impossible de remplir la table FakePlayerClass", ex);
			}
		}

		/// <summary>
		/// Mode de combat d'une classe, lu en base à chaque appel (les changements en base s'appliquent tout de suite).
		/// Classe absente de la table, ou valeur invalide : déduit du type de classe de DOLSharp
		/// (lanceur de sorts = 2, sinon 1).
		/// </summary>
		public static int GetMode(int classId)
		{
			try
			{
				FakePlayerClass row = DOLDB<FakePlayerClass>.SelectObject(DB.Column(nameof(FakePlayerClass.ClassID)).IsEqualTo(classId));
				if (row != null && (row.CombatMode == FakePlayerClass.MODE_MELEE || row.CombatMode == FakePlayerClass.MODE_SPELLS))
					return row.CombatMode;
			}
			catch (Exception ex)
			{
				log.Warn("[FakePlayers] lecture de FakePlayerClass impossible pour la classe " + classId, ex);
			}

			return CharacterClass.GetClass(classId).ClassType == eClassType.ListCaster
				? FakePlayerClass.MODE_SPELLS
				: FakePlayerClass.MODE_MELEE;
		}
	}
}
