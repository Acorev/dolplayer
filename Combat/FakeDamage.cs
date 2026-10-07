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
 * ne les a plus), et SORTS DE ZONE. Écartés pour l'instant : contrôles (mez, stun, root), debuffs,
 * sorts pulsés, sorts focus.
 * Choix, dans l'ordre :
 *   0. une invocation de combat (élémentaires du Theurgist, champignons de l'Animist), dans la limite
 *      du serveur (voir Combat/FakePets) ;
 *   1. un sort de zone, si la zone est "sûre" (voir AoeIsSafe) : au moins AoeMinTargets mobs (table
 *      FakePlayerClass, défaut 3) qui se battent déjà contre le groupe dans le rayon, et AUCUN autre mob
 *      attaquable dans le rayon (sinon on ramènerait des adds) ;
 *        - zone ciblée : centrée sur la cible (l'alt s'approche à portée, comme pour un sort simple) ;
 *        - zone autour du lanceur (PBAoE) : centrée sur l'alt. Il ne va JAMAIS se placer au milieu des mobs :
 *          il ne la lance que si les mobs sont déjà venus sur lui ;
 *   2. un dégât sur la durée si la cible ne l'a pas ;
 *   3. le sort direct prêt qui fait le plus de dégâts.
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
		/// <summary>Genre de sort de dégâts.</summary>
		private enum Kind { Single, OverTime, Area, PointBlank, Summon }

		/// <summary>Un sort de dégâts retenu.</summary>
		private record DamageSpell(Spell Spell, SpellLine Line, Kind Kind);

		/// <summary>
		/// Marge ajoutée au rayon pour chercher des mobs extérieurs : le rayon réel peut être un peu plus grand
		/// (bonus), et un mob en bordure qui se déplace serait touché.
		/// </summary>
		private const int AOE_SAFETY_MARGIN = 50;

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

			// Zone autour du lanceur : lancée sur place (le serveur la centre sur l'alt).
			// Invocation sans portée : lancée sur place aussi.
			GameLiving castOn = spell.Kind == Kind.PointBlank || (spell.Kind == Kind.Summon && spell.Spell.Range <= 0)
				? fake : target;

			// Invocation au sol (champignons de l'Animist) : la cible au sol est posée sur la cible.
			if (spell.Kind == Kind.Summon)
				FakePets.PrepareGroundTarget(fake, spell.Spell, target);
			FakeSpellCast.TryCast(fake, state, spell.Spell, spell.Line, castOn,
				spell.Kind is Kind.Area or Kind.PointBlank ? "dégâts de zone" : "dégâts", out bool moved);
			return moved;
		}

		// ================================================================= classement (aussi pour /fake admin spells)

		/// <summary>
		/// Classe un sort pour les dégâts.
		/// </summary>
		/// <param name="isDamage">true si l'alt peut s'en servir comme sort de dégâts.</param>
		/// <returns>Le verdict en clair : "dégâts", "dégâts sur la durée", "dégâts de zone",
		/// "dégâts de zone autour du lanceur" ou la raison de l'écart.</returns>
		public static string Classify(Spell spell, out bool isDamage)
		{
			Kind? kind = KindOf(spell, out string verdict);
			isDamage = kind != null;
			return verdict;
		}

		// ================================================================= outils internes

		/// <summary>Le genre d'un sort de dégâts, ou null s'il est écarté (la raison est dans verdict).</summary>
		private static Kind? KindOf(Spell spell, out string verdict)
		{
			string type = (spell.SpellType ?? "").ToUpperInvariant();
			string target = (spell.Target ?? "").ToLowerInvariant();

			// Invocations de combat (Theurgist, Animist) : voir Combat/FakePets.
			if (FakePets.IsCombatSummon(spell))
			{
				verdict = "invocation de combat";
				return Kind.Summon;
			}

			bool direct = DIRECT_TYPES.Contains(type);
			bool overTime = OVER_TIME_TYPES.Contains(type);
			if (!direct && !overTime)
			{
				verdict = target == "enemy" ? "écarté : sort offensif non pris en charge (" + spell.SpellType + ")" : "écarté : pas un sort de dégâts";
				return null;
			}

			if (target != "enemy")
				verdict = "écarté : cible \"" + spell.Target + "\" non prise en charge";
			else if (spell.IsPulsing || (spell.Frequency > 0 && !overTime))
				verdict = "écarté : sort pulsé";
			else if (spell.IsFocus)
				verdict = "écarté : sort focus (à maintenir)";
			else if (spell.NeedInstrument)
				verdict = "écarté : demande un instrument";
			else if (spell.Radius > 0 && spell.Range <= 0)
			{
				verdict = "dégâts de zone autour du lanceur";
				return Kind.PointBlank;
			}
			else if (spell.Radius > 0)
			{
				verdict = "dégâts de zone";
				return Kind.Area;
			}
			else if (spell.Range <= 0)
				verdict = "écarté : sort sans portée";
			else
			{
				verdict = overTime ? "dégâts sur la durée" : "dégâts";
				return overTime ? Kind.OverTime : Kind.Single;
			}
			return null;
		}

		// ================================================================= outils internes

		/// <summary>
		/// Le sort à lancer : un sort de zone si la zone est sûre, sinon un dégât sur la durée si la cible
		/// ne l'a pas, sinon le sort direct prêt qui fait le plus de dégâts. null si rien n'est prêt.
		/// </summary>
		private static DamageSpell ChooseSpell(FakeGamePlayer fake, GameLiving target)
		{
			List<DamageSpell> ready = KnownDamage(fake).Where(d => FakeSpellCast.IsReady(fake, d.Spell)).ToList();

			// 0. Invocation de combat (Theurgist, Animist), tant que la limite du serveur n'est pas atteinte.
			DamageSpell summon = ready.Where(d => d.Kind == Kind.Summon && FakePets.CombatSummonAllowed(fake, d.Spell))
			                          .OrderByDescending(d => d.Spell.Level).FirstOrDefault();
			if (summon != null)
				return summon;

			// 1. Zone, la plus forte d'abord, si elle est sûre (sur la cible, ou autour de l'alt pour un PBAoE).
			foreach (DamageSpell area in ready.Where(d => d.Kind is Kind.Area or Kind.PointBlank)
			                                  .OrderByDescending(d => d.Spell.Damage))
			{
				GameObject center = area.Kind == Kind.PointBlank ? fake : target;
				if (AoeIsSafe(fake, center, area.Spell.Radius))
					return area;
			}

			// 2. Dégât sur la durée que la cible n'a pas encore.
			DamageSpell dot = ready.Where(d => d.Kind == Kind.OverTime && SpellHandler.FindEffectOnTarget(target, d.Spell.SpellType) == null)
			                       .OrderByDescending(d => d.Spell.Damage).FirstOrDefault();
			if (dot != null)
				return dot;

			// 3. Le sort direct le plus fort.
			return ready.Where(d => d.Kind == Kind.Single)
			            .OrderByDescending(d => d.Spell.Damage).ThenByDescending(d => d.Spell.Level)
			            .FirstOrDefault();
		}

		/// <summary>
		/// true si une zone de ce rayon autour de "center" est sûre :
		///  - au moins AoeMinTargets mobs qui se battent déjà contre le groupe dans le rayon ;
		///  - AUCUN autre mob attaquable dans le rayon (+ marge) : il deviendrait un add.
		/// </summary>
		private static bool AoeIsSafe(FakeGamePlayer fake, GameObject center, int radius)
		{
			GamePlayer owner = fake.Owner;
			if (owner == null || radius <= 0 || fake.AoeMinTargets <= 0)
				return false;

			int engaged = 0;
			ushort searchRadius = (ushort)System.Math.Min(ushort.MaxValue, radius + AOE_SAFETY_MARGIN);
			foreach (GameNPC npc in center.GetNPCsInRadius(searchRadius).OfType<GameNPC>())
			{
				if (!npc.IsAlive || npc.ObjectState != GameObject.eObjectState.Active)
					continue;
				// Ce que le sort ne peut pas toucher (PNJ pacifiques, gardes de son royaume...) ne compte pas.
				if (!GameServer.ServerRules.IsAllowedToAttack(fake, npc, true))
					continue;

				if (!FakeCombat.IsEngagedWithGroup(owner, npc))
					return false; // un mob extérieur serait touché : add

				if (center.IsWithinRadius(npc, radius))
					engaged++;
			}
			return engaged >= fake.AoeMinTargets;
		}

		/// <summary>Les sorts de dégâts que l'alt peut utiliser (voir KindOf).</summary>
		private static List<DamageSpell> KnownDamage(FakeGamePlayer fake)
		{
			var result = new List<DamageSpell>();
			foreach (FakeSpellCast.KnownSpell known in FakeSpellCast.KnownSpells(fake))
			{
				Kind? kind = KindOf(known.Spell, out _);
				if (kind != null)
					result.Add(new DamageSpell(known.Spell, known.Line, kind.Value));
			}
			return result;
		}
	}
}
