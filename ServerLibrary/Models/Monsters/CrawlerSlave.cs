using System;
using Library;
using Server.Envir;
using S = Library.Network.ServerPackets;

namespace Server.Models.Monsters
{
    public class CrawlerSlave : DuelHitMonster
    {
        public override void ProcessAction(DelayedAction action)
        {
            switch (action.Type)
            {
                case ActionType.Function:
                    PoisonAttack((MapObject)action.Data[0], (int)action.Data[1], (Element)action.Data[2]);
                    return;
            }

            base.ProcessAction(action);
        }

        protected override void Attack2()
        {
            Broadcast(new S.ObjectMagic { ObjectID = ObjectID, Direction = Direction, CurrentLocation = CurrentLocation, Cast = true, Type = MagicType.None });

            foreach (MapObject ob in GetTargets(CurrentMap, Functions.Move(CurrentLocation, Direction), 1))
            {
                ActionList.Add(new DelayedAction(
                    SEnvir.Now.AddMilliseconds(400),
                    ActionType.Function,
                    ob,
                    GetDC(),
                    AttackElement));
            }
        }

        private void PoisonAttack(MapObject ob, int power, Element element)
        {
            int damage = Attack(ob, power, element);
            if (damage <= 0) return;

            ob.ApplyPoison(new Poison
            {
                Value = GetSC(),
                Type = PoisonType.Green,
                Owner = this,
                TickFrequency = TimeSpan.FromSeconds(2),
                TickCount = 10,
            });
        }
    }
}
