/*
 * FakePlayers - Combat/FakeStyles.cs
 *
 * Combat des alts, PALIER 2 : les styles de combat.
 * Appelé par Combat/FakeCombat à chaque tic où l'alt est au contact de sa cible.
 *
 * L'alt choisit parmi les styles qu'il a VRAIMENT appris (GetStyleList, d'après ses specs),
 * et DOLSharp fait le reste comme pour un vrai joueur qui clique sur une icône :
 *   - StyleProcessor.CanUseStyle dit si un style est utilisable MAINTENANT
 *     (arme, position, enchaînement après le bon style, réaction après parade/blocage/esquive...) ;
 *   - StyleProcessor.TryToUseStyle le prépare pour le prochain coup (et vérifie l'endurance).
 *
 * Priorité, et dans chaque catégorie le style du plus haut niveau de spécialisation :
 *   1. enchaînement : suit le style qui vient de réussir (le plus de dégâts) ;
 *   2. réaction : après une parade, un blocage, une esquive... ;
 *   3. position : dans le dos / sur le côté, seulement si l'alt s'y trouve déjà (pas de placement exprès) ;
 *   4. ouverture sans condition, utilisable à tout moment.
 * Le style d'ouverture sert aussi de style de SECOURS : il part si le style principal ne peut pas partir.
 *
 * Lignes utilisées (automatique, sans réglage) :
 *   - la ligne de l'arme en main (épée → Slash, lance → Polearm, deux mains → sa ligne...) ;
 *   - les styles d'arme gauche (Dual Wield, Left Axe, Celtic Dual) si la main gauche tient une ARME ;
 *   - les styles "n'importe quelle arme" (ligne Critical Strike des classes furtives).
 *   Les styles de BOUCLIER ne sont pas utilisés (l'alt frappait au bouclier au lieu de son arme).
 *
 * Exclus aussi : les styles de furtivité (les alts ne se mettent pas en furtivité).
 * Endurance : en dessous de ENDURANCE_RESERVE_PERCENT, l'alt frappe sans style (réserve).
 * Provocation (taunt) : autorisée pour l'instant ; un réglage en base est prévu plus tard.
 */

using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.Styles;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Choix et préparation des styles de combat d'un alt (palier 2).
	/// </summary>
	public static class FakeStyles
	{
		/// <summary>En dessous de ce pourcentage d'endurance, l'alt garde sa réserve et frappe sans style.</summary>
		public const int ENDURANCE_RESERVE_PERCENT = 20;

		/// <summary>
		/// Prépare un style pour le prochain coup, si aucun n'est déjà prêt.
		/// À appeler quand l'alt est au contact, en mode attaque, face à sa cible.
		/// </summary>
		public static void TryUseStyle(FakeGamePlayer fake)
		{
			// Un style est déjà prêt : on attend qu'il parte.
			if (fake.NextCombatStyle != null)
				return;

			// Réserve d'endurance.
			if (fake.EndurancePercent < ENDURANCE_RESERVE_PERCENT)
				return;

			if (fake.TargetObject is not GameLiving || fake.AttackWeapon == null)
				return;

			// Styles appris, de la bonne ligne, sans ceux de furtivité, et utilisables maintenant.
			string weaponLine = SkillBase.ObjectTypeToSpec((eObjectType)fake.AttackWeapon.Object_Type);
			bool leftHandWeapon = HasLeftHandWeapon(fake);
			List<Style> usable = fake.GetStyleList().OfType<Style>()
				.Where(s => !s.StealthRequirement
				            && IsAllowedLine(s, weaponLine, leftHandWeapon)
				            && StyleProcessor.CanUseStyle(fake, s, WeaponFor(fake, s)))
				.ToList();
			if (usable.Count == 0)
				return;

			Style opener = Best(usable.Where(IsOpener));
			Style main = Best(usable.Where(IsChain))
				?? Best(usable.Where(IsReaction))
				?? Best(usable.Where(IsPositional))
				?? opener;
			if (main == null)
				return;

			// Style principal, puis style de secours (l'ouverture), comme un joueur qui clique deux icônes.
			StyleProcessor.TryToUseStyle(fake, main);
			if (opener != null && opener.ID != main.ID && fake.NextCombatStyle != null)
				StyleProcessor.TryToUseStyle(fake, opener);
		}

		// ================================================================= lignes

		/// <summary>
		/// true si le style est d'une ligne que l'alt doit utiliser avec l'arme qu'il a en main :
		/// la ligne de cette arme, l'arme gauche (si la main gauche tient une arme), ou "n'importe quelle arme".
		/// Jamais le bouclier.
		/// </summary>
		private static bool IsAllowedLine(Style s, string weaponLine, bool leftHandWeapon)
		{
			if (s.WeaponTypeRequirement == (int)eObjectType.Shield)
				return false;
			if (s.WeaponTypeRequirement == Style.SpecialWeaponType.AnyWeapon)
				return true;
			if (s.WeaponTypeRequirement == Style.SpecialWeaponType.DualWield)
				return leftHandWeapon;

			// Ligne de l'arme inconnue de DOLSharp : on accepte (sauf bouclier, exclu plus haut).
			if (string.IsNullOrEmpty(weaponLine))
				return true;
			return string.Equals(s.Spec, weaponLine, System.StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>true si la main gauche tient une arme (pas un bouclier).</summary>
		private static bool HasLeftHandWeapon(FakeGamePlayer fake)
		{
			InventoryItem left = fake.Inventory.GetItem(eInventorySlot.LeftHandWeapon);
			return left != null && left.Object_Type != (int)eObjectType.Shield;
		}

		// ================================================================= catégories

		/// <summary>Enchaînement : doit suivre un style précis qui vient de réussir.</summary>
		private static bool IsChain(Style s)
			=> s.OpeningRequirementType == Style.eOpening.Offensive && s.OpeningRequirementValue != 0;

		/// <summary>Réaction : après une action de la cible contre l'alt (parade, blocage, esquive...).</summary>
		private static bool IsReaction(Style s)
			=> s.OpeningRequirementType == Style.eOpening.Defensive
			   && s.AttackResultRequirement != Style.eAttackResultRequirement.Any;

		/// <summary>Position : dans le dos, sur le côté ou de face.</summary>
		private static bool IsPositional(Style s)
			=> s.OpeningRequirementType == Style.eOpening.Positional;

		/// <summary>Ouverture sans condition : utilisable à tout moment.</summary>
		private static bool IsOpener(Style s)
			=> (s.OpeningRequirementType == Style.eOpening.Offensive && s.OpeningRequirementValue == 0
			    && s.AttackResultRequirement == Style.eAttackResultRequirement.Any)
			   || (s.OpeningRequirementType == Style.eOpening.Defensive
			       && s.AttackResultRequirement == Style.eAttackResultRequirement.Any);

		/// <summary>Le style du plus haut niveau de spécialisation, ou null.</summary>
		private static Style Best(IEnumerable<Style> styles)
			=> styles.OrderByDescending(s => s.SpecLevelRequirement).ThenByDescending(s => s.ID).FirstOrDefault();

		/// <summary>
		/// L'arme qui servira pour ce style : le bouclier pour un style de bouclier, sinon l'arme en main
		/// (même règle que StyleProcessor.TryToUseStyle).
		/// </summary>
		private static InventoryItem WeaponFor(FakeGamePlayer fake, Style style)
			=> style.WeaponTypeRequirement == (int)eObjectType.Shield
				? fake.Inventory.GetItem(eInventorySlot.LeftHandWeapon)
				: fake.AttackWeapon;
	}
}
