using System.Collections.Generic;
using System.Linq;
using DOL.GS.Realm;

namespace DOL.GS.Scripts
{
    public static class AlbionConst
    {
        public static PlayerRace Race()
        {
            List<PlayerRace> races = ([.. PlayerRace.AllRaces.Where(r => r.Realm == eRealm.Albion)]);
            return races[Util.Random(races.Count - 1)];
        }
    }
}