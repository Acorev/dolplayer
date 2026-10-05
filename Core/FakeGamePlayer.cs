/*
 * FakePlayers - Core/FakeGamePlayer.cs
 *
 * L'alt lui-même. C'est un vrai GamePlayer du serveur, construit à partir d'un personnage
 * du compte du joueur (chargé depuis la base) : il a ses vraies stats, son équipement, ses specs...
 * Mais son "client" est un FakeGameClient sans connexion réseau.
 * Il est créé par FakePlayerMgr.Call, jamais directement.
 */

using DOL.Database;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Un alt : un vrai GamePlayer, piloté par le serveur, FIGÉ (jamais sauvegardé en base).
	/// </summary>
	/// <param name="client">Son faux client réseau (FakeGameClient).</param>
	/// <param name="dbChar">Son personnage, chargé depuis la base.</param>
	public class FakeGamePlayer(GameClient client, DOLCharacters dbChar) : GamePlayer(client, dbChar)
	{
		/// <summary>
		/// Le vrai joueur qui a appelé cet alt.
		/// Les alts sont supprimés quand ce joueur se déconnecte (voir Events/FakeGroupWatcher),
		/// donc cette référence désigne toujours un joueur en jeu.
		/// </summary>
		public GamePlayer Owner { get; set; }

		/// <summary>
		/// Jamais de sauvegarde en base (la sauvegarde automatique du serveur passe aussi par ici).
		/// L'alt est figé : son personnage en base n'est jamais modifié (position, expérience...).
		/// Pour qu'il progresse un jour comme un vrai joueur, il suffira de retirer cette méthode.
		/// </summary>
		public override void SaveIntoDatabase() { }
	}
}
