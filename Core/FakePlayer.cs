using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using DOL.Database;
using log4net;

namespace DOL.GS.Scripts
{
    public static class FakePlayer
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
		/// Tous les faux joueurs du serveur.
		/// Toujours y accéder sous verrou (_lock) : la commande et les timers du serveur
		/// peuvent tourner en même temps sur des threads différents.
		/// </summary>
		private static readonly List<FakeGamePlayer> _fakes = [];

        /// <summary>Verrou qui protège la liste _fakes.</summary>
		private static readonly Lock _lock = new();

        public static FakeGamePlayer SpawnFake(GamePlayer owner, DOLCharacters dbChar)
        {
            // Personnage, compte et faux client, uniquement en mémoire
            var client = new FakeGameClient(BuildAccount(owner, dbChar));


            // Numéro de session : nécessaire pour que le serveur traite le faux client comme un vrai.
            if (WorldMgr.CreateSessionID(client) < 0)
            {
                log.Error("le serveur est plein (plus de numéro de session libre)");
                return null;
            }

            // Le faux joueur lui-même, ajouté au monde
            FakeGamePlayer fake;
            try
            {
                fake = new FakeGamePlayer(client, dbChar) { Owner = owner };
                client.Player = fake;
                client.ClientState = GameClient.eClientState.Playing; // le serveur le considère "en jeu"

                if (!fake.AddToWorld())
                {
                    log.Error("impossible d'ajouter le joueur au monde ici");
                    ReleaseClient(client);
                    return null;
                }
            }
            catch (Exception ex)
            {
                // La trace complète part dans la console du serveur, le joueur reçoit un message court.
                log.Error("[FakePlayers] erreur à la création de " + ex);
                ReleaseClient(client);
                return null;
            }
            // Démarré seulement maintenant : le faux joueur est dans le monde, sa région est connue.
            FakePositionSender.Start(fake);
            lock (_lock)
                _fakes.Add(fake);

            return fake;
        }

        // ================================================================= recherche

        /// <summary>
        /// Liste de tous les faux joueurs encore en jeu.
        /// Retourne une copie : on peut la parcourir (et supprimer des faux joueurs pendant le parcours)
        /// sans risque. Les faux joueurs disparus entre-temps sont retirés au passage.
        /// </summary>
        public static List<FakeGamePlayer> GetFakes()
        {
            lock (_lock)
            {
                _fakes.RemoveAll(f => f.ObjectState == GameObject.eObjectState.Deleted);
                return _fakes.ToList();
            }
        }

        // ================================================================= suppression

        /// <summary>
		/// Supprime un faux joueur : le sort de son groupe, le retire du monde, libère son numéro de session.
		/// </summary>
		/// <returns>false s'il y a eu une erreur (détail dans la console du serveur).</returns>
		public static bool Remove(FakeGamePlayer fake)
        {
            if (fake == null)
                return false;

            lock (_lock)
                _fakes.Remove(fake);

            try
            {
                // Arrête le timer de position (il teste cet état à chaque passage).
                fake.Client.ClientState = GameClient.eClientState.Disconnected;

                // Sortir du groupe AVANT de quitter le monde, pour que la fenêtre de groupe se mette à jour.
                fake.Group?.RemoveMember(fake);

                if (fake.ObjectState != GameObject.eObjectState.Deleted)
                    fake.Delete(); // retire du monde : les vrais joueurs voient le faux joueur disparaître

                // Rend le numéro de session au serveur (il pourra resservir).
                WorldMgr.RemoveClient(fake.Client);
                return true;
            }
            catch (Exception ex)
            {
                log.Error("[FakePlayers] erreur à la suppression de " + fake.Name, ex);
                return false;
            }
        }

        /// <summary>Supprime tous les faux joueurs du serveur.</summary>
        /// <returns>Le nombre de faux joueurs supprimés sans erreur.</returns>
        public static int RemoveAll()
        {
            int count = 0;
            foreach (FakeGamePlayer fake in GetFakes()) // GetFakes renvoie une copie : on peut supprimer pendant la boucle
            {
                if (Remove(fake))
                    count++;
            }
            return count;
        }

        // ================================================================= outils internes

        /// <summary>Libère le numéro de session d'un faux client dont la création a échoué.</summary>
        private static void ReleaseClient(FakeGameClient client)
        {
            WorldMgr.RemoveClient(client);
            client.ClientState = GameClient.eClientState.Disconnected;
        }

        /// <summary>
		/// Fabrique le compte du faux joueur, uniquement en mémoire.
		/// Niveau de droits "joueur" et même langue que le propriétaire.
		/// </summary>
		private static Account BuildAccount(GamePlayer owner, DOLCharacters dbChar)
        {
            return new Account
            {
                Name = dbChar.AccountName,
                PrivLevel = (int)ePrivLevel.Player,
                Language = owner.Client.Account.Language,
                Characters = [dbChar],
            };
        }
    }
}
