/*
 * FakePlayers - Commands/FakePlayerCommandHandler.cs
 *
 * La commande /fake tapée par le joueur dans le chat.
 * Cette classe ne fait que lire la commande, appeler FakePlayerMgr, puis afficher le résultat :
 * toute la logique (appel, suppression...) est dans FakePlayerMgr.
 */

using System.Collections.Generic;
using System.Linq;
using DOL.GS.Commands;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// La commande /fake : appel, liste et suppression des alts.
	/// L'attribut [Cmd] déclare la commande au serveur : son nom ("&amp;fake"), qui peut l'utiliser
	/// (ePrivLevel.Player = tous les joueurs) et les lignes d'aide affichées par DisplaySyntax.
	/// </summary>
	[Cmd(
		"&fake",
		ePrivLevel.Player,
		"Gère vos alts (personnages de votre compte joués par le serveur).",
		"/fake call <personnage> - appelle un de vos personnages et le groupe avec vous",
		"/fake team - appelle toute votre équipe",
		"/fake team add <personnage> - ajoute un personnage à l'équipe de ce personnage-ci",
		"/fake team remove <personnage> - retire un personnage de l'équipe",
		"/fake team list - affiche l'équipe",
		"/fake list - liste vos alts en jeu",
		"/fake remove - renvoie votre alt sélectionné (sans sélection : tous vos alts)",
		"/fake remove <nom> - renvoie un de vos alts",
		"/fake remove all - renvoie tous vos alts",
		"/fake stay [nom] - votre alt ciblé (ou nommé, sans cible : tous) reste sur place",
		"/fake follow [nom] - votre alt ciblé (ou nommé, sans cible : tous) vous suit à nouveau",
		"/fake attack [nom] - vos alts (ou l'alt nommé) attaquent votre cible, même s'ils combattent déjà (sauf soigneurs)",
		"/fake passive [nom] - votre alt ciblé (ou nommé, sans cible : tous) ne combat plus",
		"/fake fight [nom] - votre alt ciblé (ou nommé, sans cible : tous) combat à nouveau",
		"/fake guard <nom> - votre alt protège (Guard) votre cible, ou vous sans cible",
		"/fake guard <nom> off | auto - plus de Guard, ou retour au choix automatique (soigneur, sinon vous)",
		"/fake admin - commandes d'administration (GM et plus)")]
	public class FakePlayerCommandHandler : AbstractCommandHandler, ICommandHandler
	{
		/// <summary>
		/// Appelée par le serveur à chaque /fake.
		/// args[0] = "&amp;fake", args[1] = la sous-commande (call, team, list, remove, stay, follow,
		/// attack, passive, fight, guard, admin),
		/// args[2] = le paramètre éventuel.
		/// </summary>
		public void OnCommand(GameClient client, string[] args)
		{
			GamePlayer player = client?.Player;
			if (player == null)
				return;

			// Pas de sous-commande : on affiche l'aide.
			if (args.Length < 2)
			{
				DisplaySyntax(client);
				return;
			}

			switch (args[1].ToLowerInvariant())
			{
				case "call":
					Call(client, player, args);
					break;
				case "team":
					Team(client, player, args);
					break;
				case "list":
					List(client, FakePlayerMgr.GetFakesOf(player), false);
					break;
				case "remove":
					Remove(client, player, args);
					break;
				case "stay":
					SetFollow(client, player, args, false);
					break;
				case "follow":
					SetFollow(client, player, args, true);
					break;
				case "attack":
					Attack(client, player, args);
					break;
				case "passive":
					SetPassive(client, player, args, true);
					break;
				case "fight":
					SetPassive(client, player, args, false);
					break;
				case "guard":
					Guard(client, player, args);
					break;
				case "admin":
					Admin(client, player, args);
					break;
				default:
					DisplaySyntax(client); // sous-commande inconnue : on affiche l'aide
					break;
			}
		}

		/// <summary>
		/// /fake call &lt;personnage&gt; : appelle un personnage du compte à côté du joueur et le groupe avec lui.
		/// Fait en arrière-plan (chargement depuis la base), un appel à la fois : voir FakePlayerMgr.RunInBackground.
		/// Affiche "arrive" (avec la classe et le niveau) ou la raison de l'échec.
		/// </summary>
		private void Call(GameClient client, GamePlayer player, string[] args)
		{
			if (args.Length < 3)
			{
				DisplayMessage(client, "Usage : /fake call <personnage>");
				return;
			}

			string name = args[2];
			FakePlayerMgr.RunInBackground("appel de " + name, () =>
			{
				FakeGamePlayer fake = FakePlayerMgr.Call(player, name, out string error);
				if (fake == null)
					DisplayMessage(client, "Impossible d'appeler {0} : {1}.", name, error);
				else
					DisplayMessage(client, "{0} arrive : {1} niveau {2}.",
						fake.Name, fake.CharacterClass.GetSalutation(fake.Gender), fake.Level);
			});
		}

		/// <summary>
		/// /fake team [add|remove|list] : l'équipe de ce personnage (voir Core/FakeTeam).
		///  - sans rien : appelle toute l'équipe ;
		///  - add &lt;perso&gt; / remove &lt;perso&gt; : compose l'équipe ;
		///  - list : affiche l'équipe dans l'ordre (= ordre de la file indienne).
		/// </summary>
		private void Team(GameClient client, GamePlayer player, string[] args)
		{
			string action = args.Length >= 3 ? args[2].ToLowerInvariant() : "";

			switch (action)
			{
				case "":
					if (!FakeTeam.CallAll(player))
						DisplayMessage(client, "Votre équipe est vide. Ajoutez des personnages avec /fake team add <personnage>.");
					else
						DisplayMessage(client, "Votre équipe arrive...");
					break;

				case "add":
					if (args.Length < 4)
					{
						DisplayMessage(client, "Usage : /fake team add <personnage>");
						return;
					}
					if (FakeTeam.Add(player, args[3], out string added, out string addError))
						DisplayMessage(client, "{0} ajouté à votre équipe ({1}/{2}).", added, FakeTeam.Get(player).Count, FakeTeam.MaxSize);
					else
						DisplayMessage(client, "Impossible d'ajouter {0} : {1}.", args[3], addError);
					break;

				case "remove":
					if (args.Length < 4)
					{
						DisplayMessage(client, "Usage : /fake team remove <personnage>");
						return;
					}
					if (FakeTeam.Remove(player, args[3], out string removed, out string removeError))
						DisplayMessage(client, "{0} retiré de votre équipe.", removed);
					else
						DisplayMessage(client, "Impossible de retirer {0} : {1}.", args[3], removeError);
					break;

				case "list":
					List<string> team = FakeTeam.Get(player);
					if (team.Count == 0)
					{
						DisplayMessage(client, "Votre équipe est vide.");
						return;
					}
					DisplayMessage(client, "Équipe de {0} ({1}/{2}) :", player.Name, team.Count, FakeTeam.MaxSize);
					for (int i = 0; i < team.Count; i++)
						DisplayMessage(client, "  {0}. {1}", i + 1, team[i]);
					break;

				default:
					DisplayMessage(client, "Usage : /fake team [add <personnage> | remove <personnage> | list]");
					break;
			}
		}

		/// <summary>
		/// Affiche une liste d'alts, numérotés, avec leur classe, leur niveau et leur zone
		/// (et, pour l'admin, le nom de celui qui les a appelés).
		/// </summary>
		private void List(GameClient client, List<FakeGamePlayer> fakes, bool showOwner)
		{
			if (fakes.Count == 0)
			{
				DisplayMessage(client, showOwner ? "Aucun alt en jeu." : "Vous n'avez aucun alt en jeu.");
				return;
			}

			DisplayMessage(client, showOwner ? "Alts en jeu sur le serveur ({0}) :" : "Vos alts en jeu ({0}) :", fakes.Count);
			for (int i = 0; i < fakes.Count; i++)
			{
				FakeGamePlayer f = fakes[i];
				GameLiving guarded = FakeGuard.CurrentTarget(f);
				DisplayMessage(client, "  {0}. {1} ({2} niveau {3}, {4}{5}){6}",
					i + 1, f.Name, f.CharacterClass.GetSalutation(f.Gender), f.Level,
					f.CurrentZone?.Description ?? "?",
					showOwner ? ", appelé par " + (f.Owner?.Name ?? "?") : "",
					guarded != null ? ", garde " + guarded.Name : "");
			}
		}

		/// <summary>
		/// /fake remove [nom | all] : renvoie SES alts (jamais ceux des autres joueurs).
		///  - avec un nom : renvoie cet alt, s'il est à lui ;
		///  - "all" : renvoie tous ses alts ;
		///  - sans argument : renvoie l'alt sélectionné, ou tous ses alts s'il n'y a aucune sélection.
		///    Une cible qui n'est pas un de ses alts (mob, PNJ, vrai joueur, alt d'un autre) ne renvoie rien.
		/// </summary>
		private void Remove(GameClient client, GamePlayer player, string[] args)
		{
			FakeGamePlayer fake;

			if (args.Length < 3)
			{
				// Pas de paramètre : on regarde la cible sélectionnée en jeu.
				GameObject target = player.TargetObject;
				if (target == null)
				{
					RemoveList(client, FakePlayerMgr.GetFakesOf(player));
					return;
				}

				fake = target as FakeGamePlayer;
				if (fake == null || fake.Owner != player)
				{
					DisplayMessage(client, "{0} n'est pas un de vos alts, rien n'a été renvoyé.", target.Name);
					return;
				}
			}
			else if (args[2].ToLowerInvariant() == "all")
			{
				RemoveList(client, FakePlayerMgr.GetFakesOf(player));
				return;
			}
			else
			{
				fake = FakePlayerMgr.FindByName(args[2]);
				if (fake == null || fake.Owner != player)
				{
					DisplayMessage(client, "Aucun de vos alts ne s'appelle {0}. Voir /fake list.", args[2]);
					return;
				}
			}

			RemoveOne(client, fake);
		}

		/// <summary>Renvoie un alt et affiche le résultat.</summary>
		private void RemoveOne(GameClient client, FakeGamePlayer fake)
		{
			// Nom gardé avant la suppression, pour le message.
			string name = fake.Name;
			if (FakePlayerMgr.Remove(fake))
				DisplayMessage(client, "{0} est reparti.", name);
			else
				DisplayMessage(client, "Erreur à la suppression de {0} (voir la console du serveur).", name);
		}

		/// <summary>Renvoie une liste d'alts et affiche combien sont repartis.</summary>
		private void RemoveList(GameClient client, List<FakeGamePlayer> fakes)
		{
			int count = fakes.Count(FakePlayerMgr.Remove);
			DisplayMessage(client, "{0} alt(s) renvoyé(s).", count);
		}

		/// <summary>
		/// /fake stay [nom] et /fake follow [nom] : change l'ordre de déplacement.
		/// </summary>
		/// <param name="follow">true = suivre (/fake follow), false = rester (/fake stay).</param>
		private void SetFollow(GameClient client, GamePlayer player, string[] args, bool follow)
		{
			List<FakeGamePlayer> fakes = ResolveFakes(client, player, args);
			if (fakes == null)
				return;

			foreach (FakeGamePlayer fake in fakes)
				fake.IsFollowing = follow;

			string names = string.Join(", ", fakes.ConvertAll(f => f.Name));
			DisplayMessage(client, follow ? "{0} : vous suit." : "{0} : reste sur place.", names);
		}

		/// <summary>
		/// /fake passive [nom] et /fake fight [nom] : désactive ou réactive le combat (voir Combat/FakeCombat).
		/// </summary>
		/// <param name="passive">true = aucun combat (/fake passive), false = combat (/fake fight).</param>
		private void SetPassive(GameClient client, GamePlayer player, string[] args, bool passive)
		{
			List<FakeGamePlayer> fakes = ResolveFakes(client, player, args);
			if (fakes == null)
				return;

			foreach (FakeGamePlayer fake in fakes)
			{
				fake.IsPassive = passive;
				if (passive)
					FakeCombat.EndFight(fake); // arrête tout de suite un combat en cours
			}

			string names = string.Join(", ", fakes.ConvertAll(f => f.Name));
			DisplayMessage(client, passive ? "{0} : ne combat plus." : "{0} : combat à nouveau.", names);
		}

		/// <summary>
		/// /fake attack [nom] : vos alts (ou l'alt nommé) attaquent VOTRE cible actuelle (voir Combat/FakeCombat).
		/// L'ordre passe avant tout, même si l'alt combat déjà, et vaut aussi pour un alt en stay.
		/// Ne sont pas concernés : les soigneurs (HealMode soigneur) et les alts passifs.
		/// Ici, la cible du joueur est l'ennemi : sans nom, ce sont donc TOUS ses alts qui obéissent.
		/// </summary>
		private void Attack(GameClient client, GamePlayer player, string[] args)
		{
			if (player.TargetObject is not GameLiving target)
			{
				DisplayMessage(client, "Vous n'avez pas de cible.");
				return;
			}

			List<FakeGamePlayer> fakes;
			if (args.Length >= 3)
			{
				FakeGamePlayer named = FakePlayerMgr.FindByName(args[2]);
				if (named == null || named.Owner != player)
				{
					DisplayMessage(client, "Aucun de vos alts ne s'appelle {0}. Voir /fake list.", args[2]);
					return;
				}
				fakes = new List<FakeGamePlayer> { named };
			}
			else
			{
				fakes = FakePlayerMgr.GetFakesOf(player);
			}

			if (fakes.Count == 0)
			{
				DisplayMessage(client, "Vous n'avez aucun alt en jeu.");
				return;
			}

			var attacking = new List<string>();
			var skipped = new List<string>();
			foreach (FakeGamePlayer fake in fakes)
			{
				if (fake.IsPassive)
					skipped.Add(fake.Name + " (passif)");
				else if (fake.CombatMode != FakePlayerClass.MODE_MELEE && !FakeDamage.IsNuker(fake))
					skipped.Add(fake.Name + " (soigneur)");
				else if (!FakeCombat.IsValidTarget(fake, player, target))
					skipped.Add(fake.Name + " (cible non attaquable ou trop loin)");
				else
				{
					fake.OrderedTarget = target;
					attacking.Add(fake.Name);
				}
			}

			if (attacking.Count > 0)
				DisplayMessage(client, "{0} : attaque {1}.", string.Join(", ", attacking), target.Name);
			if (skipped.Count > 0)
				DisplayMessage(client, "N'obéit pas : {0}.", string.Join(", ", skipped));
		}

		/// <summary>
		/// /fake admin spells [nom] : diagnostic des sorts d'un alt (l'alt nommé, sinon l'alt ciblé), de n'importe quel joueur.
		/// Affiche son niveau, ses réglages de soin, de buff et d'aggro, puis chaque sort appris avec son type, sa cible,
		/// sa portée, sa valeur, et s'il est retenu comme soin ou buff (ou pourquoi il est écarté).
		/// </summary>
		private void Spells(GameClient client, GamePlayer player, string[] args)
		{
			FakeGamePlayer fake = args.Length >= 4 ? FakePlayerMgr.FindByName(args[3]) : player.TargetObject as FakeGamePlayer;
			if (fake == null)
			{
				DisplayMessage(client, "Usage : /fake admin spells <nom>, ou ciblez un alt.");
				return;
			}

			string healMode = fake.HealMode switch
			{
				FakePlayerClass.HEAL_NEVER => "ne soigne jamais",
				FakePlayerClass.HEAL_EMERGENCY => "urgence sous " + fake.EmergencyThreshold + " %",
				FakePlayerClass.HEAL_HEALER => FakeHeals.HasHealSpells(fake)
					? "soigneur sous " + fake.HealThreshold + " %"
					: "soigneur, mais aucun soin connu (lance des dégâts)",
				_ => "?",
			};
			string buffMode = fake.BuffMode switch
			{
				FakePlayerClass.BUFF_NEVER => "jamais",
				FakePlayerClass.BUFF_SELF => "lui-même",
				_ => "le groupe",
			};
			DisplayMessage(client, "{0} : {1} niveau {2}, mode {3}, soins : {4}, buffs : {5}, aggro {6} %, zone {11}, mana {7}/{8}, concentration {9}/{10}.",
				fake.Name, fake.CharacterClass.GetSalutation(fake.Gender), fake.Level,
				fake.CombatMode == FakePlayerClass.MODE_MELEE ? "mêlée" : "sorts",
				healMode, buffMode, fake.AggroPercent, fake.Mana, fake.MaxMana, fake.Concentration, fake.MaxConcentration,
				fake.AoeMinTargets > 0 ? "dès " + fake.AoeMinTargets + " mobs" : "jamais");

			List<FakeSpellCast.KnownSpell> spells = FakeSpellCast.KnownSpells(fake);
			if (spells.Count == 0)
			{
				DisplayMessage(client, "  Aucun sort appris.");
				return;
			}

			int heals = 0, buffs = 0, damage = 0, chants = 0, rez = 0;
			foreach (var group in spells.GroupBy(k => k.Line.Name))
			{
				DisplayMessage(client, "Ligne {0} :", group.Key);
				foreach (FakeSpellCast.KnownSpell known in group.OrderBy(k => k.Spell.Level))
				{
					Spell s = known.Spell;
					string verdict = FakeHeals.Classify(s, out bool isHeal, out _);
					string chantVerdict = FakeChants.Classify(s, out bool isChant);
					if (FakePets.IsSummon(s))
					{
						verdict = FakePets.Verdict(s);
					}
					else if (FakeRez.IsRez(s))
					{
						rez++;
						verdict = "résurrection (de vous seulement, combat fini)";
					}
					else if (isChant || s.IsPulsing)
					{
						// Sort pulsé : chant (voir Combat/FakeChants).
						if (isChant)
							chants++;
						verdict = isChant && FakeChants.IsActive(fake, s) ? chantVerdict + " actif" : chantVerdict;
					}
					else if (isHeal)
						heals++;
					else if (!s.IsHealing)
					{
						// Pas un soin : est-ce un buff ?
						string buffVerdict = FakeBuffs.Classify(s, out bool isBuff);
						if (isBuff)
							buffs++;
						if (isBuff || buffVerdict != "écarté : pas un buff")
							verdict = buffVerdict;
						else
						{
							// Ni soin ni buff : est-ce un sort de dégâts ?
							string damageVerdict = FakeDamage.Classify(s, out bool isDamage);
							if (isDamage)
								damage++;
							verdict = damageVerdict == "écarté : pas un sort de dégâts" ? "écarté : ni soin, ni buff, ni dégâts" : damageVerdict;
						}
					}
					DisplayMessage(client, "  {0} (niv {1}) : {2}, cible {3}, portée {4}, valeur {5} -> {6}",
						s.Name, s.Level, s.SpellType, s.Target, s.Range, s.Value, verdict);
				}
			}
			DisplayMessage(client, "{0} sort(s) appris, dont {1} soin(s), {2} buff(s), {3} chant(s) (max {5} actifs{6}), {8} résurrection(s) et {4} sort(s) de dégâts retenu(s){7}.",
				spells.Count, heals, buffs, chants, damage, fake.CharacterClass.MaxPulsingSpells,
				FakeChants.IsSinger(fake) ? ", chanteur : ne combat pas" : "",
				damage > 0 && !FakeDamage.IsNuker(fake) ? " (dégâts inutilisés : l'alt n'est pas lanceur de dégâts)" : "",
				rez);
		}

		// ================================================================= guard

		/// <summary>
		/// /fake guard &lt;nom&gt; [off | auto] : le Guard d'un de SES alts (voir Combat/FakeGuard).
		///  - sans rien : protège la cible du joueur (un membre du groupe), ou le joueur s'il n'a pas de cible ;
		///  - off : plus de Guard ; auto : retour au choix automatique (soigneur du groupe, sinon le joueur).
		/// </summary>
		private void Guard(GameClient client, GamePlayer player, string[] args)
		{
			if (args.Length < 3)
			{
				DisplayMessage(client, "Usage : /fake guard <nom> [off | auto]");
				return;
			}

			FakeGamePlayer fake = FakePlayerMgr.FindByName(args[2]);
			if (fake == null || fake.Owner != player)
			{
				DisplayMessage(client, "Aucun de vos alts ne s'appelle {0}. Voir /fake list.", args[2]);
				return;
			}
			if (!FakeGuard.CanGuard(fake, out string reason))
			{
				DisplayMessage(client, "Impossible : {0}.", reason);
				return;
			}

			string option = args.Length >= 4 ? args[3].ToLowerInvariant() : "";
			if (option == "off")
			{
				fake.GuardDisabled = true;
				fake.GuardChoice = null;
				FakeGuard.Cancel(fake);
				DisplayMessage(client, "{0} : plus de Guard.", fake.Name);
				return;
			}
			if (option == "auto")
			{
				fake.GuardDisabled = false;
				fake.GuardChoice = null;
				FakeGuard.Update(fake, player);
				GameLiving auto = FakeGuard.CurrentTarget(fake);
				DisplayMessage(client, "{0} : Guard automatique{1}.", fake.Name, auto != null ? " (protège " + auto.Name + ")" : "");
				return;
			}

			// Protège la cible du joueur, ou le joueur lui-même.
			GameLiving target = player.TargetObject as GameLiving ?? player;
			if (target == fake)
			{
				DisplayMessage(client, "{0} ne peut pas se protéger lui-même.", fake.Name);
				return;
			}
			if (fake.Group == null || target.Group != fake.Group)
			{
				DisplayMessage(client, "{0} n'est pas dans votre groupe.", target.Name);
				return;
			}

			fake.GuardDisabled = false;
			fake.GuardChoice = target;
			FakeGuard.Update(fake, player);
			DisplayMessage(client, "{0} protège {1}.", fake.Name, target == player ? "vous" : target.Name);
		}

		// ================================================================= administration

		/// <summary>
		/// /fake admin ... : commandes réservées aux GM (et plus), qui agissent sur les alts de TOUS les joueurs.
		///  - list : tous les alts en jeu, avec leur propriétaire ;
		///  - remove &lt;nom | all&gt; : renvoie un alt (de n'importe qui) ou tous les alts du serveur ;
		///  - spells [nom] : diagnostic des sorts d'un alt (nommé ou ciblé) ;
		///  - nav : teste le navmesh à votre position (et le chemin vers votre cible).
		/// </summary>
		private void Admin(GameClient client, GamePlayer player, string[] args)
		{
			if (client.Account.PrivLevel <= (uint)ePrivLevel.Player)
			{
				DisplayMessage(client, "Commande réservée aux administrateurs.");
				return;
			}

			string action = args.Length >= 3 ? args[2].ToLowerInvariant() : "";
			switch (action)
			{
				case "list":
					List(client, FakePlayerMgr.GetFakes(), true);
					break;

				case "remove":
					if (args.Length < 4)
					{
						DisplayMessage(client, "Usage : /fake admin remove <nom | all>");
						return;
					}
					if (args[3].ToLowerInvariant() == "all")
					{
						RemoveList(client, FakePlayerMgr.GetFakes());
						return;
					}
					FakeGamePlayer fake = FakePlayerMgr.FindByName(args[3]);
					if (fake == null)
					{
						DisplayMessage(client, "Aucun alt nommé {0} en jeu. Voir /fake admin list.", args[3]);
						return;
					}
					RemoveOne(client, fake);
					break;

				case "spells":
					Spells(client, player, args);
					break;

				case "nav":
					// Diagnostic du navmesh (voir Movement/FakeNavCheck).
					foreach (string line in FakeNavCheck.Check(player))
						DisplayMessage(client, line);
					break;

				default:
					DisplayMessage(client, "Commandes d'administration (agissent sur les alts de tous les joueurs) :");
					DisplayMessage(client, "  /fake admin list - liste tous les alts en jeu, avec leur propriétaire");
					DisplayMessage(client, "  /fake admin remove <nom> - renvoie un alt, quel que soit son propriétaire");
					DisplayMessage(client, "  /fake admin remove all - renvoie tous les alts du serveur");
					DisplayMessage(client, "  /fake admin spells [nom] - diagnostic : sorts de l'alt ciblé (ou nommé)");
					DisplayMessage(client, "  /fake admin nav - teste le navmesh à votre position (et le chemin vers votre cible)");
					break;
			}
		}

		/// <summary>
		/// Les alts visés par stay, follow, passive et fight :
		///  - avec un nom (args[2]) : cet alt ;
		///  - sans nom : l'alt ciblé, ou tous les alts du joueur s'il n'a aucune cible.
		///    Une cible qui n'est pas un alt ne vise rien.
		/// Affiche le message d'erreur et renvoie null si aucun alt n'est visé.
		/// </summary>
		private List<FakeGamePlayer> ResolveFakes(GameClient client, GamePlayer player, string[] args)
		{
			var fakes = new List<FakeGamePlayer>();

			if (args.Length >= 3)
			{
				FakeGamePlayer named = FakePlayerMgr.FindByName(args[2]);
				if (named == null || named.Owner != player)
				{
					DisplayMessage(client, "Aucun de vos alts ne s'appelle {0}. Voir /fake list.", args[2]);
					return null;
				}
				fakes.Add(named);
			}
			else if (player.TargetObject != null)
			{
				if (player.TargetObject is not FakeGamePlayer targeted || targeted.Owner != player)
				{
					DisplayMessage(client, "{0} n'est pas un de vos alts.", player.TargetObject.Name);
					return null;
				}
				fakes.Add(targeted);
			}
			else
			{
				fakes = FakePlayerMgr.GetFakesOf(player);
			}

			if (fakes.Count == 0)
			{
				DisplayMessage(client, "Vous n'avez aucun alt en jeu.");
				return null;
			}
			return fakes;
		}

	}
}
