/*
 * FakePlayers - Combat/FakeGuard.cs
 *
 * GUARD : un alt qui a la capacité Guard protège un membre du groupe (il peut bloquer les coups portés
 * à ce membre). Appelé à chaque tic par Movement/FakeFollowAction.
 *
 * Règles du jeu (DOLSharp) : le tank doit avoir la capacité Guard, un BOUCLIER en main gauche
 * (pas d'arme à deux mains), être à moins de 256 du protégé, et dans le même groupe.
 *
 * Qui est protégé :
 *   - au choix du propriétaire, avec /fake guard <alt> (sa cible, ou lui-même) ;
 *   - sinon AUTOMATIQUEMENT : le premier soigneur du groupe (alt soigneur qui connaît des soins),
 *     sinon le propriétaire ; jamais quelqu'un qu'un autre tank protège déjà ;
 *   - /fake guard <alt> off : aucun Guard ; /fake guard <alt> auto : retour au choix automatique.
 * Rien n'est sauvegardé : au rappel de l'alt, on repart en automatique.
 *
 * En combat, le tank s'occupe EN PRIORITÉ des mobs qui frappent son protégé (voir Combat/FakeCombat) :
 * il va sur eux, se retrouve près du protégé, et le Guard fonctionne.
 */

using System.Collections.Generic;
using System.Linq;
using DOL.GS.Effects;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Le Guard des alts tanks.
	/// </summary>
	public static class FakeGuard
	{
		/// <summary>true si l'alt peut protéger : capacité Guard et bouclier en main gauche.</summary>
		public static bool CanGuard(FakeGamePlayer fake, out string reason)
		{
			if (!fake.HasAbility(Abilities.Guard))
			{
				reason = fake.Name + " n'a pas la capacité Guard";
				return false;
			}
			if (fake.Inventory.GetItem(eInventorySlot.LeftHandWeapon)?.Object_Type != (int)eObjectType.Shield)
			{
				reason = fake.Name + " n'a pas de bouclier";
				return false;
			}
			reason = null;
			return true;
		}

		/// <summary>Le membre que l'alt protège en ce moment, ou null.</summary>
		public static GameLiving CurrentTarget(FakeGamePlayer fake)
			=> MyGuard(fake)?.GuardTarget;

		/// <summary>
		/// Pose, change ou retire le Guard de l'alt selon le choix du propriétaire (ou le choix automatique).
		/// </summary>
		internal static void Update(FakeGamePlayer fake, GamePlayer owner)
		{
			GuardEffect current = MyGuard(fake);
			GameLiving wanted = fake.IsAlive && CanGuard(fake, out _) ? WantedTarget(fake, owner) : null;

			if (current != null && current.GuardTarget == wanted)
				return;

			current?.Cancel(false);
			if (wanted != null && fake.Group != null && wanted.Group == fake.Group)
				new GuardEffect().Start(fake, wanted);
		}

		/// <summary>Retire le Guard de l'alt (s'il en a un).</summary>
		public static void Cancel(FakeGamePlayer fake)
			=> MyGuard(fake)?.Cancel(false);

		// ================================================================= outils internes

		/// <summary>Le Guard posé par cet alt, ou null.</summary>
		private static GuardEffect MyGuard(FakeGamePlayer fake)
			=> fake.EffectList.GetAllOfType<GuardEffect>().FirstOrDefault(g => g.GuardSource == fake);

		/// <summary>
		/// Qui l'alt doit protéger : le choix du propriétaire s'il est toujours valable,
		/// sinon le choix automatique (premier soigneur du groupe, sinon le propriétaire). null = personne.
		/// </summary>
		private static GameLiving WantedTarget(FakeGamePlayer fake, GamePlayer owner)
		{
			if (fake.GuardDisabled || fake.Group == null)
				return null;

			// Choix du propriétaire.
			if (fake.GuardChoice != null)
			{
				if (IsValid(fake, fake.GuardChoice))
					return fake.GuardChoice;
				fake.GuardChoice = null; // parti du groupe, déconnecté... : retour à l'automatique
			}

			// Choix automatique : les soigneurs du groupe d'abord, puis le propriétaire.
			IEnumerable<GameLiving> candidates = fake.Group.GetMembersInTheGroup()
				.OfType<FakeGamePlayer>()
				.Where(f => f != fake && f.HealMode == FakePlayerClass.HEAL_HEALER && FakeHeals.HasHealSpells(f))
				.Cast<GameLiving>()
				.Append(owner);

			return candidates.FirstOrDefault(c => IsValid(fake, c) && !GuardedByOther(fake, c));
		}

		/// <summary>Un protégé possible : en jeu, vivant, dans le groupe de l'alt, et pas l'alt lui-même.</summary>
		private static bool IsValid(FakeGamePlayer fake, GameLiving target)
			=> target != null && target != fake && target.IsAlive
			   && target.ObjectState == GameObject.eObjectState.Active
			   && target.Group != null && target.Group == fake.Group;

		/// <summary>true si un AUTRE que l'alt protège déjà ce membre.</summary>
		private static bool GuardedByOther(FakeGamePlayer fake, GameLiving target)
			=> target.EffectList.GetAllOfType<GuardEffect>().Any(g => g.GuardTarget == target && g.GuardSource != fake);
	}
}
