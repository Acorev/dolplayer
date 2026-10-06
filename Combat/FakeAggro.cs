/*
 * FakePlayers - Combat/FakeAggro.cs
 *
 * L'aggro générée par les alts, réglable par classe (colonne AggroPercent de la table FakePlayerClass).
 *
 * Dans DOLSharp, un mob ajoute à sa liste d'aggro :
 *   - les dégâts qu'on lui fait (mêlée et sorts) ;
 *   - les soins faits sur ses ennemis, si le soigneur est déjà dans sa liste
 *     (et tout le groupe y entre avec 1 point dès qu'un membre est pris en aggro).
 * On ne modifie pas DOLSharp : on écoute ces deux événements, et quand la source est un alt, on corrige
 * l'aggro de la différence avec son pourcentage :
 *   soin de 1000 à 30 %   → -700 (il reste 300) ;
 *   dégâts de 100 à 200 % → +100 (total 200).
 * Les vrais joueurs ne sont pas concernés.
 *
 * Pourquoi la correction est différée : notre écouteur est appelé AVANT le cerveau du mob, et DOLSharp
 * ne descend jamais une aggro sous 1. Retirer avant que le mob ait ajouté ferait perdre une partie
 * de la correction. Les corrections sont donc cumulées, puis appliquées au tic suivant de l'alt
 * (au plus 250 ms plus tard, voir Movement/FakeFollowAction).
 */

using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.Events;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Ramène l'aggro générée par chaque alt au pourcentage de sa classe.
	/// </summary>
	public static class FakeAggro
	{
		/// <summary>
		/// Corrections en attente, par alt et par mob : somme de (montant × (pourcentage - 100)).
		/// Divisée par 100 seulement à l'application, pour ne pas perdre les petits montants.
		/// </summary>
		private static readonly Dictionary<(FakeGamePlayer Fake, GameNPC Mob), long> _pending = new();
		private static readonly object _lock = new();

		/// <summary>Au démarrage : écoute les dégâts reçus et les soins vus par tous les mobs.</summary>
		[ScriptLoadedEvent]
		public static void OnScriptLoaded(DOLEvent e, object sender, EventArgs args)
		{
			GameEventMgr.AddHandler(GameObjectEvent.TakeDamage, OnTakeDamage);
			GameEventMgr.AddHandler(GameLivingEvent.EnemyHealed, OnEnemyHealed);
		}

		/// <summary>À l'arrêt des scripts : on se désabonne.</summary>
		[ScriptUnloadedEvent]
		public static void OnScriptUnloaded(DOLEvent e, object sender, EventArgs args)
		{
			GameEventMgr.RemoveHandler(GameObjectEvent.TakeDamage, OnTakeDamage);
			GameEventMgr.RemoveHandler(GameLivingEvent.EnemyHealed, OnEnemyHealed);
		}

		// ================================================================= événements

		/// <summary>Un mob a pris des dégâts : si c'est un alt qui frappe, on corrige l'aggro.</summary>
		private static void OnTakeDamage(DOLEvent e, object sender, EventArgs args)
		{
			if (sender is not GameNPC mob || args is not TakeDamageEventArgs damage)
				return;
			if (damage.DamageSource is not FakeGamePlayer fake || mob.Brain is not IOldAggressiveBrain)
				return;

			// Même montant que celui ajouté par le mob (StandardMobBrain).
			Queue(fake, mob, damage.DamageAmount + damage.CriticalAmount);
		}

		/// <summary>Un ennemi du mob a été soigné : si c'est un alt qui soigne, on corrige l'aggro.</summary>
		private static void OnEnemyHealed(DOLEvent e, object sender, EventArgs args)
		{
			if (sender is not GameNPC mob || args is not EnemyHealedEventArgs heal)
				return;
			if (heal.HealSource is not FakeGamePlayer fake || mob.Brain is not IOldAggressiveBrain brain)
				return;

			// Le mob ne compte le soin que si le soigneur est déjà dans sa liste : même règle ici.
			if (brain.GetAggroAmountForLiving(fake) <= 0)
				return;

			Queue(fake, mob, heal.HealAmount);
		}

		/// <summary>Ajoute une correction en attente pour ce montant d'aggro.</summary>
		private static void Queue(FakeGamePlayer fake, GameNPC mob, int amount)
		{
			if (amount <= 0 || fake.AggroPercent == 100)
				return;

			lock (_lock)
			{
				_pending.TryGetValue((fake, mob), out long sum);
				_pending[(fake, mob)] = sum + (long)amount * (fake.AggroPercent - 100);
			}
		}

		// ================================================================= application

		/// <summary>
		/// Applique les corrections en attente de cet alt. Appelé à chaque tic de l'alt.
		/// </summary>
		public static void Flush(FakeGamePlayer fake)
		{
			List<(GameNPC Mob, long Sum)> todo;
			lock (_lock)
			{
				if (_pending.Count == 0)
					return;

				todo = _pending.Where(kv => kv.Key.Fake == fake).Select(kv => (kv.Key.Mob, kv.Value)).ToList();
				foreach (var item in todo)
					_pending.Remove((fake, item.Mob));

				// Ménage : corrections d'alts supprimés (qui n'ont plus de tic).
				if (_pending.Count > 64)
					foreach (var key in _pending.Keys.Where(k => k.Fake.ObjectState == GameObject.eObjectState.Deleted).ToList())
						_pending.Remove(key);
			}

			foreach (var (mob, sum) in todo)
			{
				long delta = sum / 100;
				if (delta == 0 || !mob.IsAlive || mob.ObjectState != GameObject.eObjectState.Active)
					continue;
				if (mob.Brain is not IOldAggressiveBrain brain)
					continue;

				// L'alt n'est plus dans la liste (mob reparti, liste vidée) : ne pas l'y remettre.
				if (brain.GetAggroAmountForLiving(fake) <= 0)
					continue;

				brain.AddToAggroList(fake, (int)Math.Clamp(delta, int.MinValue, int.MaxValue));
			}
		}
	}
}
