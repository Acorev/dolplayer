
namespace DOL.GS.Scripts
{
    public class FakeArmsman : FakeCharacters
    {
        public FakeArmsman(GamePlayer player, string name) : base(player, name)
        {
            SetCharacter(this, CharacterClass.Armsman, eRealm.Albion);
        }
    }
}