/*
 * FakePlayers - Combat/FakeRez.cs
 *
 * Combat des alts, PALIER 3 : la RÉSURRECTION du propriétaire.
 * Appelé à chaque tic par Movement/FakeFollowAction, EN PREMIER (avant les soins).
 *
 * Fonctionnement :
 *   - seulement le PROPRIÉTAIRE (pas de rez entre alts) ;
 *   - seulement quand le combat est FINI : aucun membre vivant du groupe en combat
 *     (InCombat de DOLSharp, qui dure quelques secondes après le dernier coup) ;
 *   - UN SEUL alt s'en charge : parmi ceux qui connaissent une résurrection, un soigneur d'abord,
 *     puis le plus proche du corps ;
 *   - il s'approche (portée, ligne de vue), s'arrête et lance le sort (voir Combat/FakeSpellCast) ;
 *     le propriétaire reçoit la fenêtre habituelle et accepte ou refuse ;
 *   - tant que le propriétaire n'a pas répondu, l'alt attend (le serveur refuserait un deuxième rez) ;
 *   - pas assez de mana : il attend (message dans la console) ;
 *   - le propriétaire relâche (retour au bind) : il est de nouveau vivant, plus rien à faire.
 */

using System.Collections.Generic;
using System.Linq;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// La résurrection du propriétaire par ses alts (palier 3).
	/// </summary>
	public static class FakeRez
	{
		/// <summary>Propriété posée par DOLSharp sur un mort qui a une fenêtre de résurrection ouverte.</summary>
		private const string RESURRECT_CASTER_PROPERTY = "RESURRECT_CASTER";

		/// <summary>Sorts de résurrection connus, par alt, relus quand son niveau change.</summary>
		private static readonly Dictionary<FakeGamePlayer, (int Level, List<FakeSpellCast.KnownSpell> Spells)> _cache = new();

		// ================================================================= résurrection

		/// <summary>
		/// Ressuscite le propriétaire si besoin.
		/// </summary>
		/// <param name="moved">true si la position de l'alt a changé (à envoyer aux joueurs proches).</param>
		/// <returns>true si l'alt s'occupe de la résurrection ce tic (le reste attend).</returns>
		internal static bool Update(FakeGamePlayer fake, GamePlayer owner, FakeFollowAction.FollowState state, out bool moved)
		{
			moved = false;
			if (owner.IsAlive || !fake.IsAlive || owner.ObjectState != GameObject.eObjectState.Active)
				return false;
			if (owner.CurrentRegionID != fake.CurrentRegionID)
				return false;

			// Combat fini ? (aucun membre vivant du groupe en combat)
			if (FakeSpellCast.Members(fake, owner).Any(m => m.InCombat))
				return false;

			// Un seul alt s'en charge.
			if (ChooseRezzer(owner) != fake)
				return false;

			// Fenêtre de résurrection déjà ouverte : on attend la réponse.
			if (owner.TempProperties.getProperty<object>(RESURRECT_CASTER_PROPERTY, null) != null)
			{
				moved = FakeFollowAction.Stop(fake, state);
				return true;
			}

			// Le meilleur sort prêt (le plus de vie rendue).
			FakeSpellCast.KnownSpell spell = RezSpells(fake)
				.Where(k => FakeSpellCast.IsReady(fake, k.Spell))
				.OrderByDescending(k => k.Spell.ResurrectHealth)
				.FirstOrDefault();
			if (spell == null)
			{
				FakeSpellCast.Trace(fake, "rez : aucune résurrection prête (recharge ou mana " + fake.Mana + "/" + fake.MaxMana + ")");
				return false;
			}

			if (fake.AttackState)
				fake.StopAttack();

			return FakeSpellCast.TryCast(fake, state, spell.Spell, spell.Line, owner, "rez", out moved)
			       != FakeSpellCast.Result.Failed;
		}

		// ================================================================= classement (aussi pour /fake spells)

		/// <summary>true si c'est un sort de résurrection (sur un corps).</summary>
		public static bool IsRez(Spell spell)
			=> (spell.SpellType ?? "").ToUpperInvariant() == "RESURRECT"
			   && (spell.Target ?? "").ToLowerInvariant() == "corpse";

		// ================================================================= outils internes

		/// <summary>
		/// L'alt qui ressuscite : vivant, dans la même région, connaissant une résurrection ;
		/// un soigneur d'abord, puis le plus proche du corps. null si aucun.
		/// </summary>
		private static FakeGamePlayer ChooseRezzer(GamePlayer owner)
			=> FakePlayerMgr.GetFakesOf(owner)
				.Where(f => f.IsAlive && f.ObjectState == GameObject.eObjectState.Active
				            && f.CurrentRegionID == owner.CurrentRegionID && RezSpells(f).Count > 0)
				.OrderBy(f => f.HealMode == FakePlayerClass.HEAL_HEALER ? 0 : 1)
				.ThenBy(f => f.GetDistanceTo(owner))
				.FirstOrDefault();

		/// <summary>Les sorts de résurrection de l'alt (relus quand son niveau change).</summary>
		private static List<FakeSpellCast.KnownSpell> RezSpells(FakeGamePlayer fake)
		{
			lock (_cache)
			{
				if (_cache.TryGetValue(fake, out var cached) && cached.Level == fake.Level)
					return cached.Spells;
			}

			List<FakeSpellCast.KnownSpell> spells = FakeSpellCast.KnownSpells(fake).Where(k => IsRez(k.Spell)).ToList();

			lock (_cache)
			{
				_cache[fake] = (fake.Level, spells);

				// Ménage : on oublie les alts qui ne sont plus en jeu.
				if (_cache.Count > 32)
					foreach (FakeGamePlayer gone in _cache.Keys.Where(f => f.ObjectState == GameObject.eObjectState.Deleted).ToList())
						_cache.Remove(gone);
			}
			return spells;
		}
	}
}
