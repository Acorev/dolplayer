/*
 * FakePlayers - Combat/FakeCombat.cs
 *
 * Combat des alts, PALIER 1 : corps à corps uniquement (armes et attaque automatique).
 * Appelé à chaque tic par Movement/FakeFollowAction, AVANT le suivi : un alt ne suit son
 * propriétaire que s'il n'a rien à combattre.
 *
 * Choix de la cible, par priorité :
 *   0. l'ORDRE du propriétaire (/fake attack) : passe avant tout, même si l'alt combat déjà ;
 *   1. ce qui attaque l'alt lui-même (il se défend) ;
 *   2. ce qui attaque le propriétaire (il le défend) ;
 *   3. sa cible actuelle, tant qu'elle s'en prend au groupe.
 * (Il n'y a PAS d'assistance automatique sur la cible du propriétaire : sinon les alts attaquaient
 *  tout ce qu'il cliquait. Pour les envoyer sur sa cible, il utilise /fake attack.)
 * Règles :
 *   - /fake passive : aucun combat (et annule l'ordre en cours) ;
 *   - mode "stay" : se défend seulement (1), sans se déplacer ; mais obéit à /fake attack ;
 *   - mode de classe 2 (sorts, table FakePlayerClass) : se défend seulement (1), en attendant
 *     le palier 3 ; n'obéit pas encore à /fake attack ;
 *   - laisse : au-delà de LEASH_DISTANCE du propriétaire, l'alt abandonne (ordre compris) et revient.
 * L'ordre prend fin quand la cible meurt ou disparaît, à la laisse, avec un nouvel ordre ou /fake passive.
 *
 * Le combat lui-même (coups, dégâts, vitesse d'arme) est fait par le serveur, comme pour un vrai
 * joueur, une fois l'attaque lancée avec StartAttack. Ici, on choisit la cible, on amène l'alt
 * au contact et on le tourne vers elle.
 */

