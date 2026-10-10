using System;
using System.Collections.Generic;
using DOL.Database;

namespace DOL.GS.Scripts
{
    public class GameFake : DOLCharacters
    {
        public GameFake(GamePlayer player, string name)
        {
            base.Name = name;
            AccountName = "fake_" + name.ToLowerInvariant();
            Level = player.Level;
            Xpos = player.Position.X + Util.Random(-100, 100);
            Ypos = player.Position.Y + Util.Random(-100, 100);
            Zpos = player.Position.Z;
            Region = player.CurrentRegionID;
            Direction = player.Position.Orientation.InHeading;
            SetBaseStats(this);
        }

        private static void SetBaseStats(DOLCharacters ch)
        {
            if (!GlobalConstants.STARTING_STATS_DICT.TryGetValue((eRace)ch.Race, out Dictionary<eStat, int> raceStats))
                raceStats = GlobalConstants.STARTING_STATS_DICT[eRace.Unknown];

            CharacterClass cls = CharacterClass.GetClass(ch.Class);
            var stats = new Dictionary<eStat, int>(raceStats);

            void Add(eStat stat, int amount)
            {
                if (stat != eStat.UNDEFINED && stats.ContainsKey(stat))
                    stats[stat] += amount;
            }

            // 30 points de création.
            Add(cls.PrimaryStat, 10);
            Add(cls.SecondaryStat, 10);
            Add(cls.TertiaryStat, 10);

            // Gains de niveau (niveau 6 et plus).
            for (int level = ch.Level; level > 5; level--)
            {
                Add(cls.PrimaryStat, 1);
                if ((level - 6) % 2 == 0) Add(cls.SecondaryStat, 1);
                if ((level - 6) % 3 == 0) Add(cls.TertiaryStat, 1);
            }

            ch.Strength = stats[eStat.STR];
            ch.Constitution = stats[eStat.CON];
            ch.Dexterity = stats[eStat.DEX];
            ch.Quickness = stats[eStat.QUI];
            ch.Intelligence = stats[eStat.INT];
            ch.Piety = stats[eStat.PIE];
            ch.Empathy = stats[eStat.EMP];
            ch.Charisma = stats[eStat.CHR];
        }

        public static void SetCharacter(DOLCharacters ch, CharacterClass Character, eRealm realm)
        {
            var Genre = Util.Chance(50) ? eGender.Male : eGender.Female;
            var possibleRaces = Character.EligibleRaces.FindAll(s => s.GetModel(Genre) != eLivingModel.None);
            if (possibleRaces.Count > 0)
            {
                var indexPick = Util.Random(0, possibleRaces.Count - 1);
                var Model = (ushort)possibleRaces[indexPick].GetModel(Genre);

                ch.Class = Character.ID;
                ch.Realm = (int)realm;
                ch.Gender = (int)Genre - 1;
                ch.Race = indexPick;
                ch.CreationModel = Model;
                ch.CurrentModel = Model;
            }
        }
    }
}