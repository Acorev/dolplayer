/*
 * FakePlayers - Combat/FakeChants.cs
 *
 * Combat des alts, PALIER 3 : les CHANTS (sorts pulsés sur soi ou sur le groupe : Paladin, Skald,
 * Minstrel, Bard...). Appelé à chaque tic par Movement/FakeFollowAction, APRÈS les soins et AVANT les buffs.
 *
 * Réglage : la colonne BuffMode de la table FakePlayerClass
 *   0 = auto (tous les chants), 1 = jamais, 2 = seulement les chants sur soi.
 * Nombre de chants actifs en même temps : MaxPulsingSpells de la table CharacterClass (0 = 2).
 *
 * Règles de DOLSharp à respecter :
 *   - relancer un chant d'un type DÉJÀ actif l'ÉTEINT : on ne lance jamais un type déjà actif ;
 *   - au-delà de la limite, un nouveau chant remplace le plus ancien : on arrête nous-mêmes celui en trop ;
 *   - une chanson à instrument s'arrête si l'instrument quitte la main ;
 *   - un chant coûte du mana à chaque pulsation, et s'arrête faute de mana.
 *
 * Choix des chants, jusqu'à la limite (le premier de la liste d'abord) :
 *   en combat  : soin, endurance, armure / résistances, dégâts ajoutés, mana, autres (pas de vitesse) ;
 *   hors combat: vitesse (si l'alt suit), mana, endurance, soin, armure / résistances, dégâts, autres.
 * Un chant qui n'est plus voulu est arrêté, puis le suivant est lancé.
 * Un chant n'est LANCÉ qu'au-dessus de MANA_START_PERCENT de mana (un chant déjà actif est gardé).
 *
 * Chanteur (classe qui connaît des chansons à instrument, et a un instrument) : il garde l'instrument
 * en main et ne combat pas au corps à corps (voir Combat/FakeCombat).
 */

