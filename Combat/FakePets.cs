/*
 * FakePlayers - Combat/FakePets.cs
 *
 * Les PETS des alts. Deux genres d'invocations :
 *
 *   A. Le PET PRINCIPAL (permanent, contrôlé) : Cabaliste, Spiritmaster, Bonedancer, Enchanter, Druid, Hunter,
 *      pet de l'Animist...
 *      - invoqué dès qu'il manque, en combat comme hors combat (hors combat : au-dessus de
 *        MANA_START_PERCENT de mana) ;
 *      - ASSISTANCE : l'alt envoie son pet sur sa propre cible (mêmes priorités : /fake attack, défense,
 *        Guard...) ; quand le combat s'arrête, le pet revient près de l'alt ;
 *      - le pet reste en mode défensif : il ne part jamais seul sur un mob (pas d'adds) ;
 *      - un pet resté loin (pet fixe, ou qui ne suit pas) : hors combat, au-delà de FAR_DISTANCE,
 *        l'alt le renvoie et en réinvoque un près de lui.
 *   B. Les INVOCATIONS DE COMBAT (temporaires) : élémentaires du Theurgist, champignons de l'Animist.
 *      Utilisées en combat comme des sorts de dégâts, avant les autres (voir Combat/FakeDamage).
 *
 * Écartés : Necromancer (son pet est son corps, mécanique à part), sous-pets du Bonedancer, pets décoratifs.
 */

