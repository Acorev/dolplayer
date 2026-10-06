/*
 * FakePlayers - Combat/FakeLineOfSight.cs
 *
 * Ligne de vue d'un alt vers une cible, vérifiée par le JEU DU PROPRIÉTAIRE.
 *
 * Pourquoi : pour un vrai joueur, le serveur demande à son jeu si la cible est visible
 * (SendCheckLOS). Le faux client d'un alt ne répond jamais : sans ce fichier, les alts
 * lanceraient leurs sorts à travers les murs. On pose donc la question au jeu du propriétaire,
 * qui voit l'alt et la cible (ils sont près de lui), et qui calcule la ligne de vue entre les deux.
 *
 * La réponse arrive un peu plus tard (aller-retour réseau). Trois réponses possibles :
 *   - Visible    : dernière réponse connue = visible ;
 *   - NotVisible : dernière réponse connue = pas visible ;
 *   - Unknown    : aucune réponse encore (première question en cours).
 * Pendant qu'une nouvelle question est en cours, on garde la DERNIÈRE réponse connue (si elle n'est pas
 * trop vieille) : sinon l'alt croirait la cible cachée à chaque renouvellement et se rapprocherait.
 */

using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Vérification de la ligne de vue des alts, par le jeu de leur propriétaire.
	/// </summary>
	public static class FakeLineOfSight
	{
		/// <summary>Résultat d'une vérification.</summary>
		public enum Sight { Visible, NotVisible, Unknown }

		/// <summary>Une réponse est renouvelée (nouvelle question) passé ce délai (ms).</summary>
		private const int REFRESH_MS = 1500;

		/// <summary>Une réponse reste utilisable, le temps du renouvellement, jusqu'à ce délai (ms).</summary>
		private const int ANSWER_MAX_AGE_MS = 5000;

		/// <summary>Une question sans réponse est reposée après ce délai (ms).</summary>
		private const int REQUEST_RETRY_MS = 1000;

		/// <summary>En dessous de cette distance, la cible est considérée comme visible sans demander.</summary>
		private const int ALWAYS_VISIBLE_DISTANCE = 64;

		/// <summary>Les entrées plus vieilles que ce délai (ms) sont effacées.</summary>
		private const int FORGET_MS = 30000;

		/// <summary>L'état d'une paire alt / cible.</summary>
		private class Entry
		{
			public bool Visible;
			public long AnsweredAt = -1;   // -1 = pas encore de réponse
			public long RequestedAt = -1;
		}

		private static readonly Dictionary<(FakeGamePlayer, GameObject), Entry> _entries = new();
		private static readonly object _lock = new();

		/// <summary>true si l'alt voit la cible (raccourci de Check).</summary>
		public static bool CanSee(FakeGamePlayer fake, GameObject target)
			=> Check(fake, target) == Sight.Visible;

		/// <summary>
		/// La ligne de vue de l'alt vers la cible, d'après la dernière réponse du jeu du propriétaire.
		/// Pose une nouvelle question si la réponse date (elle servira aux tics suivants).
		/// </summary>
		public static Sight Check(FakeGamePlayer fake, GameObject target)
		{
			if (target == null)
				return Sight.NotVisible;
			if (target == fake || fake.IsWithinRadius(target, ALWAYS_VISIBLE_DISTANCE))
				return Sight.Visible;

			// Pas de jeu pour vérifier (propriétaire absent) : on laisse faire.
			GamePlayer owner = fake.Owner;
			if (owner?.Client == null || owner.ObjectState != GameObject.eObjectState.Active)
				return Sight.Visible;

			long now = Environment.TickCount64;
			Entry entry;
			bool ask = false;

			lock (_lock)
			{
				Cleanup(now);

				var key = (fake, target);
				if (!_entries.TryGetValue(key, out entry))
				{
					entry = new Entry();
					_entries[key] = entry;
				}

				bool fresh = entry.AnsweredAt >= 0 && now - entry.AnsweredAt <= REFRESH_MS;
				bool pending = entry.RequestedAt > entry.AnsweredAt && now - entry.RequestedAt <= REQUEST_RETRY_MS;

				if (!fresh && !pending)
				{
					entry.RequestedAt = now;
					ask = true;
				}
			}

			if (ask)
			{
				// Le jeu du propriétaire calcule la ligne de vue de l'alt vers la cible.
				Entry e = entry;
				owner.Out.SendCheckLOS(fake, target, (player, response, targetOID) =>
				{
					lock (_lock)
					{
						e.Visible = (response & 0x100) == 0x100; // même test que SpellHandler.CheckLOSPlayerToTarget
						e.AnsweredAt = Environment.TickCount64;
					}
				});
			}

			lock (_lock)
			{
				// Aucune réponse utilisable : on ne sait pas encore.
				if (entry.AnsweredAt < 0 || now - entry.AnsweredAt > ANSWER_MAX_AGE_MS)
					return Sight.Unknown;
				return entry.Visible ? Sight.Visible : Sight.NotVisible;
			}
		}

		/// <summary>Efface les vieilles entrées (alts supprimés, cibles mortes...).</summary>
		private static void Cleanup(long now)
		{
			if (_entries.Count < 64)
				return;
			foreach (var key in _entries.Where(kv => now - Math.Max(kv.Value.AnsweredAt, kv.Value.RequestedAt) > FORGET_MS)
			                            .Select(kv => kv.Key).ToList())
				_entries.Remove(key);
		}
	}
}
