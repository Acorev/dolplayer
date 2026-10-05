

using System;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS.Scripts.fakeplayers
{
    public static class FakePlayerMgr
    {
        public static FakeGamePlayer Spawn(GamePlayer owner, string name, out string error)
        {
            // Le personnage, uniquement en mémoire.
            var dbChar = new DOLCharacters
            {
                Name = name,
                AccountName = "fakeplayer_" + name.ToLower(),
                Realm = (int)owner.Realm,
                Race = owner.Race,
                Gender = (int)owner.Gender,
                Class = owner.CharacterClass.ID,
                Level = owner.Level,
                CreationModel = owner.Model,
                CurrentModel = owner.Model,
                Strength = owner.Strength,
                Constitution = owner.Constitution,
                Dexterity = owner.Dexterity,
                Quickness = owner.Quickness,
                Intelligence = owner.Intelligence,
                Piety = owner.Piety,
                Empathy = owner.Empathy,
                Charisma = owner.Charisma,
                Region = owner.CurrentRegionID,
                Xpos = owner.Position.X,
                Ypos = owner.Position.Y,
                Zpos = owner.Position.Z,
                Direction = owner.Position.Orientation.InHeading,
            };
            // Le compte, uniquement en mémoire.
            var account = new Account
            {
                Name = dbChar.AccountName,
                PrivLevel = (int)ePrivLevel.Player,
                Language = owner.Client.Account.Language,
                Characters = [dbChar],
            };

            // Création d'une session ID
            var client = new FakeGameClient(account);
            if (WorldMgr.CreateSessionID(client) < 0)
            {
                error = ("le serveur est plein (plus de numéro de session libre)");
                return null;
            }

            FakeGamePlayer fake;
            try
            {
                fake = new FakeGamePlayer(client, dbChar) { Owner = owner };
                client.Player = fake;
                client.ClientState = GameClient.eClientState.Playing;

                StartKeepAlive(fake);

                if (!fake.AddToWorld())
                {
                    error = "impossible d'ajouter le joueur au monde ici";
                    WorldMgr.RemoveClient(client);
                    client.ClientState = GameClient.eClientState.Disconnected;
                    return null;
                }
            }
            catch (Exception ex)
            {
                error = "erreur à la création : " + ex.Message;
                WorldMgr.RemoveClient(client);
                client.ClientState = GameClient.eClientState.Disconnected;
                return null;
            }

            error = null;
            return fake;
        }

        /// <summary>
		/// Garde la session "vivante" pour que le contrôle de ping du serveur ne la signale pas.
		/// </summary>
		private static void StartKeepAlive(FakeGamePlayer fake)
        {
            var timer = new RegionTimer(fake, callingTimer =>
            {
                if (fake.ObjectState == GameObject.eObjectState.Deleted || fake.Client.ClientState != GameClient.eClientState.Playing)
                    return 0;
                fake.Client.PingTime = DateTime.Now.Ticks;
                try
                {
                    SendPositionToNearbyPlayers(fake);
                }
                catch (Exception)
                {
                }
                return 1000;
            });
            timer.Start(1000);
        }

        /// <summary>
        /// Depuis les clients 1.124+, SendPlayerForgedPosition ne fait plus rien. Un vrai joueur reste visible
        /// parce que son client envoie sa position et que PlayerPositionUpdateHandler la relaie aux autres.
        /// On reproduit ici ce paquet relayé (même format que outpak1124 / outpak1127 du handler).
        /// Les clients plus anciens reçoivent déjà SendPlayerForgedPosition depuis le WorldUpdateThread.
        /// </summary>
        private static void SendPositionToNearbyPlayers(FakeGamePlayer fake)
        {
            Zone zone = fake.CurrentZone;
            if (zone == null || fake.ObjectState != GameObject.eObjectState.Active)
                return;

            ushort state = 0;
            if (!fake.IsAlive)
                state = 5 << 10;
            else if (fake.IsSitting)
                state = 4 << 10;

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

            byte health = (byte)(fake.HealthPercent + (fake.AttackState ? 0x80 : 0));
            var cacheKey = new Tuple<ushort, ushort>(fake.CurrentRegionID, (ushort)fake.ObjectID);

            foreach (GamePlayer player in fake.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
            {
                if (player == null || player == fake || player is FakeGamePlayer)
                    continue;
                if (player.Client.Version < GameClient.eClientVersion.Version1124)
                    continue;
                if ((fake.InHouse || player.InHouse) && player.CurrentHouse != fake.CurrentHouse)
                    continue;
                if (fake.IsStealthed && !player.CanDetect(fake))
                    continue;

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

                player.Client.GameObjectUpdateArray[cacheKey] = GameTimer.GetTickCount();
                player.Out.SendUDP(pak);
            }
        }
    }
}