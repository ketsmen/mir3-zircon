using Library;
using Server.Envir;
using System;

namespace Server.Models.Monsters
{
    class SamaScorcer : MonsterObject
    {
        private DateTime ProtectionCheckTime;
        private bool HasProtection;

        protected override bool InAttackRange()
        {
            if (Target.CurrentMap != CurrentMap) return false;

            return Target.CurrentLocation != CurrentLocation && Functions.InRange(CurrentLocation, Target.CurrentLocation, 3);
        }

        public override int Attacked(MapObject attacker, int power, Element element, bool canReflect = true, bool ignoreShield = false, bool canCrit = true, bool canStruck = true)
        {
            if (ProtectedByBloodStone()) return 0;

            return base.Attacked(attacker, power, element, canReflect, ignoreShield, canCrit);
        }

        private bool ProtectedByBloodStone()
        {
            if (SEnvir.Now < ProtectionCheckTime) return HasProtection;

            ProtectionCheckTime = SEnvir.Now.AddSeconds(1);
            HasProtection = false;

            foreach (MapObject ob in CurrentMap.Objects)
            {
                if (ob.Race != ObjectType.Monster) continue;

                MonsterObject mob = (MonsterObject)ob;

                if (mob.Dead || mob.MonsterInfo.Flag != MonsterFlag.BloodStone) continue;

                HasProtection = true;
                break;
            }

            return HasProtection;
        }

        public override void ProcessTarget()
        {
            if (Target == null) return;

            if (!CanAttack) return;

            if (SEnvir.Random.Next(5) == 0 && !Functions.InRange(Target.CurrentLocation, CurrentLocation, 5))
                TeleportObjectNear(Target, CurrentLocation, 2);

            base.ProcessTarget();
        }
    }
}
