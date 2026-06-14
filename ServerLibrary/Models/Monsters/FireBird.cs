using System;
using System.Collections.Generic;
using System.Drawing;
using Library;
using Server.Envir;
using S = Library.Network.ServerPackets;

namespace Server.Models.Monsters
{
    public class FireBird : MonsterObject
    {
        public int AttackRange = 10;

        protected override bool InAttackRange()
        {
            if (Target.CurrentMap != CurrentMap) return false;
            if (Target.CurrentLocation == CurrentLocation) return false;

            return Functions.InRange(CurrentLocation, Target.CurrentLocation, AttackRange);
        }

        public override void ProcessTarget()
        {
            if (Target == null) return;

            if (InAttackRange() && CanAttack)
            {
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
            else if (!Functions.InRange(CurrentLocation, Target.CurrentLocation, 2))
                MoveTo(Target.CurrentLocation);
        }

        public override void ProcessAction(DelayedAction action)
        {
            switch (action.Type)
            {
                case ActionType.RangeAttack:
                    if (action.Data.Length == 0)
                        ScorchedEarthBurst();
                    else
                        ScorchedEarth((MirDirection)action.Data[0]);
                    return;
            }

            base.ProcessAction(action);
        }

        protected override void Attack()
        {
            Direction = Functions.DirectionFromPoint(CurrentLocation, Target.CurrentLocation);
            if (SEnvir.Random.Next(6) == 0 || !Functions.InRange(Target.CurrentLocation, CurrentLocation, 2))
            {
                RangeAttack();
                return;
            }

            UpdateAttackTime();

            Broadcast(new S.ObjectAttack { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation }); //Animation ?

            foreach (MapObject ob in GetTargets(CurrentMap, Functions.Move(CurrentLocation, Direction, 2), 1))
            {
                ActionList.Add(new DelayedAction(
                    SEnvir.Now.AddMilliseconds(400),
                    ActionType.DelayAttack,
                    ob,
                    GetDC(),
                    AttackElement));
            }
        }

        private void RangeAttack()
        {
            switch (SEnvir.Random.Next(2))
            {
                case 0:
                    CycloneAttack(MagicType.IgyuCyclone, 45);
                    break;
                default:
                    ActionList.Add(new DelayedAction(
                           SEnvir.Now.AddMilliseconds(500),
                           ActionType.RangeAttack));
                    break;
            }
        }

        private void ScorchedEarth(MirDirection direction)
        {
            if (Dead) return;

            LineAoE(10, 0, 0, MagicType.IgyuScorchedEarth, Element.Fire, direction);
        }

        private void ScorchedEarthBurst()
        {
            if (Dead) return;

            List<uint> targetIDs = new List<uint>();
            List<Point> locations = new List<Point>();
            HashSet<Point> locationSet = new HashSet<Point>();
            HashSet<MapObject> hitTargets = new HashSet<MapObject>();

            foreach (MirDirection direction in Enum.GetValues(typeof(MirDirection)))
                AddLineCells(10, direction, Element.Fire, targetIDs, locations, locationSet, hitTargets);

            Broadcast(new S.ObjectMagic { ObjectID = ObjectID, Direction = Direction, CurrentLocation = CurrentLocation, Cast = true, Type = MagicType.IgyuScorchedEarth, Targets = targetIDs, Locations = locations });

            UpdateAttackTime();
        }

        public void LineAoE(int distance, int min, int max, MagicType magic, Element element, MirDirection dir)
        {
            List<uint> targetIDs = new List<uint>();
            List<Point> locations = new List<Point>();
            HashSet<Point> locationSet = new HashSet<Point>();
            HashSet<MapObject> hitTargets = new HashSet<MapObject>();

            for (int d = min; d <= max; d++)
            {
                MirDirection direction = Functions.ShiftDirection(dir, d);

                AddLineCells(distance, direction, element, targetIDs, locations, locationSet, hitTargets);
            }

            Broadcast(new S.ObjectMagic { ObjectID = ObjectID, Direction = dir, CurrentLocation = CurrentLocation, Cast = true, Type = magic, Targets = targetIDs, Locations = locations });

            UpdateAttackTime();
        }

        private void CycloneAttack(MagicType type, int chance)
        {
            Direction = Functions.DirectionFromPoint(CurrentLocation, Target.CurrentLocation);

            List<uint> targetIDs = new List<uint>();
            List<Point> locations = new List<Point>();

            foreach (MapObject ob in GetTargets(CurrentMap, CurrentLocation, AttackRange))
            {
                if (SEnvir.Random.Next(4) == 0) continue;

                targetIDs.Add(ob.ObjectID);

                ActionList.Add(new DelayedAction(
                    SEnvir.Now.AddMilliseconds(500),
                    ActionType.DelayAttack,
                    ob,
                    GetDC(),
                    Element.Wind));
            }

            foreach (Cell cell in CurrentMap.GetCells(CurrentLocation, 0, AttackRange))
            {
                if (cell.Objects != null) continue;
                if (SEnvir.Random.Next(chance) != 0) continue;

                locations.Add(cell.Location);
            }

            Broadcast(new S.ObjectMagic { ObjectID = ObjectID, Direction = Direction, CurrentLocation = CurrentLocation, Cast = true, Type = type, Targets = targetIDs, Locations = locations });

            UpdateAttackTime();
        }

        private void AddLineCells(int distance, MirDirection direction, Element element, List<uint> targetIDs, List<Point> locations, HashSet<Point> locationSet, HashSet<MapObject> hitTargets)
        {
            for (int i = 1; i <= distance; i++)
            {
                Point location = Functions.Move(CurrentLocation, direction, i);
                int delay = 500 + i * 75;

                AddAttackCell(CurrentMap.GetCell(location), delay, element, targetIDs, locations, locationSet, hitTargets);

                switch (direction)
                {
                    case MirDirection.Up:
                    case MirDirection.Right:
                    case MirDirection.Down:
                    case MirDirection.Left:
                        AddAttackCell(CurrentMap.GetCell(Functions.Move(location, Functions.ShiftDirection(direction, -2))), delay, element, targetIDs, locations, locationSet, hitTargets);
                        AddAttackCell(CurrentMap.GetCell(Functions.Move(location, Functions.ShiftDirection(direction, 2))), delay, element, targetIDs, locations, locationSet, hitTargets);
                        break;
                    case MirDirection.UpRight:
                    case MirDirection.DownRight:
                    case MirDirection.DownLeft:
                    case MirDirection.UpLeft:
                        AddAttackCell(CurrentMap.GetCell(Functions.Move(location, Functions.ShiftDirection(direction, -1))), delay, element, targetIDs, locations, locationSet, hitTargets);
                        AddAttackCell(CurrentMap.GetCell(Functions.Move(location, Functions.ShiftDirection(direction, 1))), delay, element, targetIDs, locations, locationSet, hitTargets);
                        break;
                }
            }
        }

        private void AddAttackCell(Cell cell, int delay, Element element, List<uint> targetIDs, List<Point> locations, HashSet<Point> locationSet, HashSet<MapObject> hitTargets)
        {
            if (cell == null) return;

            if (locationSet.Add(cell.Location))
                locations.Add(cell.Location);

            if (cell.Objects == null) return;

            foreach (MapObject ob in cell.Objects)
            {
                if (!CanAttackTarget(ob)) continue;
                if (!hitTargets.Add(ob)) continue;

                targetIDs.Add(ob.ObjectID);

                ActionList.Add(new DelayedAction(
                    SEnvir.Now.AddMilliseconds(delay),
                    ActionType.DelayAttack,
                    ob,
                    GetMC(),
                    element));
            }
        }

    }
}