using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Les pets des alts.
	/// </summary>
	public static class FakePets
	{
		/// <summary>Hors combat, un pet principal n'est invoqué qu'au-dessus de ce pourcentage de mana.</summary>
		public const int MANA_START_PERCENT = 30;

		/// <summary>Hors combat, un pet plus loin que ça de l'alt est renvoyé puis réinvoqué près de lui.</summary>
		public const int FAR_DISTANCE = 1500;

		/// <summary>Types de sorts : pet principal (en majuscules).</summary>
		private static readonly HashSet<string> MAIN_PET_TYPES = new()
		{
			"SUMMONSIMULACRUM", "SUMMONSPIRITFIGHTER", "SUMMONCOMMANDER", "SUMMONUNDERHILL",
			"SUMMONDRUIDPET", "SUMMONHUNTERPET", "SUMMONANIMISTPET", "SUMMONELEMENTAL",
		};

		/// <summary>Types de sorts : invocation de combat (en majuscules).</summary>
		public static readonly HashSet<string> COMBAT_SUMMON_TYPES = new() { "SUMMONTHEURGISTPET", "SUMMONANIMISTFNF" };

		// ================================================================= pet principal

		/// <summary>Le pet principal de l'alt, vivant, ou null.</summary>
		public static GameNPC MainPet(FakeGamePlayer fake)
		{
			GameNPC pet = fake.ControlledBrain?.Body;
			return pet != null && pet.IsAlive && pet.ObjectState == GameObject.eObjectState.Active ? pet : null;
		}

		/// <summary>
		/// Invoque le pet principal s'il manque (ou le réinvoque près de l'alt s'il est resté loin).
		/// </summary>
		/// <param name="moved">true si la position de l'alt a changé (à envoyer aux joueurs proches).</param>
		/// <returns>true si l'alt invoque ce tic (le reste attend).</returns>
		internal static bool Update(FakeGamePlayer fake, GamePlayer owner, FakeFollowAction.FollowState state, out bool moved)
		{
			moved = false;
			if (!fake.IsAlive)
				return false;

			bool inCombat = fake.InCombat || owner.InCombat || fake.AttackState;
			GameNPC pet = MainPet(fake);

			// Pet resté loin (pet fixe, ou qui ne suit pas) : hors combat, on le renvoie.
			if (pet != null && !inCombat && !fake.IsWithinRadius(pet, FAR_DISTANCE))
			{
				fake.TargetObject = null; // sans cible, "release" renvoie le pet principal
				fake.CommandNpcRelease();
				fake.PetOrderedTarget = null;
				return false; // réinvoqué au prochain tic
			}
			if (pet != null || fake.ControlledBrain != null)
				return false;

			// Hors combat : garder du mana.
			if (!inCombat && fake.ManaPercent <= MANA_START_PERCENT)
				return false;

			FakeSpellCast.KnownSpell spell = FakeSpellCast.KnownSpells(fake)
				.Where(k => IsMainPet(k.Spell) && FakeSpellCast.IsReady(fake, k.Spell))
				.OrderByDescending(k => k.Spell.Level)
				.FirstOrDefault();
			if (spell == null)
				return false;

			fake.PetOrderedTarget = null;
			return FakeSpellCast.TryCast(fake, state, spell.Spell, spell.Line, fake, "pet", out moved)
			       != FakeSpellCast.Result.Failed;
		}

		/// <summary>
		/// Assistance : envoie le pet principal sur la cible de l'alt (une seule fois par cible).
		/// </summary>
		internal static void Assist(FakeGamePlayer fake, GameLiving target)
		{
			GameNPC pet = MainPet(fake);
			if (pet == null || fake.ControlledBrain is not IControlledBrain brain || target == null)
				return;

			if (fake.PetOrderedTarget == target && pet.TargetObject == target)
				return;

			brain.Attack(target);
			fake.PetOrderedTarget = target;
		}

		/// <summary>Fin du combat (ou /fake passive) : le pet revient près de l'alt.</summary>
		internal static void Recall(FakeGamePlayer fake)
		{
			if (fake.PetOrderedTarget == null)
				return;
			fake.PetOrderedTarget = null;

			if (MainPet(fake) != null && fake.ControlledBrain is ControlledNpcBrain brain)
				brain.FollowOwner();
		}

		/// <summary>Renvoie le pet principal (au retrait de l'alt).</summary>
		public static void Release(FakeGamePlayer fake)
		{
			if (fake.ControlledBrain == null)
				return;
			fake.TargetObject = null;
			fake.CommandNpcRelease();
		}

		// ================================================================= invocations de combat

		/// <summary>
		/// true si l'alt peut lancer cette invocation de combat maintenant (limites du serveur) :
		/// élémentaires du Theurgist (THEURGIST_PET_CAP), champignons de l'Animist (TURRET_PLAYER_CAP_COUNT).
		/// </summary>
		public static bool CombatSummonAllowed(FakeGamePlayer fake, Spell spell)
		{
			string type = (spell.SpellType ?? "").ToUpperInvariant();
			if (type == "SUMMONTHEURGISTPET")
				return fake.PetCount < ServerProperties.Properties.THEURGIST_PET_CAP;
			if (type == "SUMMONANIMISTFNF")
				return fake.PetCount < ServerProperties.Properties.TURRET_PLAYER_CAP_COUNT;
			return false;
		}

		/// <summary>
		/// Prépare une invocation au sol (champignons de l'Animist) : la cible au sol est posée sur la cible,
		/// et marquée visible (la ligne de vue vers la cible est vérifiée avant le lancement).
		/// </summary>
		public static void PrepareGroundTarget(FakeGamePlayer fake, Spell spell, GameLiving target)
		{
			if ((spell.Target ?? "").ToLowerInvariant() != "area")
				return;
			fake.GroundTargetPosition = target.Position;
			fake.GroundTargetInView = true;
		}

		// ================================================================= classement (aussi pour /fake admin spells)

		/// <summary>true si c'est une invocation de pet principal.</summary>
		public static bool IsMainPet(Spell spell)
			=> MAIN_PET_TYPES.Contains((spell.SpellType ?? "").ToUpperInvariant());

		/// <summary>true si c'est une invocation de combat (Theurgist, Animist).</summary>
		public static bool IsCombatSummon(Spell spell)
			=> COMBAT_SUMMON_TYPES.Contains((spell.SpellType ?? "").ToUpperInvariant());

		/// <summary>true si c'est un sort d'invocation (pris en charge ou non).</summary>
		public static bool IsSummon(Spell spell)
			=> (spell.SpellType ?? "").ToUpperInvariant().StartsWith("SUMMON");

		/// <summary>Le verdict d'une invocation pour /fake admin spells.</summary>
		public static string Verdict(Spell spell)
			=> IsMainPet(spell) ? "pet principal"
			 : IsCombatSummon(spell) ? "invocation de combat"
			 : "écarté : invocation non prise en charge (" + spell.SpellType + ")";
	}
}