using System.Collections.Generic;
using System.Linq;
using DOL.GS.Geometry;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Décisions de combat d'un alt (palier 1 : corps à corps).
	/// </summary>
	public static class FakeCombat
	{
		/// <summary>Au-delà de cette distance du propriétaire, l'alt abandonne le combat et revient.</summary>
		public const int LEASH_DISTANCE = 1500;

		/// <summary>L'alt s'arrête un peu avant la limite de portée de son arme, pour être sûr de toucher.</summary>
		private const int RANGE_MARGIN = 30;

		/// <summary>Distance d'arrêt minimale (si la portée de l'arme est très faible).</summary>
		private const int MIN_STOP_DISTANCE = 50;

		// ================================================================= choix de la cible

		/// <summary>
		/// Choisit la cible de l'alt pour ce tic, ou null s'il n'a rien à combattre.
		/// </summary>
		public static GameLiving ChooseTarget(FakeGamePlayer fake, GamePlayer owner)
		{
			if (fake.IsPassive || !fake.IsAlive)
				return null;

			// Laisse : trop loin du propriétaire, on ne combat plus (le suivi le ramène), ordre compris.
			if (fake.Position.Coordinate.DistanceTo(owner.Position.Coordinate) > LEASH_DISTANCE)
			{
				fake.OrderedTarget = null;
				return null;
			}

			// 0. L'ordre du propriétaire (/fake attack), tant que la cible est valide.
			if (fake.OrderedTarget != null)
			{
				if (IsValidTarget(fake, owner, fake.OrderedTarget))
					return fake.OrderedTarget;
				fake.OrderedTarget = null; // cible morte, disparue ou trop loin : l'ordre est terminé
			}

			// 1. Ce qui l'attaque lui-même : toujours (même en stay ou en mode sorts).
			GameLiving target = FirstValid(fake, owner, fake.Attackers);
			if (target != null)
				return target;

			// En stay ou en mode sorts : uniquement la défense personnelle.
			if (!fake.IsFollowing || fake.CombatMode != FakePlayerClass.MODE_MELEE)
				return null;

			// 2. Ce qui attaque le propriétaire.
			target = FirstValid(fake, owner, owner.Attackers);
			if (target != null)
				return target;

			// 3. Sa cible actuelle, tant qu'elle s'en prend au groupe.
			if (fake.AttackState && fake.TargetObject is GameLiving current && IsValidTarget(fake, owner, current)
				&& current.TargetObject is GameLiving victim && IsGroupMember(owner, victim))
				return current;

			return null;
		}

		// ================================================================= combat

		/// <summary>
		/// Fait combattre l'alt contre sa cible : approche (sauf en stay, hors ordre), se tourne vers elle, attaque.
		/// </summary>
		/// <returns>true si la position a changé (il faut l'envoyer aux joueurs proches).</returns>
		internal static bool Fight(FakeGamePlayer fake, GameLiving target, FakeFollowAction.FollowState state)
		{
			Coordinate here = fake.Position.Coordinate;
			Coordinate there = target.Position.Coordinate;
			int stopAt = System.Math.Max(MIN_STOP_DISTANCE, fake.AttackRange - RANGE_MARGIN);

			// Le serveur se sert de la cible du joueur pour ses coups.
			if (fake.TargetObject != target)
				fake.TargetObject = target;

			// Trop loin : on s'approche. En stay, on attend que la cible vienne, sauf sur ordre (/fake attack).
			if (here.DistanceTo(there) > stopAt)
			{
				if (!fake.IsFollowing && target != fake.OrderedTarget)
					return FakeFollowAction.Stop(fake, state);
				short speed = fake.MaxSpeed > 0 ? fake.MaxSpeed : FakeFollowAction.DEFAULT_SPEED;
				return FakeFollowAction.Approach(fake, state, there, speed);
			}

			// Au contact : arrêt, tourné vers la cible (le serveur refuse les coups portés dans le dos).
			bool moved = FakeFollowAction.Stop(fake, state);
			Angle facing = here.GetOrientationTo(there);
			if (fake.Position.Orientation != facing)
			{
				fake.Position = fake.Position.With(orientation: facing);
				moved = true;
			}

			// Pour un vrai joueur, c'est son jeu qui dit si la cible est visible.
			// Au contact et de face, elle l'est.
			fake.TargetInView = true;

			EnsureMeleeWeapon(fake);
			if (!fake.AttackState)
				fake.StartAttack(target);

			return moved;
		}

		/// <summary>Fin du combat : l'alt arrête d'attaquer et oublie sa cible (et l'ordre en cours).</summary>
		public static void EndFight(FakeGamePlayer fake)
		{
			fake.OrderedTarget = null;
			if (fake.AttackState)
				fake.StopAttack();
			if (fake.TargetObject != null)
				fake.TargetObject = null;
		}

		// ================================================================= outils internes

		/// <summary>Le premier attaquant valide de la liste, ou null.</summary>
		private static GameLiving FirstValid(FakeGamePlayer fake, GamePlayer owner, List<GameObject> attackers)
		{
			List<GameObject> copy;
			lock (attackers)
				copy = attackers.ToList();

			return copy.OfType<GameLiving>().FirstOrDefault(l => IsValidTarget(fake, owner, l));
		}

		/// <summary>
		/// Une cible valide : vivante, en jeu, dans la même région, pas un membre du groupe,
		/// attaquable selon les règles du serveur, et pas trop loin du propriétaire (laisse).
		/// </summary>
		public static bool IsValidTarget(FakeGamePlayer fake, GamePlayer owner, GameLiving living)
		{
			if (living == null || !living.IsAlive || living.ObjectState != GameObject.eObjectState.Active)
				return false;
			if (living.CurrentRegionID != fake.CurrentRegionID)
				return false;
			if (IsGroupMember(owner, living))
				return false;
			if (living.Position.Coordinate.DistanceTo(owner.Position.Coordinate) > LEASH_DISTANCE)
				return false;
			return GameServer.ServerRules.IsAllowedToAttack(fake, living, true);
		}

		/// <summary>true si c'est le propriétaire, un de ses alts ou un membre de son groupe.</summary>
		private static bool IsGroupMember(GamePlayer owner, GameLiving living)
		{
			if (living == owner)
				return true;
			if (living is FakeGamePlayer f && f.Owner == owner)
				return true;
			return owner.Group != null && living.Group == owner.Group;
		}

		/// <summary>
		/// Met une arme de mêlée en main si l'alt a son arc (ou une arme de jet) en main :
		/// l'arme à une main s'il en a une, sinon l'arme à deux mains.
		/// Les autres cas (une main, deux mains) sont laissés tels que le personnage les avait.
		/// </summary>
		private static void EnsureMeleeWeapon(FakeGamePlayer fake)
		{
			if (fake.ActiveWeaponSlot != GameLiving.eActiveWeaponSlot.Distance)
				return;

			bool hasOneHand = fake.Inventory.GetItem(eInventorySlot.RightHandWeapon) != null;
			bool hasTwoHand = fake.Inventory.GetItem(eInventorySlot.TwoHandWeapon) != null;
			if (hasOneHand)
				fake.SwitchWeapon(GameLiving.eActiveWeaponSlot.Standard);
			else if (hasTwoHand)
				fake.SwitchWeapon(GameLiving.eActiveWeaponSlot.TwoHanded);
		}
	}
}
