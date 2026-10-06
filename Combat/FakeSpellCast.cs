/*
 * FakePlayers - Combat/FakeSpellCast.cs
 *
 * Outils communs aux sorts des alts (soins, buffs, et plus tard dégâts) :
 *   - la liste des sorts appris par l'alt (relue quand son niveau change) ;
 *   - "le sort est-il prêt ?" (recharge, mana) ;
 *   - s'approcher d'une cible (portée, ligne de vue) puis lancer le sort ;
 *   - les messages de diagnostic dans la console ("[FakePlayers] ...").
 *
 * Le sort lui-même (portée, mana, concentration, recharge, temps d'incantation) est géré par le serveur
 * comme pour un vrai joueur, avec CastSpell.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.GS.Geometry;
using log4net;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Outils communs pour faire lancer des sorts aux alts.
	/// </summary>
	public static class FakeSpellCast
	{
		/// <summary>Journal du serveur (console + fichier de log).</summary>
		private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		/// <summary>L'alt s'approche à cette marge sous la portée du sort, pour être sûr d'être à portée.</summary>
		private const int RANGE_MARGIN = 100;

		/// <summary>Un même message de diagnostic n'est écrit qu'une fois par alt pendant ce délai (ms).</summary>
		private const int LOG_INTERVAL_MS = 10000;

		/// <summary>Un sort connu de l'alt, avec sa ligne (nécessaire pour CastSpell).</summary>
		public record KnownSpell(Spell Spell, SpellLine Line);

		/// <summary>Dernier message de diagnostic écrit, par alt (pour ne pas inonder la console).</summary>
		private static readonly Dictionary<FakeGamePlayer, (string Message, long At)> _lastLog = new();

		/// <summary>Résultat d'une tentative de lancement.</summary>
		public enum Result
		{
			/// <summary>Le sort est parti (ou l'incantation a commencé).</summary>
			Cast,
			/// <summary>L'alt se déplace vers la cible (portée ou ligne de vue) : il réessaiera au prochain tic.</summary>
			Approaching,
			/// <summary>Impossible pour l'instant (hors de portée en stay, refus du serveur...).</summary>
			Failed,
		}

		// ================================================================= sorts appris

		/// <summary>
		/// Tous les sorts que l'alt a appris : listes de sorts (lanceurs purs) et sorts d'hybride.
		/// La liste est relue quand le niveau de l'alt a changé (sinon DOLSharp renvoie sa copie en cache).
		/// </summary>
		public static List<KnownSpell> KnownSpells(FakeGamePlayer fake)
		{
			bool refresh = fake.SpellsCheckedLevel != fake.Level;
			fake.SpellsCheckedLevel = fake.Level;

			var result = new List<KnownSpell>();

			// Lanceurs purs : leurs listes de sorts.
			foreach (Tuple<SpellLine, List<Skill>> entry in fake.GetAllUsableListSpells(refresh))
				foreach (Spell spell in entry.Item2.OfType<Spell>())
					result.Add(new KnownSpell(spell, entry.Item1));

			// Hybrides : sorts rangés avec leur ligne dans la liste des compétences.
			foreach (Tuple<Skill, Skill> entry in fake.GetAllUsableSkills(refresh))
				if (entry.Item1 is Spell spell && entry.Item2 is SpellLine line)
					result.Add(new KnownSpell(spell, line));

			// Un même sort peut apparaître deux fois : on ne le garde qu'une fois.
			return result.GroupBy(k => k.Spell.ID).Select(g => g.First()).ToList();
		}

		/// <summary>true si le sort est prêt : pas en recharge et assez de mana.</summary>
		public static bool IsReady(FakeGamePlayer fake, Spell spell)
		{
			if (fake.GetSkillDisabledDuration(spell) > 0)
				return false;

			// Power < 0 : pourcentage du mana maximum (règle de DOLSharp).
			int cost = spell.Power >= 0 ? spell.Power : fake.MaxMana * -spell.Power / 100;
			return fake.Mana >= cost;
		}

		// ================================================================= lancement

		/// <summary>
		/// Amène l'alt à portée et en vue de la cible, puis lance le sort.
		/// En stay, l'alt ne se déplace pas : si la cible est hors de portée, c'est un échec.
		/// </summary>
		/// <param name="what">Description pour les messages de diagnostic ("soin", "buff"...).</param>
		/// <param name="moved">true si la position de l'alt a changé (à envoyer aux joueurs proches).</param>
		internal static Result TryCast(FakeGamePlayer fake, FakeFollowAction.FollowState state,
			Spell spell, SpellLine line, GameLiving target, string what, out bool moved)
		{
			moved = false;

			// Trop loin : on s'approche (sauf en stay).
			if (target != fake && !fake.IsWithinRadius(target, Math.Max(0, spell.Range - RANGE_MARGIN)))
			{
				if (!fake.IsFollowing)
				{
					Trace(fake, what + " : " + target.Name + " hors de portée et l'alt est en stay");
					return Result.Failed;
				}
				moved = Approach(fake, state, target);
				return Result.Approaching;
			}

			// Ligne de vue (vérifiée par le jeu du propriétaire, voir Combat/FakeLineOfSight).
			switch (FakeLineOfSight.Check(fake, target))
			{
				case FakeLineOfSight.Sight.Unknown:
					// Réponse pas encore arrivée : on attend sur place (sans avancer pour rien).
					moved = FakeFollowAction.Stop(fake, state);
					return Result.Approaching;

				case FakeLineOfSight.Sight.NotVisible:
					// Cible cachée (mur, relief) : on s'approche, ou on attend en stay.
					Trace(fake, what + " : pas de ligne de vue sur " + target.Name);
					moved = fake.IsFollowing ? Approach(fake, state, target) : FakeFollowAction.Stop(fake, state);
					return Result.Approaching;
			}

			// Bouger interromprait l'incantation.
			moved = FakeFollowAction.Stop(fake, state);

			// Face à la cible : le serveur refuse un sort lancé dans le dos.
			if (target != fake)
			{
				Angle facing = fake.Position.Coordinate.GetOrientationTo(target.Position.Coordinate);
				if (fake.Position.Orientation != facing)
				{
					fake.Position = fake.Position.With(orientation: facing);
					moved = true;
				}
			}

			// Le serveur lance le sort sur la cible du joueur ; la ligne de vue a été vérifiée avant.
			fake.TargetObject = target;
			fake.TargetInView = true;

			if (fake.CastSpell(spell, line))
				return Result.Cast;

			Trace(fake, what + " : le serveur a refusé " + spell.Name + " sur " + target.Name
			            + " (mana " + fake.Mana + "/" + fake.MaxMana
			            + ", concentration " + fake.Concentration + "/" + fake.MaxConcentration
			            + ", distance " + fake.GetDistanceTo(target) + ")");
			return Result.Failed;
		}

		/// <summary>Fait marcher l'alt vers la cible.</summary>
		private static bool Approach(FakeGamePlayer fake, FakeFollowAction.FollowState state, GameLiving target)
		{
			short speed = fake.MaxSpeed > 0 ? fake.MaxSpeed : FakeFollowAction.DEFAULT_SPEED;
			return FakeFollowAction.Approach(fake, state, target.Position.Coordinate, speed);
		}

		// ================================================================= groupe

		/// <summary>
		/// Les membres à surveiller : le groupe du propriétaire (lui compris), vivants, dans la même région
		/// et à portée de laisse. Sans groupe : le propriétaire et l'alt.
		/// </summary>
		public static List<GameLiving> Members(FakeGamePlayer fake, GamePlayer owner)
		{
			IEnumerable<GameLiving> members = owner.Group != null
				? owner.Group.GetMembersInTheGroup()
				: new GameLiving[] { owner, fake };

			return members.Where(m => m != null && m.IsAlive
			                          && m.ObjectState == GameObject.eObjectState.Active
			                          && m.CurrentRegionID == fake.CurrentRegionID
			                          && fake.IsWithinRadius(m, FakeCombat.LEASH_DISTANCE))
			              .ToList();
		}

		// ================================================================= diagnostic

		/// <summary>
		/// Écrit un message de diagnostic dans la console du serveur, au plus une fois toutes les
		/// LOG_INTERVAL_MS par alt pour un même message.
		/// </summary>
		public static void Trace(FakeGamePlayer fake, string message)
		{
			long now = Environment.TickCount64;
			lock (_lastLog)
			{
				if (_lastLog.TryGetValue(fake, out var last) && last.Message == message && now - last.At < LOG_INTERVAL_MS)
					return;
				_lastLog[fake] = (message, now);

				// Ménage : on oublie les alts qui ne sont plus en jeu.
				if (_lastLog.Count > 32)
					foreach (FakeGamePlayer gone in _lastLog.Keys.Where(f => f.ObjectState == GameObject.eObjectState.Deleted).ToList())
						_lastLog.Remove(gone);
			}
			log.Info("[FakePlayers] " + fake.Name + " : " + message);
		}
	}
}
