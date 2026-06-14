using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Library;
using Library.SystemModels;
using Server.Envir;

namespace Server.Models.Monsters
{
    public class KingsSpawner : MonsterObject
    {
        public MonsterInfo MonsterSpawnInfo;
        public int NumberToSpawn;
        public int MaxTotalSpawn = 12;

        public override void Die()
        {
            if(MonsterSpawnInfo != null && EXPOwner != null)
            {
                int spawned = 0;

                if (EXPOwner.GroupMembers != null)
                {
                    foreach (PlayerObject ob in EXPOwner.GroupMembers)
                    {
                        if (ob.CurrentMap != CurrentMap || !Functions.InRange(ob.CurrentLocation, CurrentLocation, Config.MaxViewRange)) continue;

                        for (int i = 0; i < NumberToSpawn && spawned < MaxTotalSpawn; i++)
                        {
                            if (SpawnMonsterNear(ob.CurrentLocation))
                                spawned++;
                        }

                        if (spawned >= MaxTotalSpawn) break;
                    }
                }
                else
                {
                    for (int i = 0; i < NumberToSpawn && spawned < MaxTotalSpawn; i++)
                    {
                        if (SpawnMonsterNear(EXPOwner.CurrentLocation))
                            spawned++;
                    }
                }
            }

            base.Die();
        }

        private bool SpawnMonsterNear(Point origin)
        {
            MonsterObject mob = GetMonster(MonsterSpawnInfo);
            if (mob == null) return false;

            foreach (Cell cell in CurrentMap.GetCells(origin, 0, 3, true))
            {
                if (cell.Movements != null || cell.IsBlocking(mob, false)) continue;

                return mob.Spawn(CurrentMap, cell.Location);
            }

            return false;
        }
    }
}
