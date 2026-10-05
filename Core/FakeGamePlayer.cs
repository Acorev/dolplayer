/*
 * FakePlayers - Core/FakeGamePlayer.cs
 *
 * L'alt lui-même. C'est un vrai GamePlayer du serveur, construit à partir d'un personnage
 * du compte du joueur (chargé depuis la base) : il a ses vraies stats, son équipement, ses specs...
 * Mais son "client" est un FakeGameClient sans connexion réseau.
 * Il est créé par FakePlayerMgr.Call, jamais directement.
 */

using DOL.Database;
using DOL.GS.Geometry;

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
		/// true = l'alt suit son propriétaire (par défaut) ; false = il reste sur place (/fake stay).
		/// Utilisé par Movement/FakeFollowAction.
		/// </summary>
		public bool IsFollowing { get; set; } = true;

		/// <summary>
		/// Jamais de sauvegarde en base (la sauvegarde automatique du serveur passe aussi par ici).
		/// L'alt est figé : son personnage en base n'est jamais modifié (position, expérience...).
		/// Pour qu'il progresse un jour comme un vrai joueur, il suffira de retirer cette méthode.
		/// </summary>
		public override void SaveIntoDatabase() { }

		// ================================================================= déplacement
		// Un être vivant de DOLSharp a un "mouvement" (Motion) : point de départ, destination, vitesse.
		// Sa position est calculée en continu à partir de ce mouvement : l'alt avance donc tout seul,
		// de façon fluide, entre deux tics de FakeFollowAction, et s'arrête pile sur la destination.

		/// <summary>Fait marcher l'alt en ligne droite vers un point, à cette vitesse.</summary>
		public void WalkTowards(Coordinate destination, short speed)
		{
			Motion = DOL.GS.Geometry.Motion.Create(Position, destination, speed);
		}

		/// <summary>Arrête l'alt là où il se trouve.</summary>
		public void StopWalking()
		{
			Motion = DOL.GS.Geometry.Motion.Create(Position, Coordinate.Nowhere, 0);
		}

		/// <summary>La destination du mouvement en cours (Coordinate.Nowhere à l'arrêt).</summary>
		public Coordinate WalkDestination => Motion.Destination;

		/// <summary>true si l'alt est en train de marcher.</summary>
		public bool IsWalking => Motion.Speed != 0 && Motion.Destination != Coordinate.Nowhere;
	}
}
