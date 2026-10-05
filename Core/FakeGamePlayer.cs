/*
 * FakePlayers - Core/FakeGamePlayer.cs
 *
 * Le faux joueur lui-même. C'est un vrai GamePlayer du serveur (il a des stats, peut être groupé,
 * ciblé, etc.), mais son "client" est un FakeGameClient sans connexion réseau.
 * Il est créé par FakePlayerMgr.Spawn, jamais directement.
 */

using DOL.Database;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Un faux joueur : un vrai GamePlayer, piloté par le serveur, qui n'est jamais sauvegardé en base.
	/// </summary>
	/// <param name="client">Son faux client réseau (FakeGameClient).</param>
	/// <param name="dbChar">Son personnage, créé uniquement en mémoire.</param>
	public class FakeGamePlayer(GameClient client, DOLCharacters dbChar) : GamePlayer(client, dbChar)
	{
		/// <summary>
		/// Le vrai joueur qui a créé ce faux joueur (affiché dans /fake list).
		/// Attention : si le créateur se déconnecte, cette référence pointe vers un joueur qui n'est plus en jeu.
		/// </summary>
		public GamePlayer Owner { get; set; }

		/// <summary>
		/// Jamais de sauvegarde en base (la sauvegarde automatique du serveur passe aussi par ici).
		/// Sans ça, le serveur tente un UPDATE sur un personnage qui n'existe pas en base.
		/// </summary>
		public override void SaveIntoDatabase() { }
	}
}
