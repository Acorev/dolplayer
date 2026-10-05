/*
 * FakePlayers - Network/FakePositionSender.cs
 *
 * Problème réglé ici : un vrai joueur reste visible parce que son jeu envoie sans arrêt sa position
 * au serveur, qui la relaie aux autres. Un faux joueur n'a pas de jeu derrière lui :
 * sans ce fichier, il serait déconnecté (plus de ping) ou disparaîtrait de l'écran des autres.
 * Un timer par faux joueur fait donc ce travail à sa place, toutes les INTERVAL_MS millisecondes.
 */

using System;
using System.Reflection;
using DOL.GS.PacketHandler;
using log4net;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Garde un faux joueur "vivant" et visible :
	///  - rafraîchit son PingTime pour que le contrôle de ping du serveur ne le déconnecte pas ;
	///  - diffuse sa position aux vrais joueurs proches.
	/// </summary>
	public static class FakePositionSender
	{
		/// <summary>Journal du serveur (console + fichier de log) pour écrire les erreurs.</summary>
		private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		/// <summary>Intervalle d'envoi en millisecondes (baisser à 500 si le faux joueur "saute" à l'écran).</summary>
		public const int INTERVAL_MS = 1000;

		/// <summary>
		/// Démarre le timer. À appeler APRÈS AddToWorld (le timer utilise la région du faux joueur).
		/// Le timer s'arrête tout seul quand le faux joueur est supprimé ou que son client n'est plus en jeu.
		/// </summary>
		/// <param name="fake">Le faux joueur à garder vivant et visible.</param>
		public static void Start(FakeGamePlayer fake)
		{
			bool errorLogged = false;

			// Le code entre { } est exécuté à chaque tic du timer.
			// La valeur renvoyée est le délai avant le prochain tic (0 = arrêter le timer).
			var timer = new RegionTimer(fake, callingTimer =>
			{
				// Faux joueur supprimé (/fake remove) : on arrête.
				if (fake.ObjectState == GameObject.eObjectState.Deleted || fake.Client.ClientState != GameClient.eClientState.Playing)
					return 0; // 0 = arrêt du timer

				// "Signe de vie" : sans ça, le serveur le déconnecte pour "Ping timeout".
				fake.Client.PingTime = DateTime.Now.Ticks;
				try
				{
					SendToNearbyPlayers(fake);
				}
				catch (Exception ex)
				{
					// On logue une seule fois pour ne pas inonder la console toutes les secondes.
					if (!errorLogged)
					{
						log.Warn("[FakePlayers] erreur d'envoi de position pour " + fake.Name, ex);
						errorLogged = true;
					}
				}
				return INTERVAL_MS;
			});
			timer.Start(INTERVAL_MS);
		}

		/// <summary>
		/// Depuis les clients 1.124+, SendPlayerForgedPosition ne fait plus rien. Un vrai joueur reste visible
		/// parce que son client envoie sa position et que PlayerPositionUpdateHandler la relaie aux autres.
		/// On reproduit ici ce paquet relayé (même format que outpak1124 / outpak1127 du handler).
		/// Les clients plus anciens reçoivent déjà SendPlayerForgedPosition depuis le WorldUpdateThread.
		///
		/// Pour chaque vrai joueur à portée de vue : on construit le paquet "position du joueur"
		/// (position, zone, état assis/mort, cible, vie/mana/endurance) et on le lui envoie.
		/// L'ordre des champs du paquet doit rester exactement celui-ci : c'est le format attendu par le client.
		/// </summary>
		private static void SendToNearbyPlayers(FakeGamePlayer fake)
		{
			Zone zone = fake.CurrentZone;
			if (zone == null || fake.ObjectState != GameObject.eObjectState.Active)
				return;

			// État affiché par le client : debout (0), assis, ou mort.
			ushort state = 0;
			if (!fake.IsAlive)
				state = 5 << 10;
			else if (fake.IsSitting)
				state = 4 << 10;

			// Petits drapeaux d'état (plonge, a une cible, torche allumée, furtif...).
			byte action = 0;
			if (fake.IsDiving)
				action |= 0x04;
			if (fake.TargetInView)
				action |= 0x30;
			if (fake.GroundTargetInView)
				action |= 0x08;
			if (fake.IsTorchLighted)
				action |= 0x80;
			if (fake.IsStealthed)
				action |= 0x02;

			// Pourcentage de vie ; le bit 0x80 indique qu'il est en mode combat.
			byte health = (byte)(fake.HealthPercent + (fake.AttackState ? 0x80 : 0));
			// Clé utilisée par le serveur pour savoir quand chaque client a reçu la dernière mise à jour de cet objet.
			var cacheKey = new Tuple<ushort, ushort>(fake.CurrentRegionID, (ushort)fake.ObjectID);

			foreach (GamePlayer player in fake.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
			{
				// Inutile d'envoyer aux faux joueurs (personne ne regarde leur écran) ni à lui-même.
				if (player == null || player == fake || player is FakeGamePlayer)
					continue;
				// Les vieux clients sont déjà servis par le serveur (voir plus haut).
				if (player.Client.Version < GameClient.eClientVersion.Version1124)
					continue;
				// Pas visible depuis une autre maison, ni s'il est furtif et non détecté.
				if ((fake.InHouse || player.InHouse) && player.CurrentHouse != fake.CurrentHouse)
					continue;
				if (fake.IsStealthed && !player.CanDetect(fake))
					continue;

				// Le format change un peu à partir du client 1.127 (deux champs en plus).
				bool is1127 = player.Client.Version >= GameClient.eClientVersion.Version1127;
				var pak = new GSUDPPacketOut(player.Out.GetPacketCode(eServerPackets.PlayerPosition));
				pak.WriteFloatLowEndian(fake.Position.X);
				pak.WriteFloatLowEndian(fake.Position.Y);
				pak.WriteFloatLowEndian(fake.Position.Z);
				pak.WriteFloatLowEndian(0); // vitesse
				pak.WriteFloatLowEndian(0); // vitesse verticale
				pak.WriteShort((ushort)fake.Client.SessionID);
				if (is1127)
					pak.WriteShort((ushort)fake.ObjectID);
				pak.WriteShort(zone.ZoneSkinID);
				pak.WriteShort(state);
				pak.WriteShort(0); // place sur une monture
				pak.WriteShort(fake.Orientation.InHeading);
				pak.WriteByte(action);
				pak.WriteByte((byte)(fake.RPFlag ? 1 : 0));
				pak.WriteByte(0);
				pak.WriteByte(health);
				pak.WriteByte(fake.ManaPercent);
				pak.WriteByte(fake.EndurancePercent);
				if (is1127)
					pak.WriteShort(0);
				pak.WritePacketLength();

				// Note que ce client vient d'être mis à jour, puis envoie le paquet.
				player.Client.GameObjectUpdateArray[cacheKey] = GameTimer.GetTickCount();
				player.Out.SendUDP(pak);
			}
		}
	}
}
