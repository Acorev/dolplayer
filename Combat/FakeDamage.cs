/*
 * FakePlayers - Combat/FakeDamage.cs
 *
 * Combat des alts, PALIER 3 : les SORTS DE DÉGÂTS.
 * Appelé par Combat/FakeCombat pour un alt "lanceur de dégâts" qui a une cible.
 *
 * Qui lance des sorts de dégâts (IsNuker) :
 *   - les alts en mode 2 (sorts, table FakePlayerClass) ;
 *   - SAUF les soigneurs (HealMode = soigneur) : un soigneur soigne OU fait des dégâts, pas les deux.
 *     Pour qu'un Cleric attaque, passer sa classe en HealMode 2 (urgence) ou 1 (jamais).
 *     Un alt réglé "soigneur" qui ne connaît AUCUN soin (mage, Cleric de niveau 1...) attaque.
 * Ils suivent la même logique de cible que la mêlée (ordre /fake attack, défense de soi, du propriétaire,
 * du groupe...), mais restent À DISTANCE : ils s'approchent jusqu'à la portée du sort, jamais au contact.
 *
 * Sorts retenus : dégâts directs, bolts, drains de vie, dégâts sur la durée (relancés seulement si la cible
 * ne les a plus). Écartés pour l'instant : contrôles (mez, stun, root), sorts de zone, debuffs, sorts focus.
 * Choix : un dégât sur la durée si la cible ne l'a pas, sinon le sort direct prêt qui fait le plus de dégâts.
 * Mana : pas de réserve. À court de mana (ou sans sort prêt), l'alt reste en retrait et ne frappe au corps
 * à corps que si la cible est sur lui.
 */

using System.Collections.Generic;
using System.Linq;
using DOL.GS.Spells;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Les sorts de dégâts d'un alt (palier 3).
	/// </summary>
	public static class FakeDamage
	{
		/// <summary>Un sort de dégâts retenu.</summary>
		private record DamageSpell(Spell Spell, SpellLine Line, bool IsOverTime);

		/// <summary>Types de sorts de dégâts pris en charge (en majuscules).</summary>
		private static readonly HashSet<string> DIRECT_TYPES = new() { "DIRECTDAMAGE", "BOLT", "LIFEDRAIN", "DIRECTDAMAGEWITHDEBUFF" };
		private static readonly HashSet<string> OVER_TIME_TYPES = new() { "DAMAGEOVERTIME" };

		/// <summary>
		/// true si l'alt attaque avec des sorts : mode 2, et pas soigneur.
		/// (Un soigneur soigne ou fait des dégâts, pas les deux.)
		/// Un alt réglé "soigneur" mais SANS aucun sort de soin (un mage, un Cleric de niveau 1...)
		/// est traité comme lanceur de dégâts.
		/// </summary>
		public static bool IsNuker(FakeGamePlayer fake)
			=> fake.CombatMode == FakePlayerClass.MODE_SPELLS
			   && !(fake.HealMode == FakePlayerClass.HEAL_HEALER && FakeHeals.HasHealSpells(fake));

		// ================================================================= combat

		/// <summary>
		/// Fait combattre l'alt à distance : choisit un sort, s'approche jusqu'à sa portée, le lance.
		/// </summary>
		/// <returns>true si la position a changé (il faut l'envoyer aux joueurs proches).</returns>
		internal static bool Fight(FakeGamePlayer fake, GameLiving target, FakeFollowAction.FollowState state)
		{
			DamageSpell spell = ChooseSpell(fake, target);

			if (spell == null)
			{
				// Plus de mana ou rien de prêt : en retrait, sauf si la cible est sur lui (il se défend au contact).
				if (target.TargetObject == fake && fake.IsWithinRadius(target, fake.AttackRange))
					return FakeCombat.Melee(fake, target, state);

				FakeSpellCast.Trace(fake, "dégâts : aucun sort prêt (mana " + fake.Mana + "/" + fake.MaxMana + ")");
				if (fake.AttackState)
					fake.StopAttack();
				return FakeFollowAction.Stop(fake, state);
			}

			// Un lanceur ne frappe pas au corps à corps pendant qu'il incante.
			if (fake.AttackState)
				fake.StopAttack();

			FakeSpellCast.TryCast(fake, state, spell.Spell, spell.Line, target, "dégâts", out bool moved);
			return moved;
		}

		// ================================================================= classement (aussi pour /fake spells)

		/// <summary>
		/// Classe un sort pour les dégâts.
		/// </summary>
		/// <param name="isDamage">true si l'alt peut s'en servir comme sort de dégâts.</param>
		/// <returns>Le verdict en clair : "dégâts", "dégâts sur la durée" ou la raison de l'écart.</returns>
		public static string Classify(Spell spell, out bool isDamage)
		{
			isDamage = false;
			string type = (spell.SpellType ?? "").ToUpperInvariant();
			string target = (spell.Target ?? "").ToLowerInvariant();

			bool direct = DIRECT_TYPES.Contains(type);
			bool overTime = OVER_TIME_TYPES.Contains(type);
			if (!direct && !overTime)
				return target == "enemy" ? "écarté : sort offensif non pris en charge (" + spell.SpellType + ")" : "écarté : pas un sort de dégâts";

			if (target != "enemy")
				return "écarté : cible \"" + spell.Target + "\" non prise en charge";
			if (spell.Radius > 0)
				return "écarté : sort de zone";
			if (spell.Range <= 0)
				return "écarté : sort de zone autour du lanceur";
			if (spell.IsPulsing || (spell.Frequency > 0 && !overTime))
				return "écarté : sort pulsé";
			if (spell.IsFocus)
				return "écarté : sort focus (à maintenir)";
			if (spell.NeedInstrument)
				return "écarté : demande un instrument";

			isDamage = true;
			return overTime ? "dégâts sur la durée" : "dégâts";
		}

		// ================================================================= outils internes

		/// <summary>
		/// Le sort à lancer : un dégât sur la durée si la cible ne l'a pas, sinon le sort direct prêt
		/// qui fait le plus de dégâts. null si rien n'est prêt.
		/// </summary>
		private static DamageSpell ChooseSpell(FakeGamePlayer fake, GameLiving target)
		{
			List<DamageSpell> ready = KnownDamage(fake).Where(d => FakeSpellCast.IsReady(fake, d.Spell)).ToList();

			DamageSpell dot = ready.Where(d => d.IsOverTime && SpellHandler.FindEffectOnTarget(target, d.Spell.SpellType) == null)
			                       .OrderByDescending(d => d.Spell.Damage).FirstOrDefault();
			if (dot != null)
				return dot;

			return ready.Where(d => !d.IsOverTime)
			            .OrderByDescending(d => d.Spell.Damage).ThenByDescending(d => d.Spell.Level)
			            .FirstOrDefault();
		}

		/// <summary>Les sorts de dégâts que l'alt peut utiliser (voir Classify).</summary>
		private static List<DamageSpell> KnownDamage(FakeGamePlayer fake)
		{
			var result = new List<DamageSpell>();
			foreach (FakeSpellCast.KnownSpell known in FakeSpellCast.KnownSpells(fake))
			{
				string verdict = Classify(known.Spell, out bool isDamage);
				if (isDamage)
					result.Add(new DamageSpell(known.Spell, known.Line, verdict == "dégâts sur la durée"));
			}
			return result;
		}
	}
}
