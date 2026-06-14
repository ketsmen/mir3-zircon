using System;
using System.Collections.Generic;
using Library;
using Library.SystemModels;
using Server.Envir;
using S = Library.Network.ServerPackets;

namespace Server.Models.Monsters
{
    public class HellBringer : MonsterObject
    {
        public int AttackRange = 8;
        private bool BatsSpawned;
        public MonsterInfo BatInfo;

        protected override bool InAttackRange()
        {
            if (Target.CurrentMap != CurrentMap) return false;
            if (Target.CurrentLocation == CurrentLocation) return false;

            return Functions.InRange(CurrentLocation, Target.CurrentLocation, AttackRange);
        }

        public override void ProcessTarget()
        {
            if (Target == null) return;

            if (InAttackRange())
            {
                if (CanAttack)
                    Attack();
                return;
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
            else if (!Functions.InRange(CurrentLocation, Target.CurrentLocation, 1))
                MoveTo(Target.CurrentLocation);

            if (InAttackRange() && CanAttack)
                Attack();
        }

        protected override void Attack()
        {
            Direction = Functions.DirectionFromPoint(CurrentLocation, Target.CurrentLocation);
            UpdateAttackTime();

            if (!BatsSpawned && CurrentHP <= Stats[Stat.Health] / 4)
            {
                Broadcast(new S.ObjectMagic { ObjectID = ObjectID, Direction = Direction, CurrentLocation = CurrentLocation, Cast = true, Type = MagicType.HellBringerBats });
                BatsSpawned = true;
                if (BatInfo == null) return;

                int count = Math.Min(14, MaxMinions - MinionList.Count);

                for (int i = 0; i < count; i++)
                {
                    MonsterObject mob = GetMonster(BatInfo);
                    if (mob == null) continue;

                    if (!SpawnMinion(mob)) continue;

                    mob.Target = Target;
                    mob.Master = this;
                    MinionList.Add(mob);
                }
                return;
            }

            if (SEnvir.Random.Next(3) == 0 || !Functions.InRange(Target.CurrentLocation, CurrentLocation, 2))
                RangeAttack();
            else
            {
                Broadcast(new S.ObjectAttack { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation }); //Animation ?

                foreach (MapObject ob in GetTargets(CurrentMap, Functions.Move(CurrentLocation, Direction), 1))
                {
                    ActionList.Add(new DelayedAction(
                        SEnvir.Now.AddMilliseconds(400),
                        ActionType.DelayAttack,
                        ob,
                        GetDC(),
                        AttackElement));
                    break;
                }
            }
        }

        public override bool SpawnMinion(MonsterObject mob)
        {
            if (mob == null || CurrentMap == null) return false;

            foreach (Cell cell in CurrentMap.GetCells(CurrentLocation, 1, AttackRange, true))
            {
                if (cell.Movements != null || cell.IsBlocking(mob, false)) continue;

                return mob.Spawn(CurrentMap, cell.Location);
            }

            return false;
        }

        private void RangeAttack()
        {
            Direction = Functions.DirectionFromPoint(CurrentLocation, Target.CurrentLocation);
            switch (SEnvir.Random.Next(3))
            {
                case 0:
                    List<uint> targetIDs = new List<uint>();
                    List<MapObject> targets = GetTargets(CurrentMap, CurrentLocation, AttackRange);

                    if (targets.Count > 0)
                    {
                        foreach (MapObject ob in targets)
                        {
                            if (SEnvir.Random.Next(2) > 0) continue;

                            ob.ApplyPoison(new Poison
                            {
                                Owner = this,
                                Type = PoisonType.Silenced,
                                TickFrequency = TimeSpan.FromSeconds(10),
                                TickCount = 1,
                            });

                            targetIDs.Add(ob.ObjectID);
                        }
                    }

                    Broadcast(new S.ObjectMagic { ObjectID = ObjectID, Direction = Direction, CurrentLocation = CurrentLocation, Cast = true, Type = MagicType.None, Targets = targetIDs });
                    break;
                default:
                    Broadcast(new S.ObjectRangeAttack { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation, Targets = new List<uint> { Target.ObjectID } });
                    foreach (MapObject ob in GetTargets(CurrentMap, Target.CurrentLocation, 2))
                        ActionList.Add(new DelayedAction(
                                       SEnvir.Now.AddMilliseconds(400),
                                       ActionType.DelayAttack,
                                       ob,
                                       GetMC(),
                                       AttackElement));
                    break;
            }
        }
    }
}