using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.Effects;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Les chants d'un alt (palier 3).
	/// </summary>
	public static class FakeChants
	{
		/// <summary>Un chant n'est lancé qu'au-dessus de ce pourcentage de mana.</summary>
		public const int MANA_START_PERCENT = 30;

		/// <summary>Un chant refusé par le serveur est mis de côté pendant ce délai (ms).</summary>
		private const int RETRY_MS = 30000;

		/// <summary>Genre de chant, pour l'ordre de priorité.</summary>
		private enum Kind { Heal, Endurance, Defense, Damage, Power, Speed, Other }

		/// <summary>Ordre de priorité en combat (la vitesse n'y est pas : jamais en combat).</summary>
		private static readonly Kind[] IN_COMBAT = { Kind.Heal, Kind.Endurance, Kind.Defense, Kind.Damage, Kind.Power, Kind.Other };

		/// <summary>Ordre de priorité hors combat.</summary>
		private static readonly Kind[] OUT_OF_COMBAT = { Kind.Speed, Kind.Power, Kind.Endurance, Kind.Heal, Kind.Defense, Kind.Damage, Kind.Other };

		/// <summary>Un chant retenu.</summary>
		private record ChantSpell(Spell Spell, SpellLine Line, Kind Kind, bool IsSelf);

		/// <summary>Chants refusés récemment : (alt, sort) → moment où l'on pourra réessayer.</summary>
		private static readonly Dictionary<(FakeGamePlayer, int), long> _retryAt = new();

		// ================================================================= chants

		/// <summary>
		/// Garde les bons chants actifs : arrête ceux en trop, lance celui qui manque.
		/// </summary>
		/// <param name="moved">true si la position de l'alt a changé (à envoyer aux joueurs proches).</param>
		/// <returns>true si l'alt lance un chant ce tic (les buffs, le combat et le suivi attendent).</returns>
		internal static bool Update(FakeGamePlayer fake, GamePlayer owner, FakeFollowAction.FollowState state, out bool moved)
		{
			moved = false;
			if (fake.BuffMode == FakePlayerClass.BUFF_NEVER || !fake.IsAlive)
				return false;

			List<ChantSpell> chants = KnownChants(fake);
			if (chants.Count == 0)
				return false;

			bool inCombat = fake.InCombat || owner.InCombat || fake.AttackState;
			Kind[] order = inCombat ? IN_COMBAT : OUT_OF_COMBAT;
			int limit = Math.Max(1, (int)fake.CharacterClass.MaxPulsingSpells);

			List<PulsingSpellEffect> active = ActiveChants(fake);
			var activeTypes = new HashSet<string>(active.Select(a => a.SpellHandler.Spell.SpellType));

			// Les candidats : déjà actifs, ou prêts à être lancés.
			bool canStart = fake.ManaPercent > MANA_START_PERCENT;
			List<ChantSpell> wanted = chants
				.Where(c => order.Contains(c.Kind))
				.Where(c => c.Kind != Kind.Speed || fake.IsFollowing)
				.Where(c => activeTypes.Contains(c.Spell.SpellType) || (canStart && IsAllowedNow(fake, c.Spell)))
				.OrderBy(c => Array.IndexOf(order, c.Kind))
				.ThenByDescending(c => activeTypes.Contains(c.Spell.SpellType)) // à égalité, on garde l'actif
				.Take(limit)
				.ToList();
			var wantedTypes = new HashSet<string>(wanted.Select(c => c.Spell.SpellType));

			// 1. Arrêter les chants qui ne sont plus voulus (sans relancer : relancer les éteindrait).
			foreach (PulsingSpellEffect effect in active.Where(a => !wantedTypes.Contains(a.SpellHandler.Spell.SpellType)))
				effect.Cancel(false);

			// 2. Lancer le premier chant voulu qui n'est pas actif.
			ChantSpell missing = wanted.FirstOrDefault(c => !activeTypes.Contains(c.Spell.SpellType));
			if (missing == null)
				return false;

			if (missing.Spell.NeedInstrument && !HoldInstrument(fake))
			{
				Trace(fake, missing, "pas d'instrument");
				return false;
			}

			FakeSpellCast.Result result = FakeSpellCast.TryCast(fake, state, missing.Spell, missing.Line, fake, "chant", out moved);
			if (result == FakeSpellCast.Result.Failed)
			{
				lock (_retryAt)
					_retryAt[(fake, missing.Spell.ID)] = Environment.TickCount64 + RETRY_MS;
				return false;
			}
			return true;
		}

		/// <summary>
		/// true si l'alt est un chanteur : il connaît des chansons à instrument et a un instrument.
		/// Il garde alors l'instrument en main et ne combat pas au corps à corps.
		/// </summary>
		public static bool IsSinger(FakeGamePlayer fake)
			=> fake.BuffMode != FakePlayerClass.BUFF_NEVER
			   && InstrumentSlot(fake) != null
			   && KnownChants(fake).Any(c => c.Spell.NeedInstrument);

		// ================================================================= classement (aussi pour /fake admin spells)

		/// <summary>
		/// Classe un sort pour les chants.
		/// </summary>
		/// <param name="isChant">true si l'alt peut s'en servir comme chant.</param>
		/// <returns>Le verdict en clair : "chant (genre)" ou la raison de l'écart.</returns>
		public static string Classify(Spell spell, out bool isChant)
		{
			isChant = false;
			if (!spell.IsPulsing)
				return "écarté : pas un chant";
			if (spell.IsFocus)
				return "écarté : sort focus (à maintenir)";

			string target = (spell.Target ?? "").ToLowerInvariant();
			if (target != "self" && target != "group")
				return "écarté : chant sur cible \"" + spell.Target + "\" non pris en charge";

			isChant = true;
			return "chant (" + KindName(KindOf(spell)) + (spell.NeedInstrument ? ", instrument" : "") + ")";
		}

		/// <summary>true si l'alt a un chant de ce type actif en ce moment.</summary>
		public static bool IsActive(FakeGamePlayer fake, Spell spell)
			=> ActiveChants(fake).Any(a => a.SpellHandler.Spell.SpellType == spell.SpellType);

		// ================================================================= outils internes

		/// <summary>Le genre d'un chant, d'après son type de sort.</summary>
		private static Kind KindOf(Spell spell)
		{
			string type = (spell.SpellType ?? "").ToUpperInvariant();
			if (spell.IsHealing || type == "COMBATHEAL" || type == "HEAL" || type == "HEALTHREGENBUFF")
				return Kind.Heal;
			if (type == "ENDURANCEREGENBUFF" || type == "ENDURANCEHEAL")
				return Kind.Endurance;
			if (type == "POWERREGENBUFF")
				return Kind.Power;
			if (type == "SPEEDENHANCEMENT" || type == "SPEEDOFTHEREALM")
				return Kind.Speed;
			if (type == "DAMAGEADD" || type == "DAMAGESHIELD")
				return Kind.Damage;
			if (type.Contains("ARMOR") || type.Contains("RESIST") || type.Contains("ABSORB"))
				return Kind.Defense;
			return Kind.Other;
		}

		private static string KindName(Kind kind) => kind switch
		{
			Kind.Heal => "soin",
			Kind.Endurance => "endurance",
			Kind.Defense => "armure / résistances",
			Kind.Damage => "dégâts ajoutés",
			Kind.Power => "mana",
			Kind.Speed => "vitesse",
			_ => "autre",
		};

		/// <summary>
		/// Les chants que l'alt peut utiliser : pour chaque type, le meilleur (plus grande valeur, puis plus
		/// haut niveau). En BuffMode "lui-même", seulement les chants sur soi. Sans instrument, pas de chanson.
		/// </summary>
		private static List<ChantSpell> KnownChants(FakeGamePlayer fake)
		{
			bool hasInstrument = InstrumentSlot(fake) != null;
			var all = new List<ChantSpell>();
			foreach (FakeSpellCast.KnownSpell known in FakeSpellCast.KnownSpells(fake))
			{
				Classify(known.Spell, out bool isChant);
				if (!isChant || (known.Spell.NeedInstrument && !hasInstrument))
					continue;
				bool isSelf = known.Spell.Target.ToLowerInvariant() == "self";
				if (fake.BuffMode == FakePlayerClass.BUFF_SELF && !isSelf)
					continue;
				all.Add(new ChantSpell(known.Spell, known.Line, KindOf(known.Spell), isSelf));
			}

			return all.GroupBy(c => c.Spell.SpellType)
			          .Select(g => g.OrderByDescending(c => c.Spell.Value).ThenByDescending(c => c.Spell.Level).First())
			          .ToList();
		}

		/// <summary>Les chants en cours de l'alt.</summary>
		private static List<PulsingSpellEffect> ActiveChants(FakeGamePlayer fake)
			=> fake.ConcentrationEffects.GetAllOfType(typeof(PulsingSpellEffect)).OfType<PulsingSpellEffect>().ToList();

		/// <summary>L'emplacement de l'instrument de l'alt (arme à distance, deux mains ou main droite), ou null.</summary>
		private static GameLiving.eActiveWeaponSlot? InstrumentSlot(FakeGamePlayer fake)
		{
			if (IsInstrument(fake.Inventory.GetItem(eInventorySlot.DistanceWeapon)))
				return GameLiving.eActiveWeaponSlot.Distance;
			if (IsInstrument(fake.Inventory.GetItem(eInventorySlot.TwoHandWeapon)))
				return GameLiving.eActiveWeaponSlot.TwoHanded;
			if (IsInstrument(fake.Inventory.GetItem(eInventorySlot.RightHandWeapon)))
				return GameLiving.eActiveWeaponSlot.Standard;
			return null;
		}

		private static bool IsInstrument(InventoryItem item)
			=> item != null && item.Object_Type == (int)eObjectType.Instrument;

		/// <summary>Met l'instrument en main si besoin. false si l'alt n'en a pas.</summary>
		private static bool HoldInstrument(FakeGamePlayer fake)
		{
			if (IsInstrument(fake.AttackWeapon))
				return true;
			GameLiving.eActiveWeaponSlot? slot = InstrumentSlot(fake);
			if (slot == null)
				return false;
			if (fake.AttackState)
				fake.StopAttack();
			fake.SwitchWeapon(slot.Value);
			return IsInstrument(fake.AttackWeapon);
		}

		/// <summary>true si le chant n'a pas été mis de côté après un refus, et s'il est prêt.</summary>
		private static bool IsAllowedNow(FakeGamePlayer fake, Spell spell)
		{
			lock (_retryAt)
			{
				if (_retryAt.TryGetValue((fake, spell.ID), out long at))
				{
					if (Environment.TickCount64 < at)
						return false;
					_retryAt.Remove((fake, spell.ID));
				}

				// Ménage : on oublie les alts qui ne sont plus en jeu.
				if (_retryAt.Count > 256)
					foreach (var key in _retryAt.Keys.Where(k => k.Item1.ObjectState == GameObject.eObjectState.Deleted).ToList())
						_retryAt.Remove(key);
			}
			return FakeSpellCast.IsReady(fake, spell);
		}

		private static void Trace(FakeGamePlayer fake, ChantSpell chant, string reason)
			=> FakeSpellCast.Trace(fake, "chant : " + chant.Spell.Name + " impossible (" + reason + ")");
	}
}
