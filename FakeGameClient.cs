
using System;
using DOL.Database;
using DOL.GS.Scripts.FakePlayers;

namespace DOL.GS.Scripts.fakeplayers
{
	/// <summary>
	/// Le fake-Client du fake-Player à aucune connexion, tous les paquets envoyés sont ignorés.
	/// </summary>
	public class FakeGameClient : GameClient
	{
		public FakeGameClient(Account account)
			: base(null)
		{
			Account = account;
			Out = new NullPacketLib();
			PingTime = DateTime.Now.Ticks;
		}
	}
}