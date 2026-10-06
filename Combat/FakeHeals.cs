/*
 * FakePlayers - Combat/FakeHeals.cs
 *
 * Combat des alts, PALIER 3 : les SOINS.
 * Appelé à chaque tic par Movement/FakeFollowAction, AVANT les buffs et le combat : soigner passe en premier.
 *
 * Réglages par classe dans la table FakePlayerClass (voir Combat/FakeClassModes) :
 *   - HealMode : jamais / urgence seulement / soigneur (auto : mode 2 → soigneur, mode 1 → urgence) ;
 *   - HealThreshold (soigneur, défaut 75 %) et EmergencyThreshold (urgence, défaut 40 %).
 * Une classe sans sort de soin ne soigne jamais, quel que soit le réglage.
 *
 * Fonctionnement :
 *   - soigne les membres du groupe (propriétaire, alts, autres joueurs), le plus blessé en % d'abord ;
 *   - au moins GROUP_HEAL_MIN_WOUNDED blessés et un soin de groupe connu → soin de groupe ;
 *   - sinon soin simple : le plus petit qui couvre la vie manquante, ou le plus gros s'il n'y en a pas assez ;
 *   - l'alt s'arrête pour lancer son sort ; trop loin ou hors de vue : il s'approche d'abord
 *     (voir Combat/FakeSpellCast) ;
 *   - pas de réserve de mana : les soins passent avant tout.
 * Pas encore pris en charge : soins sur la durée, régénération, guérisons (poison, maladie), chants.
 */

using System.Collections.Generic;
using System.Linq;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Les soins d'un alt (palier 3).
	/// </summary>
	public static class FakeHeals
	{
		/// <summary>À partir de ce nombre de blessés, l'alt préfère un soin de groupe.</summary>
		public const int GROUP_HEAL_MIN_WOUNDED = 3;

		/// <summary>Un sort de soin retenu, avec son genre (simple ou de groupe).</summary>
		private record HealSpell(Spell Spell, SpellLine Line, bool IsGroup);

		// ================================================================= soins

		/// <summary>
		/// Soigne si besoin.
		/// </summary>
		/// <param name="moved">true si la position de l'alt a changé (à envoyer aux joueurs proches).</param>
		/// <returns>true si l'alt s'occupe d'un soin ce tic (les buffs, le combat et le suivi attendent).</returns>
		internal static bool Update(FakeGamePlayer fake, GamePlayer owner, FakeFollowAction.FollowState state, out bool moved)
		{
			moved = false;
			if (fake.HealMode == FakePlayerClass.HEAL_NEVER || !fake.IsAlive)
				return false;

			int threshold = fake.HealMode == FakePlayerClass.HEAL_HEALER ? fake.HealThreshold : fake.EmergencyThreshold;

			// Les blessés, le plus blessé d'abord.
			List<GameLiving> wounded = FakeSpellCast.Members(fake, owner)
				.Where(m => m.HealthPercent < threshold)
				.OrderBy(m => m.HealthPercent)
				.ToList();
			if (wounded.Count == 0)
				return false;

			List<HealSpell> heals = KnownHeals(fake);
			if (heals.Count == 0)
			{
				FakeSpellCast.Trace(fake, "soin : aucun sort de soin connu (niveau " + fake.Level + ", voir /fake spells)");
				return false;
			}

			List<HealSpell> ready = heals.Where(h => FakeSpellCast.IsReady(fake, h.Spell)).ToList();
			if (ready.Count == 0)
			{
				FakeSpellCast.Trace(fake, "soin : aucun soin prêt (recharge ou mana " + fake.Mana + "/" + fake.MaxMana + ")");
				return false; // l'alt continue à combattre en attendant
			}

			// --- Soin de groupe, s'il y a assez de blessés.
			if (wounded.Count >= GROUP_HEAL_MIN_WOUNDED)
			{
				HealSpell group = ready.Where(h => h.IsGroup).OrderByDescending(h => h.Spell.Value).FirstOrDefault();
				if (group != null)
					return FakeSpellCast.TryCast(fake, state, group.Spell, group.Line, fake, "soin", out moved)
					       != FakeSpellCast.Result.Failed;
			}

			// --- Soin simple sur le plus blessé.
			GameLiving target = wounded[0];
			List<HealSpell> singles = ready.Where(h => !h.IsGroup && (target == fake || h.Spell.Range > 0)).ToList();
			if (singles.Count == 0)
			{
				FakeSpellCast.Trace(fake, "soin : aucun soin simple utilisable sur " + target.Name);
				return false;
			}

			int missing = target.MaxHealth - target.Health;
			HealSpell heal = singles.Where(h => h.Spell.Value >= missing).OrderBy(h => h.Spell.Value).FirstOrDefault()
			                 ?? singles.OrderByDescending(h => h.Spell.Value).First();

			return FakeSpellCast.TryCast(fake, state, heal.Spell, heal.Line, target, "soin", out moved)
			       != FakeSpellCast.Result.Failed;
		}

		// ================================================================= classement (aussi pour /fake spells)

		/// <summary>
		/// Classe un sort pour les soins.
		/// </summary>
		/// <param name="isHeal">true si l'alt peut s'en servir comme soin.</param>
		/// <param name="isGroup">true si c'est un soin de groupe.</param>
		/// <returns>Le verdict en clair : "soin simple", "soin de groupe" ou la raison de l'écart.</returns>
		public static string Classify(Spell spell, out bool isHeal, out bool isGroup)
		{
			isHeal = false;
			isGroup = false;

			string type = (spell.SpellType ?? "").ToUpperInvariant();
			string target = (spell.Target ?? "").ToLowerInvariant();
			bool healType = type == "HEAL" || type == "OMNIHEAL" || type == "SPREADHEAL" || type == "PBAEHEAL";

			if (!healType)
				return spell.IsHealing ? "écarté : soin non pris en charge (" + spell.SpellType + ")" : "écarté : pas un soin";
			if (spell.NeedInstrument)
				return "écarté : demande un instrument";
			if (spell.IsPulsing || spell.Frequency > 0)
				return "écarté : sort pulsé (chant)";
			if (spell.Duration > 0)
				return "écarté : soin sur la durée";

			isGroup = type == "SPREADHEAL" || type == "PBAEHEAL" || target == "group";
			bool isSingle = !isGroup && (target == "realm" || target == "self");
			if (!isGroup && !isSingle)
				return "écarté : cible \"" + spell.Target + "\" non prise en charge";

			isHeal = true;
			return isGroup ? "soin de groupe" : "soin simple";
		}

		/// <summary>
		/// true si l'alt connaît au moins un sort de soin utilisable.
		/// Vérifié une fois par niveau (il apprend des sorts en montant de niveau).
		/// </summary>
		public static bool HasHealSpells(FakeGamePlayer fake)
		{
			if (fake.HealSpellsCheckedLevel != fake.Level)
			{
				fake.KnowsHealSpells = KnownHeals(fake).Count > 0;
				fake.HealSpellsCheckedLevel = fake.Level;
			}
			return fake.KnowsHealSpells;
		}

		/// <summary>Les sorts de soin que l'alt peut utiliser (voir Classify).</summary>
		private static List<HealSpell> KnownHeals(FakeGamePlayer fake)
		{
			var result = new List<HealSpell>();
			foreach (FakeSpellCast.KnownSpell known in FakeSpellCast.KnownSpells(fake))
			{
				Classify(known.Spell, out bool isHeal, out bool isGroup);
				if (isHeal)
					result.Add(new HealSpell(known.Spell, known.Line, isGroup));
			}
			return result;
		}
	}
}
