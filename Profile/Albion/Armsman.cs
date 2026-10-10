

using System.Reflection;
using log4net;

namespace DOL.GS.Scripts
{
    public class Armsman : GameFake
    {
        public Armsman(GamePlayer player, string name) : base(player, name)
        {
            SetCharacter(this, CharacterClass.Armsman, eRealm.Albion);
        }
    }
}