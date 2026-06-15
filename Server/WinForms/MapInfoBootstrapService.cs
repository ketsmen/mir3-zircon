using Library;
using Library.SystemModels;
using MirDB;

namespace Server.WinForms
{
    public static class MapInfoBootstrapService
    {
        public static int MigrateLegacyBuffStats(Session session, bool saveChanges)
        {
            int migrated = 0;

            foreach (MapInfo map in session.GetCollection<MapInfo>().Binding)
            {
                int mapMigrated = 0;
                mapMigrated += MoveLegacyStat(session, map, Stat.MonsterHealth, map.MonsterHealth, value => map.MonsterHealth = value);
                mapMigrated += MoveLegacyStat(session, map, Stat.MonsterDamage, map.MonsterDamage, value => map.MonsterDamage = value);
                mapMigrated += MoveLegacyStat(session, map, Stat.MonsterDrop, map.DropRate, value => map.DropRate = value);
                mapMigrated += MoveLegacyStat(session, map, Stat.MonsterExperience, map.ExperienceRate, value => map.ExperienceRate = value);
                mapMigrated += MoveLegacyStat(session, map, Stat.MonsterGold, map.GoldRate, value => map.GoldRate = value);
                mapMigrated += MoveLegacyStat(session, map, Stat.MaxMonsterHealth, map.MaxMonsterHealth, value => map.MaxMonsterHealth = value);
                mapMigrated += MoveLegacyStat(session, map, Stat.MaxMonsterDamage, map.MaxMonsterDamage, value => map.MaxMonsterDamage = value);
                mapMigrated += MoveLegacyStat(session, map, Stat.MaxMonsterDrop, map.MaxDropRate, value => map.MaxDropRate = value);
                mapMigrated += MoveLegacyStat(session, map, Stat.MaxMonsterExperience, map.MaxExperienceRate, value => map.MaxExperienceRate = value);
                mapMigrated += MoveLegacyStat(session, map, Stat.MaxMonsterGold, map.MaxGoldRate, value => map.MaxGoldRate = value);

                if (mapMigrated > 0)
                    map.StatsChanged();

                migrated += mapMigrated;
            }

            if (migrated > 0 && saveChanges)
                session.Save(true);

            return migrated;
        }

        private static int MoveLegacyStat(Session session, MapInfo map, Stat statType, int amount, System.Action<int> clear)
        {
            if (amount == 0) return 0;

            MapInfoStat stat = session.GetCollection<MapInfoStat>().CreateNewObject();
            stat.Stat = statType;
            stat.Amount = amount;
            map.BuffStats.Add(stat);
            clear(0);
            return 1;
        }
    }
}
