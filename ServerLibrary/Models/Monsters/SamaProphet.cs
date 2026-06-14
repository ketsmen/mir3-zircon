using Library;
using Server.Envir;
using System;

namespace Server.Models.Monsters
{
    class SamaProphet : MonsterObject
    {
        public DateTime CheckTime;
        private DateTime ProtectionCheckTime;
        private bool HasProtection;

        protected override bool InAttackRange()
        {
            if (Target.CurrentMap != CurrentMap) return false;

            return Target.CurrentLocation != CurrentLocation && Functions.InRange(CurrentLocation, Target.CurrentLocation, 12);
        }

        protected override void OnSpawned()
        {
            base.OnSpawned();

            SpawnMinions(1, 0, Target);
        }

        public override void Process()
        {
            base.Process();

            if (Dead || SEnvir.Now < CheckTime) return;

            CheckTime = SEnvir.Now.AddSeconds(10);

            foreach (MapObject ob in CurrentMap.Objects) //Expensive.
            {
                if (ob.Race != ObjectType.Monster) continue;

                MonsterObject mob = (MonsterObject)ob;


                if (mob.MonsterInfo.Flag != MonsterFlag.SamaSorcerer) continue;

                if (Functions.InRange(mob.CurrentLocation, CurrentLocation, MonsterInfo.ViewRange - 3)) continue;

                TeleportObjectNear(mob, Functions.Move(CurrentLocation, MirDirection.DownLeft, 2), 1);
                break;
            }
        }
        public override bool SpawnMinion(MonsterObject mob)
        {
            return SpawnMinionNear(mob, Functions.Move(CurrentLocation, MirDirection.DownLeft, 5), 4);
        }

        public override void ProcessTarget()
        {
            if (Target == null) return;

            if (!CanAttack) return;

            if (SEnvir.Random.Next(5) == 0 && !Functions.InRange(Target.CurrentLocation, CurrentLocation, 5))
                TeleportObjectNear(Target, Functions.Move(CurrentLocation, MirDirection.DownLeft, 2), 1);

            RangeAttack();
        }

        public override int Attacked(MapObject attacker, int power, Element element, bool canReflect = true, bool ignoreShield = false, bool canCrit = true, bool canStruck = true)
        {
            if (ProtectedBySamaMonsters()) return 0;

            return base.Attacked(attacker, power, element, canReflect, ignoreShield, canCrit);
        }

        private bool ProtectedBySamaMonsters()
        {
            if (SEnvir.Now < ProtectionCheckTime) return HasProtection;

            ProtectionCheckTime = SEnvir.Now.AddSeconds(1);
            HasProtection = false;

            foreach (MapObject ob in CurrentMap.Objects)
            {
                if (ob.Race != ObjectType.Monster) continue;

                MonsterObject mob = (MonsterObject)ob;

                if (mob.Dead) continue;
                if (mob.MonsterInfo.Flag != MonsterFlag.BloodStone && mob.MonsterInfo.Flag != MonsterFlag.SamaSorcerer) continue;

                HasProtection = true;
                break;
            }

            return HasProtection;
        }

        public virtual void RangeAttack()
        {
            switch (SEnvir.Random.Next(3))
            {
                case 0:
                    AttackAoE(15, MagicType.SamaProphetFire, Element.Fire);
                    break;
                case 1:
                    AttackAoE(15, MagicType.SamaProphetWind, Element.Ice);
                    break;
                case 2:
                    AttackAoE(15, MagicType.SamaProphetLightning, Element.Lightning);
                    break;
            }
        }
    }
}
