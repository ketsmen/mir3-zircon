using Library;
using Server.Envir;
using System.Drawing;
using S = Library.Network.ServerPackets;

namespace Server.Models.Monsters
{
    public class ArachnidGrazer : MonsterObject
    {
        public override bool CanMove => false;
        public int MinionsCount = 1;

        protected override bool InAttackRange()
        {
            return Target.CurrentMap == CurrentMap && Functions.InRange(CurrentLocation, Target.CurrentLocation, ViewRange);
        }


        public ArachnidGrazer()
        {
            Direction = (MirDirection)SEnvir.Random.Next(3);
        }

        public override void ProcessAction(DelayedAction action)
        {
            switch (action.Type)
            {
                case ActionType.Function:
                    SpawnMinions(MinionsCount, 0, Target);
                    break;
            }

            base.ProcessAction(action);
        }

        protected override void Attack()
        {
            Broadcast(new S.ObjectAttack { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation });

            UpdateAttackTime();

            ActionList.Add(new DelayedAction(
                               SEnvir.Now.AddMilliseconds(600),
                               ActionType.Function));
        }


        public override bool SpawnMinion(MonsterObject mob)
        {
            if (mob == null || CurrentMap == null) return false;

            Point preferredLocation = Functions.Move(CurrentLocation, Functions.ShiftDirection(Direction, 3));
            Cell preferredCell = CurrentMap.GetCell(preferredLocation);

            if (CanSpawnAt(preferredCell, mob))
                return mob.Spawn(CurrentMap, preferredCell.Location);

            foreach (Cell cell in CurrentMap.GetCells(CurrentLocation, 1, 3, true))
            {
                if (!CanSpawnAt(cell, mob)) continue;

                return mob.Spawn(CurrentMap, cell.Location);
            }

            return false;
        }

        private static bool CanSpawnAt(Cell cell, MonsterObject mob)
        {
            return cell != null && cell.Movements == null && !cell.IsBlocking(mob, false);
        }
    }
}
