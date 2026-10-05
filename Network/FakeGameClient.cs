/*
 * FakePlayers - Network/FakeGameClient.cs
 *
 * Pour le serveur, chaque joueur est relié à un GameClient (sa connexion réseau).
 * Un faux joueur n'a pas de vraie connexion : FakeGameClient fait semblant d'en être une.
 * Tout ce que le serveur essaie de lui envoyer est jeté, par deux chemins :
 *   - Out (NullPacketLib) : la voie normale des envois ;
 *   - PacketProcessor (fermé dès la création) : la voie directe, utilisée par certaines méthodes du serveur.
 */

using System;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Le client du faux joueur : aucune connexion réseau, tous les paquets envoyés sont ignorés.
	/// </summary>
	public class FakeGameClient : GameClient
	{
		/// <summary>
		/// Prépare un faux client prêt à l'emploi.
		/// Il reste ensuite à lui donner un numéro de session (WorldMgr.CreateSessionID) et un joueur :
		/// c'est FakePlayerMgr.Spawn qui s'en charge.
		/// </summary>
		/// <param name="account">Le compte du faux joueur, créé uniquement en mémoire.</param>
		public FakeGameClient(Account account)
			: base(GameServer.Instance) // et non null : le PacketProcessor a besoin du serveur
		{
			Account = account;
			Out = new NullPacketLib();      // tous les envois "normaux" ne font rien
			PingTime = DateTime.Now.Ticks;  // dernier "signe de vie" ; rafraîchi ensuite par FakePositionSender

			// Le serveur appelle parfois directement Client.PacketProcessor, sans passer par Out
			// (ex. SendWarlockChamberEffect quand un joueur apparaît à côté d'un faux joueur).
			// On en crée un, puis on le "ferme" tout de suite : son tampon est rendu au serveur
			// et tous ses envois sont ensuite ignorés (SendTCP sort si le tampon est vide ;
			// les envois UDP retombent sur SendTCP).
			PacketProcessor = new PacketProcessor(this);
			PacketProcessor.OnDisconnect();

			// Rend aussi le tampon de réception pris par BaseClient : le faux client ne reçoit rien.
			// Pas de socket, donc cet appel ne fait que libérer le tampon.
			CloseConnections();
		}
	}
}
