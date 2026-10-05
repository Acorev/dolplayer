
using DOL.Database;

namespace DOL.GS.Scripts.fakeplayers
{
    /// <summary>
	/// Le FakePlayer est un vrai GamePlayer qui n'est jamais sauvegardé.
	/// </summary>
    public class FakeGamePlayer(GameClient client, DOLCharacters dbChar) : GamePlayer(client, dbChar)
    {
        public GamePlayer Owner { get; set; }


        /// <summary>
        /// Jamais de sauvegarde en base (la sauvegarde automatique du serveur passe aussi par ici).
        /// </summary>
        public override void SaveIntoDatabase() { }
    }
}
