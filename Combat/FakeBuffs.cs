/*
 * FakePlayers - Combat/FakeBuffs.cs
 *
 * Combat des alts, PALIER 3 : les BUFFS.
 * Appelé à chaque tic par Movement/FakeFollowAction, APRÈS les soins et AVANT le combat,
 * et seulement HORS COMBAT (ni l'alt ni son propriétaire en combat).
 *
 * Réglage par classe dans la table FakePlayerClass : BuffMode
 *   0 = auto (buffe le groupe), 1 = jamais, 2 = seulement lui-même.
 *
 * Fonctionnement :
 *   - sorts retenus : les buffs appris par l'alt (pas les chants, ni les sorts à instrument) ;
 *     pour chaque type de buff (armure, force...) et chaque genre de cible, seul le MEILLEUR est gardé ;
 *   - buff sur une cible : chaque membre du groupe (propriétaire, alts, autres joueurs) ;
 *     buff de groupe : lancé une fois si un membre ne l'a pas ; buff sur soi : l'alt lui-même ;
 *   - une cible qui porte déjà un buff de ce TYPE (de n'importe quel lanceur) n'est pas rebuffée ;
 *   - renouvellement : un buff à durée qui expire disparaît, et l'alt le relance ;
 *   - un buff par incantation ; l'alt s'arrête, vérifie la portée et la ligne de vue (Combat/FakeSpellCast) ;
 *   - réserve : l'alt ne buffe que s'il a plus de MANA_RESERVE_PERCENT de mana (garder de quoi soigner) ;
 *   - un buff refusé par le serveur (concentration épuisée...) est mis de côté RETRY_MS avant de réessayer.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.Spells;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Les buffs d'un alt (palier 3).
	/// </summary>
	public static class FakeBuffs
	{
		/// <summary>En dessous de ce pourcentage de mana, l'alt ne buffe pas (il garde de quoi soigner).</summary>
		public const int MANA_RESERVE_PERCENT = 30;

		/// <summary>Un buff refusé par le serveur est mis de côté pendant ce délai (ms).</summary>
		private const int RETRY_MS = 30000;

		/// <summary>Un buff retenu.</summary>
		private record BuffSpell(Spell Spell, SpellLine Line, string TargetKind);

		/// <summary>Buffs refusés récemment : (alt, sort) → moment où l'on pourra réessayer.</summary>
		private static readonly Dictionary<(FakeGamePlayer, int), long> _retryAt = new();

		// ================================================================= buffs

		/// <summary>
		/// Buffe si besoin, hors combat.
		/// </summary>
		/// <param name="moved">true si la position de l'alt a changé (à envoyer aux joueurs proches).</param>
		/// <returns>true si l'alt s'occupe d'un buff ce tic (le combat et le suivi attendent).</returns>
		internal static bool Update(FakeGamePlayer fake, GamePlayer owner, FakeFollowAction.FollowState state, out bool moved)
		{
			moved = false;
			if (fake.BuffMode == FakePlayerClass.BUFF_NEVER || !fake.IsAlive)
				return false;

			// Hors combat seulement.
			if (fake.InCombat || owner.InCombat || fake.AttackState)
				return false;

			// Réserve de mana pour les soins.
			if (fake.ManaPercent <= MANA_RESERVE_PERCENT)
				return false;

			List<BuffSpell> buffs = KnownBuffs(fake).Where(b => IsAllowedNow(fake, b.Spell)).ToList();
			if (buffs.Count == 0)
				return false;

			List<GameLiving> members = fake.BuffMode == FakePlayerClass.BUFF_SELF
				? new List<GameLiving> { fake }
				: FakeSpellCast.Members(fake, owner);

			// Ordre : buffs de groupe, puis sur soi, puis sur chaque membre (le propriétaire d'abord).
			foreach (BuffSpell buff in buffs.OrderBy(b => b.TargetKind == "group" ? 0 : b.TargetKind == "self" ? 1 : 2))
			{
				GameLiving target = ChooseTarget(fake, owner, buff, members);
				if (target == null)
					continue;

				FakeSpellCast.Result result = FakeSpellCast.TryCast(fake, state, buff.Spell, buff.Line, target, "buff", out moved);
				if (result == FakeSpellCast.Result.Failed)
				{
					// Refusé (concentration épuisée, hors de portée en stay...) : on le met de côté.
					lock (_retryAt)
						_retryAt[(fake, buff.Spell.ID)] = Environment.TickCount64 + RETRY_MS;
					continue;
				}
				return true;
			}
			return false;
		}

		// ================================================================= classement (aussi pour /fake spells)

		/// <summary>
		/// Classe un sort pour les buffs.
		/// </summary>
		/// <param name="isBuff">true si l'alt peut s'en servir comme buff.</param>
		/// <returns>Le verdict en clair : "buff (cible)" ou la raison de l'écart.</returns>
		public static string Classify(Spell spell, out bool isBuff)
		{
			isBuff = false;
			if (!spell.IsBuff || spell.IsHealing)
				return "écarté : pas un buff";
			if (spell.NeedInstrument)
				return "écarté : demande un instrument";
			if (spell.IsPulsing || spell.Frequency > 0)
				return "écarté : sort pulsé (chant)";

			string target = (spell.Target ?? "").ToLowerInvariant();
			if (target != "self" && target != "realm" && target != "group")
				return "écarté : buff sur cible \"" + spell.Target + "\" non pris en charge";

			isBuff = true;
			return target switch
			{
				"self" => "buff sur soi",
				"group" => "buff de groupe",
				_ => "buff sur une cible",
			};
		}

		// ================================================================= outils internes

		/// <summary>
		/// Les buffs que l'alt peut utiliser : pour chaque type de buff et genre de cible, le meilleur
		/// (plus grande valeur, puis plus haut niveau).
		/// </summary>
		private static List<BuffSpell> KnownBuffs(FakeGamePlayer fake)
		{
			var all = new List<BuffSpell>();
			foreach (FakeSpellCast.KnownSpell known in FakeSpellCast.KnownSpells(fake))
			{
				Classify(known.Spell, out bool isBuff);
				if (isBuff)
					all.Add(new BuffSpell(known.Spell, known.Line, known.Spell.Target.ToLowerInvariant()));
			}

			return all.GroupBy(b => (b.Spell.SpellType, b.TargetKind))
			          .Select(g => g.OrderByDescending(b => b.Spell.Value).ThenByDescending(b => b.Spell.Level).First())
			          .ToList();
		}

		/// <summary>
		/// La cible qui a besoin de ce buff, ou null si personne :
		///  - sur soi : l'alt, s'il ne l'a pas ;
		///  - de groupe : l'alt (le sort touche tout le groupe), si un membre ne l'a pas ;
		///  - sur une cible : le premier membre qui ne l'a pas (le propriétaire d'abord).
		/// </summary>
		private static GameLiving ChooseTarget(FakeGamePlayer fake, GamePlayer owner, BuffSpell buff, List<GameLiving> members)
		{
			switch (buff.TargetKind)
			{
				case "self":
					return HasBuff(fake, buff.Spell) ? null : fake;

				case "group":
					if (fake.BuffMode == FakePlayerClass.BUFF_SELF)
						return HasBuff(fake, buff.Spell) ? null : fake;
					return members.Any(m => !HasBuff(m, buff.Spell)) ? fake : null;

				default: // "realm"
					return members.OrderBy(m => m == owner ? 0 : 1).FirstOrDefault(m => !HasBuff(m, buff.Spell));
			}
		}

		/// <summary>true si la cible porte déjà un buff de ce type (de n'importe quel lanceur).</summary>
		private static bool HasBuff(GameLiving target, Spell spell)
			=> SpellHandler.FindEffectOnTarget(target, spell.SpellType) != null;

		/// <summary>true si le sort est prêt et n'a pas été mis de côté après un refus.</summary>
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
	}
}
