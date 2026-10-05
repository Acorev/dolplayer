/*
 * FakePlayers - Events/FakeGroupWatcher.cs
 *
 * Suppression automatique des alts, en écoutant les événements du serveur :
 *   - un alt quitte son groupe (le joueur l'a exclu, ou le groupe s'est dissous) → cet alt est supprimé ;
 *   - le joueur quitte son groupe (disband) → tous ses alts sont supprimés ;
 *   - le joueur se déconnecte (Quit ou perte de connexion) → tous ses alts sont supprimés.
 *     Indispensable : sinon il pourrait se reconnecter sur un personnage déjà en jeu comme alt.
 *
 * GamePlayerEvent.LeaveGroup est envoyé par DOLSharp (Group.RemoveMember) chaque fois qu'un joueur
 * quitte un groupe, quelle qu'en soit la raison.
 */

using System;
using DOL.Events;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Écoute les événements de groupe et de déconnexion pour supprimer les alts.
	/// </summary>
	public static class FakeGroupWatcher
	{
		/// <summary>Au démarrage du serveur (scripts chargés) : on s'abonne aux événements.</summary>
		[ScriptLoadedEvent]
		public static void OnScriptLoaded(DOLEvent e, object sender, EventArgs args)
		{
			GameEventMgr.AddHandler(GamePlayerEvent.LeaveGroup, OnLeaveGroup);
			GameEventMgr.AddHandler(GamePlayerEvent.Quit, OnOwnerGone);
			GameEventMgr.AddHandler(GamePlayerEvent.Linkdeath, OnOwnerGone);
		}

		/// <summary>À l'arrêt du serveur (scripts déchargés) : on se désabonne.</summary>
		[ScriptUnloadedEvent]
		public static void OnScriptUnloaded(DOLEvent e, object sender, EventArgs args)
		{
			GameEventMgr.RemoveHandler(GamePlayerEvent.LeaveGroup, OnLeaveGroup);
			GameEventMgr.RemoveHandler(GamePlayerEvent.Quit, OnOwnerGone);
			GameEventMgr.RemoveHandler(GamePlayerEvent.Linkdeath, OnOwnerGone);
		}

		/// <summary>
		/// Un joueur vient de quitter un groupe.
		/// Les suppressions sont faites "un peu plus tard" (RemoveLater) : le serveur est encore en train
		/// de mettre à jour le groupe à ce moment-là.
		/// </summary>
		private static void OnLeaveGroup(DOLEvent e, object sender, EventArgs args)
		{
			if (sender is FakeGamePlayer fake)
			{
				// Un alt quitte le groupe : on le supprime.
				FakePlayerMgr.RemoveLater(fake);
			}
			else if (sender is GamePlayer owner)
			{
				// Le joueur quitte le groupe : tous ses alts partent avec lui.
				foreach (FakeGamePlayer f in FakePlayerMgr.GetFakesOf(owner))
					FakePlayerMgr.RemoveLater(f);
			}
		}

		/// <summary>Un vrai joueur se déconnecte : tous ses alts sont supprimés.</summary>
		private static void OnOwnerGone(DOLEvent e, object sender, EventArgs args)
		{
			if (sender is GamePlayer owner && sender is not FakeGamePlayer)
			{
				foreach (FakeGamePlayer f in FakePlayerMgr.GetFakesOf(owner))
					FakePlayerMgr.RemoveLater(f);
			}
		}
	}
}
