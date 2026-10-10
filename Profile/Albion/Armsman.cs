namespace DOL.GS.Scripts
{
    public class Armsman : GameFake
    {
        protected override int FakeRace => (int)eRace.Highlander;
        protected override int FakeGender => (int)eGender.Male;
        protected override int FakeClass => (int)eCharacterClass.Armsman;
        protected override int FakeModel => (int)eLivingModel.HighlanderMale;
        public Armsman(GamePlayer player, string name) : base(player, name)
        {

        }
    }
}