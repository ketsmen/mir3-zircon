using System;
using System.Collections.Generic;
using System.Drawing;
using Library;
using Library.SystemModels;
using Server.DBModels;
using Server.Envir;
using S = Library.Network.ServerPackets;

namespace Server.Models.Monsters
{
    public class DemonicLunatic : MonsterObject
    {
        public List<MonsterObject> minions = new List<MonsterObject>();
        public int count = 9;
        public MonsterInfo MonsterSpawnInfo;
        private DateTime NextSwapTime;

        public DemonicLunatic()
        {
            AvoidFireWall = false;
            minions.Add(this);
        }

        protected override bool InAttackRange()
        {
            if (Target.CurrentMap != CurrentMap) return false;

            return Target.CurrentLocation != CurrentLocation && Functions.InRange(CurrentLocation, Target.CurrentLocation, ViewRange);
        }

        public bool SpawnMinion(MonsterObject mob, MonsterObject owner)
        {
            DemonicLunaticMirror mirror = mob as DemonicLunaticMirror;
            if (mirror == null) return false;

            foreach (Cell cell in CurrentMap.GetCells(CurrentLocation, 1, 6, true))
            {
                if (cell.Movements != null || cell.IsBlocking(mirror, false)) continue;

                if (!mirror.Spawn(CurrentMap, cell.Location)) return false;

                mirror.Owner = this;
                return true;
            }

            return false;
        }

        public override void ProcessTarget()
        {
            if (Target == null) return;
            if (minions.Count == 1)
            {
                for (int i = 0; i < count; i++)
                {
                    MonsterObject monster = GetMonster(MonsterSpawnInfo);
                    if (monster == null)
                    {
                        break;
                    }
                    if(SpawnMinion(monster, this))
                    {
                        minions.Add(monster);
                    }
                }
                for(int i = 0; i < count * 2 && minions.Count > 1; i++)
                {
                    SwapRandomLocations(false);
                }
            }

            if (minions.Count > 1 && SEnvir.Now >= NextSwapTime && (CanAttack || CanMove))
            {
                if (SEnvir.Random.Next(10) == 0)
                {
                    SwapRandomLocations(true);
                    NextSwapTime = SEnvir.Now.AddSeconds(1);
                }
            }

            if (InAttackRange())
            {
                if (CanAttack)
                {
                    Attack();
                }
            }
            if (CurrentLocation == Target.CurrentLocation)
            {
                MirDirection direction = (MirDirection)SEnvir.Random.Next(8);
                int rotation = SEnvir.Random.Next(2) == 0 ? 1 : -1;

                for (int d = 0; d < 8; d++)
                {
                    if (Walk(direction)) break;

                    direction = Functions.ShiftDirection(direction, rotation);
                }
            }
            else
                MoveTo(Target.CurrentLocation);
        }

        public override void Die()
        {
            MonsterObject[] currentMinions = minions.ToArray();

            for(int i = currentMinions.Length - 1; i >= 0; i--)
            {
                if(currentMinions[i] != this)
                {
                    currentMinions[i].EXPOwner = null;
                    currentMinions[i].Die();
                }
            }
            base.Die();
        }

        private void SwapRandomLocations(bool leaveEffect)
        {
            if (minions.Count < 2) return;

            int first = SEnvir.Random.Next(minions.Count);
            int second = 0;
            do
            {
                second = SEnvir.Random.Next(minions.Count);
            } while (second == first);
            Point firstPoint = minions[first].CurrentLocation;
            Point secondPoint = minions[second].CurrentLocation;
            minions[first].Teleport(CurrentMap, secondPoint, leaveEffect);
            minions[second].Teleport(CurrentMap, firstPoint, leaveEffect);
        }
    }
}
