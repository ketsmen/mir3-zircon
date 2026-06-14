using Library;
using Library.Network;
using Library.SystemModels;
using Server.DBModels;
using Server.Envir;
using Server.Models.Monsters;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using S = Library.Network.ServerPackets;

namespace Server.Models
{
    public class HeroObject : MapObject
    {
        public override ObjectType Race => ObjectType.Hero;
        public override bool Blocking => !Dead;

        public HeroInfo Info;
        public PlayerObject Owner;

        public UserItem[] Inventory;
        public UserItem[] Equipment;

        public int BagWeight;
        public int WearWeight;
        public int HandWeight;

        public decimal MaxExperience;
        public MapObject Target;

        public DateTime SearchTime;
        public DateTime RoamTime;
        public DateTime SupportTime;
        public DateTime AutoPotTime;
        public DateTime AutoPotLowStockTime;
        public DateTime ReviveTime;
        public DateTime UseItemTime;

        public int MoveDelay = 600;
        public int AttackDelay = 900;

        private const int RunDistance = 2;
        private const int RunStartDistance = 3;

        protected virtual int AttackRange => 1;
        protected virtual int ViewRange => Math.Max(AttackRange, Math.Min(Config.MaxViewRange, Config.HeroSearchRange));
        protected virtual int MaxPursuitRange => Math.Max(ViewRange, Math.Min(Config.MaxViewRange, Config.HeroPursuitRange));
        protected virtual int PreferredOwnerRange => 2;
        protected virtual TimeSpan SearchDelay => TimeSpan.FromMilliseconds(800);
        protected virtual TimeSpan RoamDelay => TimeSpan.FromMilliseconds(500);
        protected virtual TimeSpan SupportDelay => TimeSpan.FromSeconds(2);
        protected virtual TimeSpan AutoPotDelay => TimeSpan.FromSeconds(1);
        protected virtual TimeSpan AutoMagicDelay => TimeSpan.FromMilliseconds(1600);
        protected virtual MagicType AutoMagic => MagicType.None;
        protected virtual Element AutoMagicElement => Element.None;
        protected virtual int AutoMagicCost => 0;
        protected virtual bool CanUseAutoMagicForRange => AutoMagic != MagicType.None &&
                                                         CurrentMP >= AutoMagicCost &&
                                                         (Poison & PoisonType.Silenced) != PoisonType.Silenced;
        protected virtual int DesiredAttackRange => CanUseAutoMagicForRange ? AttackRange : 1;

        public MirClass Class => Info.Class;
        public MirGender Gender => Info.Gender;
        public int HairType => Info.HairType;
        public Color HairColour => Info.HairColour;
        public byte Grade => Info.Grade;

        public HeroBehaviour Behaviour => Owner?.Character?.HeroBehaviour ?? HeroBehaviour.Attack;

        public override string Name
        {
            get { return Info?.Name ?? string.Empty; }
            set
            {
                if (Info != null)
                    Info.Name = value;
            }
        }

        public override int Level
        {
            get { return Math.Max(1, Info?.Level ?? 1); }
            set
            {
                if (Info != null)
                    Info.Level = Math.Max(1, value);
            }
        }

        public override int CurrentHP
        {
            get { return Info?.CurrentHP ?? 0; }
            set
            {
                if (Info != null)
                    Info.CurrentHP = Math.Max(0, value);
            }
        }

        public override int CurrentMP
        {
            get { return Info?.CurrentMP ?? 0; }
            set
            {
                if (Info != null)
                    Info.CurrentMP = Math.Max(0, value);
            }
        }

        public override bool CanMove => base.CanMove && MoveDelay > 0;
        public override bool CanAttack => base.CanAttack && AttackDelay > 0;

        public HeroObject(HeroInfo info, PlayerObject owner)
        {
            Info = info;
            Owner = owner;

            Stats = new Stats();
            Inventory = new UserItem[Globals.HeroInventorySize];
            Equipment = new UserItem[Globals.EquipmentSize];

            Direction = owner?.Direction ?? (MirDirection)SEnvir.Random.Next(8);
            NameColour = Color.MediumPurple;

            LoadItems();
            RefreshStats();

            bool reviveRecorded = Info.ReviveTime > DateTime.MinValue;
            if (CurrentHP <= 0 && reviveRecorded && SEnvir.Now < Info.ReviveTime)
            {
                Dead = true;
                ReviveTime = Info.ReviveTime;
            }
            else if (CurrentHP <= 0)
            {
                CurrentHP = reviveRecorded ? Math.Max(1, Stats[Stat.Health] / 2) : Stats[Stat.Health];

                if (reviveRecorded)
                    Info.ReviveTime = DateTime.MinValue;
            }

            if (!Dead && CurrentMP <= 0)
                CurrentMP = reviveRecorded ? Math.Max(1, Stats[Stat.Mana] / 2) : Stats[Stat.Mana];

            DisplayHP = CurrentHP;
            DisplayMP = CurrentMP;

            SearchTime = SEnvir.Now.AddMilliseconds(SEnvir.Random.Next((int)SearchDelay.TotalMilliseconds));
            RoamTime = SEnvir.Now.AddMilliseconds(SEnvir.Random.Next((int)RoamDelay.TotalMilliseconds));
            RegenTime = SEnvir.Now.AddMilliseconds(SEnvir.Random.Next((int)RegenDelay.TotalMilliseconds));
            ActionTime = SEnvir.Now.AddSeconds(1);
        }

        public static HeroObject Create(HeroInfo info, PlayerObject owner)
        {
            return info.Class switch
            {
                MirClass.Warrior => new WarriorHero(info, owner),
                MirClass.Wizard => new WizardHero(info, owner),
                MirClass.Taoist => new TaoistHero(info, owner),
                MirClass.Assassin => new AssassinHero(info, owner),
                _ => new HeroObject(info, owner)
            };
        }

        public bool Summon()
        {
            if (Owner?.Node == null || Owner.CurrentMap == null || Owner.Dead) return false;
            if (Owner.CurrentMap.Info.NoHero) return false;
            if (Node != null) return true;

            Cell cell = Owner.CurrentMap.GetCell(Functions.Move(Owner.CurrentLocation, Owner.Direction, -1));

            if (cell == null || cell.IsBlocking(this, false))
                cell = Owner.CurrentCell;

            if (cell == null) return false;

            if (!Spawn(Owner.CurrentMap, cell.Location)) return false;

            AddAllObjects();
            Activate();

            Owner.Character.HeroSpawned = true;
            Owner.Enqueue(new S.UpdateHeroSpawnState { State = Dead ? HeroSpawnState.Dead : HeroSpawnState.Summoned });
            Owner.Enqueue(new S.HeroInformation { Hero = ToClientHero() });

            return true;
        }

        public override void Activate()
        {
            if (Activated) return;

            Activated = true;
            SEnvir.ActiveObjects.Add(this);
        }

        public override void DeActivate()
        {
            return;
        }

        public void Recall()
        {
            if (Node == null) return;

            Target = null;
            Despawn();
        }

        public void SendInformation()
        {
            Owner?.Enqueue(new S.HeroInformation { Hero = ToClientHero() });
        }

        public ClientUserHero ToClientHero()
        {
            return Info.ToClientHero(Stats);
        }

        public override void ProcessAction(DelayedAction action)
        {
            switch (action.Type)
            {
                case ActionType.DelayAttack:
                    MapObject target = (MapObject)action.Data[0];
                    MagicType magic = action.Data.Length > 3 ? (MagicType)action.Data[3] : MagicType.None;
                    int damage = Attack(target, (int)action.Data[1], (Element)action.Data[2]);

                    if (damage > 0)
                        OnAttackLanded(target, magic, damage);
                    return;
            }

            base.ProcessAction(action);
        }

        public override void Process()
        {
            if (Owner?.Node == null || Owner.CurrentMap == null)
            {
                Recall();
                return;
            }

            if (Owner.Dead)
            {
                Owner.HeroDespawn();
                return;
            }

            if (Owner.CurrentMap != CurrentMap || Owner.CurrentMap.Info.NoHero)
            {
                Owner.UpdateHeroAfterOwnerMove();
                return;
            }

            if (Dead)
            {
                ProcessDead();
                return;
            }

            if (Target?.Node == null || Target.Dead || Target.CurrentMap != CurrentMap ||
                !Functions.InRange(CurrentLocation, Target.CurrentLocation, MaxPursuitRange) ||
                !CanAttackTarget(Target))
                Target = null;

            ProcessRegen();
            ProcessAutoPot();
            ProcessSupport();
            ProcessSearch();
            ProcessTarget();
            ProcessRoam();
        }

        protected virtual void ProcessDead()
        {
            if (Config.HeroReviveDelay <= TimeSpan.Zero || SEnvir.Now < ReviveTime) return;
            if (Owner?.Node == null || Owner.Dead || Owner.CurrentMap?.Info?.NoHero == true) return;

            Dead = false;
            SetHP(Math.Max(1, Stats[Stat.Health] / 2));
            SetMP(Math.Max(1, Stats[Stat.Mana] / 2));
            Info.ReviveTime = DateTime.MinValue;
            DisplayHP = CurrentHP;
            DisplayMP = CurrentMP;

            Target = null;
            PoisonList.Clear();
            Poison = PoisonType.None;

            Broadcast(new S.ObjectRevive { ObjectID = ObjectID, Location = CurrentLocation, Effect = true });
            Owner.Enqueue(new S.UpdateHeroSpawnState { State = HeroSpawnState.Summoned });
            Owner.Enqueue(new S.HeroInformation { Hero = ToClientHero() });
            Owner.Connection?.ReceiveChat($"{Name}已经复活。", MessageType.System);
        }

        public virtual void ProcessRegen()
        {
            if (SEnvir.Now < RegenTime) return;

            RegenTime = SEnvir.Now + RegenDelay;

            if ((Poison & PoisonType.Hemorrhage) == PoisonType.Hemorrhage) return;

            if (CurrentHP < Stats[Stat.Health])
                ChangeHP(Math.Max(1, Stats[Stat.Health] / 50));

            if (CurrentMP < Stats[Stat.Mana])
                ChangeMP(Math.Max(1, Stats[Stat.Mana] / 50));
        }

        public virtual void ProcessSearch()
        {
            if (Behaviour == HeroBehaviour.Follow || Behaviour == HeroBehaviour.CounterAttack && Target == null) return;
            if (Target != null || SEnvir.Now < SearchTime) return;

            SearchTime = SEnvir.Now + SearchDelay;

            int bestScore = int.MinValue;
            List<MapObject> candidates = new List<MapObject>();

            foreach (MapObject ob in Owner.VisibleObjects)
            {
                if (!CanAttackTarget(ob)) continue;

                int distance = Functions.Distance(CurrentLocation, ob.CurrentLocation);
                if (distance > ViewRange) continue;
                if (Owner != null && !Functions.InRange(Owner.CurrentLocation, ob.CurrentLocation, MaxPursuitRange)) continue;

                int score = GetTargetScore(ob, distance);
                if (score < bestScore) continue;

                if (score > bestScore)
                {
                    candidates.Clear();
                    bestScore = score;
                }

                candidates.Add(ob);
            }

            if (candidates.Count == 0) return;

            Target = candidates[SEnvir.Random.Next(candidates.Count)];
        }

        public virtual void ProcessTarget()
        {
            if (Behaviour == HeroBehaviour.Follow)
            {
                Target = null;
                return;
            }

            if (Target == null) return;

            if (Owner == null || !Functions.InRange(Owner.CurrentLocation, Target.CurrentLocation, MaxPursuitRange))
            {
                Target = null;
                return;
            }

            if (Functions.Distance(CurrentLocation, Owner.CurrentLocation) > MaxPursuitRange)
            {
                Target = null;
                MoveTo(Owner.CurrentLocation);
                return;
            }

            if (!InAttackRange())
            {
                MoveTo(Target.CurrentLocation);
                return;
            }

            if (!CanAttack) return;

            Attack();
        }

        public virtual void ProcessRoam()
        {
            if (!CanMove || SEnvir.Now < RoamTime || CurrentMap != Owner.CurrentMap) return;

            RoamTime = SEnvir.Now + RoamDelay;

            int distance = Functions.Distance(CurrentLocation, Owner.CurrentLocation);

            if (distance > MaxPursuitRange + PreferredOwnerRange)
            {
                Cell recallCell = Owner.CurrentMap.GetCell(Functions.Move(Owner.CurrentLocation, Owner.Direction, -1));

                if (recallCell == null || recallCell.IsBlocking(this, false))
                    recallCell = Owner.CurrentCell;

                if (recallCell != null)
                    Teleport(Owner.CurrentMap, recallCell.Location);

                Target = null;
                return;
            }

            if (Target != null) return;

            Point ownerFront = Functions.Move(Owner.CurrentLocation, Owner.Direction);
            if (CurrentLocation == ownerFront)
            {
                MoveTo(Functions.Move(Owner.CurrentLocation, Owner.Direction, -1));
                return;
            }

            if (distance <= PreferredOwnerRange) return;

            MoveTo(Owner.CurrentLocation);
        }

        protected virtual void ProcessSupport()
        {
        }

        protected virtual int GetTargetScore(MapObject ob, int distance)
        {
            int score = 10000 - distance * 100;

            if (Owner != null && Functions.InRange(Owner.CurrentLocation, ob.CurrentLocation, PreferredOwnerRange + 1))
                score += 1000;

            switch (ob)
            {
                case MonsterObject mob:
                    if (mob.Target == Owner)
                        score += 6000;
                    else if (mob.Target == this)
                        score += 5000;
                    else if (mob.PetOwner == null)
                        score += 2000;
                    else
                        score += 1000;

                    if (mob.EXPOwner == Owner || mob.HeroEXPOwner == this)
                        score += 750;

                    break;
                case PlayerObject:
                    score += 1500;
                    break;
                case HeroObject:
                    score += 1250;
                    break;
            }

            if (ob.Level > Level + 5)
                score -= 500;

            return score;
        }

        protected virtual void Attack()
        {
            Direction = Functions.DirectionFromPoint(CurrentLocation, Target.CurrentLocation);

            MagicType magic = GetAutoMagic();
            if (magic != MagicType.None)
            {
                Element element = GetAutoMagicElement();
                Broadcast(new S.ObjectMagic
                {
                    ObjectID = ObjectID,
                    Direction = Direction,
                    CurrentLocation = CurrentLocation,
                    Cast = true,
                    Type = magic,
                    AttackElement = element,
                    Targets = new List<uint> { Target.ObjectID },
                    Locations = new List<Point> { Target.CurrentLocation }
                });

                ChangeMP(-AutoMagicCost);
                UpdateMagicAttackTime();
                DamageItem((int)EquipmentSlot.Weapon);

                ActionList.Add(new DelayedAction(
                    SEnvir.Now.AddMilliseconds(500),
                    ActionType.DelayAttack,
                    Target,
                    GetAutoMagicPower(),
                    element,
                    magic));
                return;
            }

            if (CanUseAutoMagicForRange && Functions.InRange(CurrentLocation, Target.CurrentLocation, AttackRange))
                return;

            if (!Functions.InRange(CurrentLocation, Target.CurrentLocation, 1))
            {
                MoveTo(Target.CurrentLocation);
                return;
            }

            MagicType attackMagic = GetMeleeAttackMagic();
            Element attackElement = GetMeleeAttackElement();

            Broadcast(new S.ObjectAttack
            {
                ObjectID = ObjectID,
                Direction = Direction,
                Location = CurrentLocation,
                TargetID = Target.ObjectID,
                AttackMagic = attackMagic,
                AttackElement = attackElement
            });

            UpdateAttackTime();
            DamageItem((int)EquipmentSlot.Weapon);

            ActionList.Add(new DelayedAction(
                SEnvir.Now.AddMilliseconds(400),
                ActionType.DelayAttack,
                Target,
                GetMeleeAttackPower(),
                attackElement,
                attackMagic));
        }

        public virtual int Attack(MapObject ob, int power, Element element)
        {
            if (ob?.Node == null || ob.Dead || !CanAttackTarget(ob)) return 0;

            int damage;

            if (element == Element.None)
            {
                if (SEnvir.Random.Next(Math.Max(1, ob.Stats[Stat.Agility])) > Stats[Stat.Accuracy])
                {
                    ob.Dodged();
                    return 0;
                }

                damage = power - ob.GetAC();
            }
            else
            {
                damage = power - ob.GetMR();
            }

            int resistance = ob.Stats.GetResistanceValue(element);

            if (resistance > 0)
                damage -= damage * resistance / 10;
            else if (resistance < 0)
                damage -= damage * resistance / 5;

            if (damage <= 0)
            {
                ob.Blocked();
                return 0;
            }

            damage = ob.Attacked(this, damage, element);

            if (damage <= 0) return damage;

            if (Stats[Stat.LifeSteal] > 0)
            {
                LifeSteal += damage * Stats[Stat.LifeSteal] / 100M;

                if (LifeSteal > 1)
                {
                    int heal = (int)Math.Floor(LifeSteal);
                    LifeSteal -= heal;
                    ChangeHP(heal);
                }
            }

            return damage;
        }

        public bool DamageItem(int slot, int rate = 1, bool delayStats = false)
        {
            if (slot < 0 || slot >= Equipment.Length) return false;

            UserItem item = Equipment[slot];
            if (item == null || item.Info.Durability == 0 || item.CurrentDurability == 0) return false;
            if ((item.Flags & UserItemFlags.Marriage) == UserItemFlags.Marriage) return false;

            switch (item.Info.ItemType)
            {
                case ItemType.Nothing:
                case ItemType.Consumable:
                case ItemType.Poison:
                case ItemType.Amulet:
                case ItemType.Scroll:
                    return false;
                case ItemType.Weapon:
                    if (SEnvir.Random.Next(Math.Max(1, Stats[Stat.Strength])) > 0) return false;
                    break;
                default:
                    if (SEnvir.Random.Next(3) == 0 && SEnvir.Random.Next(Math.Max(1, Stats[Stat.Strength])) > 0) return false;
                    break;
            }

            item.CurrentDurability = Math.Max(0, item.CurrentDurability - rate);

            Owner?.Enqueue(new S.ItemDurability
            {
                GridType = GridType.HeroEquipment,
                Slot = slot,
                CurrentDurability = item.CurrentDurability,
            });

            if (item.CurrentDurability != 0) return false;

            if (!delayStats)
            {
                SendShapeUpdate();
                RefreshStats();
                SendInformation();
            }

            return true;
        }

        public override int Attacked(MapObject attacker, int power, Element element, bool canReflect = true, bool ignoreShield = false, bool canCrit = true, bool canStruck = true)
        {
            if (attacker?.Node == null || power == 0 || Dead || attacker.CurrentMap != CurrentMap ||
                !Functions.InRange(attacker.CurrentLocation, CurrentLocation, Config.MaxViewRange) ||
                Stats[Stat.Invincibility] > 0) return 0;

            if (StruckTime != DateTime.MaxValue && SEnvir.Now > StruckTime.AddMilliseconds(300))
            {
                StruckTime = SEnvir.Now;
                Broadcast(new S.ObjectStruck { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation, AttackerID = attacker.ObjectID, Element = element });
            }

            if ((Poison & PoisonType.Red) == PoisonType.Red)
                power = (int)(power * 1.2F);

            for (int i = 0; i < attacker.Stats[Stat.Rebirth]; i++)
                power = (int)(power * 1.5F);

            BuffInfo buff = Buffs.FirstOrDefault(x => x.Type == BuffType.MagicShield);

            if (buff != null)
                buff.RemainingTime -= TimeSpan.FromMilliseconds(power * 10);

            power -= power * Stats[Stat.MagicShield] / 100;

            if (SEnvir.Random.Next(100) < attacker.Stats[Stat.CriticalChance] && canCrit && power > 0)
            {
                power += power + power * attacker.Stats[Stat.CriticalDamage] / 100;
                Critical();
            }

            bool update = false;
            for (int i = 0; i < Equipment.Length; i++)
            {
                switch ((EquipmentSlot)i)
                {
                    case EquipmentSlot.Amulet:
                    case EquipmentSlot.Poison:
                    case EquipmentSlot.Torch:
                        continue;
                }

                update = DamageItem(i, SEnvir.Random.Next(2) + 1, true) || update;
            }

            if (update)
            {
                SendShapeUpdate();
                RefreshStats();
                SendInformation();
            }

            buff = Buffs.FirstOrDefault(x => x.Type == BuffType.SuperiorMagicShield);

            if (buff != null)
            {
                Stats[Stat.SuperiorMagicShield] -= power;
                if (Stats[Stat.SuperiorMagicShield] <= 0)
                    BuffRemove(buff);
            }
            else
                ChangeHP(-power);

            if (Behaviour != HeroBehaviour.Follow && ShouldReplaceTarget(attacker, 4000))
            {
                Target = attacker;
                SearchTime = DateTime.MinValue;
            }

            return power;
        }

        public void OwnerAttacked(MapObject attacker)
        {
            if (Node == null || Dead || Behaviour == HeroBehaviour.Follow) return;
            if (!ShouldReplaceTarget(attacker, 7000)) return;

            Target = attacker;
            SearchTime = DateTime.MinValue;
        }

        public void OwnerEngagedTarget(MapObject target)
        {
            if (Node == null || Dead) return;
            if (Behaviour != HeroBehaviour.Attack && Behaviour != HeroBehaviour.Custom) return;
            if (!ShouldReplaceTarget(target, 5500)) return;

            Target = target;
            SearchTime = DateTime.MinValue;
        }

        protected virtual bool ShouldReplaceTarget(MapObject attacker, int bonus)
        {
            if (attacker?.Node == null || !CanAttackTarget(attacker)) return false;
            if (Target == null || !CanAttackTarget(Target)) return true;

            int attackerScore = GetTargetScore(attacker, Functions.Distance(CurrentLocation, attacker.CurrentLocation)) + bonus;
            int targetScore = GetTargetScore(Target, Functions.Distance(CurrentLocation, Target.CurrentLocation));

            return attackerScore > targetScore;
        }

        public override void Die()
        {
            base.Die();

            Target = null;
            ReviveTime = Config.HeroReviveDelay > TimeSpan.Zero ? SEnvir.Now + Config.HeroReviveDelay : DateTime.MaxValue;
            Info.ReviveTime = ReviveTime;

            Owner?.Enqueue(new S.UpdateHeroSpawnState { State = HeroSpawnState.Dead });
            Owner?.Enqueue(new S.HeroInformation { Hero = ToClientHero() });
            Owner?.Connection?.ReceiveChat($"{Name}已经死亡。", MessageType.System);
        }

        public override void RefreshStats()
        {
            Stats.Clear();
            AddBaseStats();

            foreach (UserItem item in Equipment)
            {
                if (item == null || item.CurrentDurability == 0 && item.Info.Durability > 0) continue;

                Stats.Add(item.Info.Stats, item.Info.ItemType != ItemType.Weapon);
                Stats.Add(item.Stats, item.Info.ItemType != ItemType.Weapon);

                if (item.Info.ItemType != ItemType.Weapon) continue;

                Stat element = item.Stats.GetWeaponElement();

                if (element == Stat.None)
                    element = item.Info.Stats.GetWeaponElement();

                if (element != Stat.None)
                    Stats[element] += item.Stats.GetWeaponElementValue() + item.Info.Stats.GetWeaponElementValue();
            }

            foreach (BuffInfo buff in Buffs)
            {
                if (buff.Pause || buff.Stats == null) continue;

                Stats.Add(buff.Stats);
            }

            if (Buffs.Any(x => x.Type == BuffType.MagicWeakness))
            {
                Stats[Stat.MinMR] = 0;
                Stats[Stat.MaxMR] = 0;
            }

            Stats[Stat.AttackSpeed] += Math.Min(3, Level / 15);

            Stats[Stat.FireResistance] = Math.Min(5, Stats[Stat.FireResistance]);
            Stats[Stat.IceResistance] = Math.Min(5, Stats[Stat.IceResistance]);
            Stats[Stat.LightningResistance] = Math.Min(5, Stats[Stat.LightningResistance]);
            Stats[Stat.WindResistance] = Math.Min(5, Stats[Stat.WindResistance]);
            Stats[Stat.HolyResistance] = Math.Min(5, Stats[Stat.HolyResistance]);
            Stats[Stat.DarkResistance] = Math.Min(5, Stats[Stat.DarkResistance]);
            Stats[Stat.PhantomResistance] = Math.Min(5, Stats[Stat.PhantomResistance]);
            Stats[Stat.PhysicalResistance] = Math.Min(5, Stats[Stat.PhysicalResistance]);

            Stats[Stat.Health] += Stats[Stat.Health] * Stats[Stat.HealthPercent] / 100;
            Stats[Stat.Mana] += Stats[Stat.Mana] * Stats[Stat.ManaPercent] / 100;

            Stats[Stat.MinDC] += Stats[Stat.MinDC] * Stats[Stat.DCPercent] / 100;
            Stats[Stat.MaxDC] += Stats[Stat.MaxDC] * Stats[Stat.DCPercent] / 100;
            Stats[Stat.MinMC] += Stats[Stat.MinMC] * Stats[Stat.MCPercent] / 100;
            Stats[Stat.MaxMC] += Stats[Stat.MaxMC] * Stats[Stat.MCPercent] / 100;
            Stats[Stat.MinSC] += Stats[Stat.MinSC] * Stats[Stat.SCPercent] / 100;
            Stats[Stat.MaxSC] += Stats[Stat.MaxSC] * Stats[Stat.SCPercent] / 100;

            Stats[Stat.MinAC] += Stats[Stat.MinAC] * Stats[Stat.PhysicalDefencePercent] / 100;
            Stats[Stat.MaxAC] += Stats[Stat.MaxAC] * Stats[Stat.PhysicalDefencePercent] / 100;
            Stats[Stat.MinMR] += Stats[Stat.MinMR] * Stats[Stat.MagicDefencePercent] / 100;
            Stats[Stat.MaxMR] += Stats[Stat.MaxMR] * Stats[Stat.MagicDefencePercent] / 100;

            Stats[Stat.Health] = Math.Max(10, Stats[Stat.Health]);
            Stats[Stat.Mana] = Math.Max(10, Stats[Stat.Mana]);

            Stats[Stat.MinAC] = Math.Max(0, Stats[Stat.MinAC]);
            Stats[Stat.MaxAC] = Math.Max(0, Stats[Stat.MaxAC]);
            Stats[Stat.MinMR] = Math.Max(0, Stats[Stat.MinMR]);
            Stats[Stat.MaxMR] = Math.Max(0, Stats[Stat.MaxMR]);
            Stats[Stat.MinDC] = Math.Max(0, Stats[Stat.MinDC]);
            Stats[Stat.MaxDC] = Math.Max(0, Stats[Stat.MaxDC]);
            Stats[Stat.MinMC] = Math.Max(0, Stats[Stat.MinMC]);
            Stats[Stat.MaxMC] = Math.Max(0, Stats[Stat.MaxMC]);
            Stats[Stat.MinSC] = Math.Max(0, Stats[Stat.MinSC]);
            Stats[Stat.MaxSC] = Math.Max(0, Stats[Stat.MaxSC]);

            Stats[Stat.MinDC] = Math.Min(Stats[Stat.MinDC], Stats[Stat.MaxDC]);
            Stats[Stat.MinMC] = Math.Min(Stats[Stat.MinMC], Stats[Stat.MaxMC]);
            Stats[Stat.MinSC] = Math.Min(Stats[Stat.MinSC], Stats[Stat.MaxSC]);

            MoveDelay = Math.Max(250, 700 - Stats[Stat.AttackSpeed] * 20);
            AttackDelay = Math.Max(450, 1200 - Stats[Stat.AttackSpeed] * 40);
            RegenDelay = TimeSpan.FromMilliseconds(15000 - Math.Min(20, Stats[Stat.Comfort]) * 650);

            if (CurrentHP > Stats[Stat.Health]) SetHP(Stats[Stat.Health]);
            if (CurrentMP > Stats[Stat.Mana]) SetMP(Stats[Stat.Mana]);

            RefreshWeight();

            if (Node != null)
            {
                S.DataObjectMaxHealthMana p = new S.DataObjectMaxHealthMana { ObjectID = ObjectID, MaxHealth = Stats[Stat.Health], MaxMana = Stats[Stat.Mana] };

                foreach (PlayerObject player in DataSeenByPlayers)
                    player.Enqueue(p);
            }
        }

        public void AddBaseStats()
        {
            MaxExperience = Level < Globals.ExperienceList.Count ? Globals.ExperienceList[Level] : 0;

            BaseStat stat = null;

            foreach (BaseStat baseStat in SEnvir.BaseStatList.Binding)
            {
                if (baseStat.Class != Class) continue;
                if (baseStat.Level > Level) continue;
                if (stat != null && baseStat.Level < stat.Level) continue;

                stat = baseStat;

                if (baseStat.Level == Level) break;
            }

            if (stat == null) return;

            Stats[Stat.Health] = stat.Health;
            Stats[Stat.Mana] = stat.Mana;
            Stats[Stat.BagWeight] = stat.BagWeight;
            Stats[Stat.WearWeight] = stat.WearWeight;
            Stats[Stat.HandWeight] = stat.HandWeight;
            Stats[Stat.Accuracy] = stat.Accuracy;
            Stats[Stat.Agility] = stat.Agility;
            Stats[Stat.MinAC] = stat.MinAC;
            Stats[Stat.MaxAC] = stat.MaxAC;
            Stats[Stat.MinMR] = stat.MinMR;
            Stats[Stat.MaxMR] = stat.MaxMR;
            Stats[Stat.MinDC] = stat.MinDC;
            Stats[Stat.MaxDC] = stat.MaxDC;
            Stats[Stat.MinMC] = stat.MinMC;
            Stats[Stat.MaxMC] = stat.MaxMC;
            Stats[Stat.MinSC] = stat.MinSC;
            Stats[Stat.MaxSC] = stat.MaxSC;
            Stats[Stat.PickUpRadius] = 1;
            Stats[Stat.SkillRate] = 1;
            Stats[Stat.CriticalChance] = 1;
            Stats[Stat.BaseHealth] = Stats[Stat.Health];
            Stats[Stat.BaseMana] = Stats[Stat.Mana];
        }

        public void GainExperience(decimal amount, bool rateEffected = true)
        {
            if (rateEffected)
                amount *= 1M + Config.HeroExperienceRate / 100M;

            if (amount <= 0) return;

            Info.Experience += amount;
            Owner?.Enqueue(new S.GainHeroExperience { Amount = amount, Experience = Info.Experience, MaxExperience = MaxExperience });

            while (MaxExperience > 0 && Info.Experience >= MaxExperience && Level < Config.MaxLevel)
            {
                Info.Experience -= MaxExperience;
                Level++;

                RefreshStats();
                SetHP(Stats[Stat.Health]);
                SetMP(Stats[Stat.Mana]);

                Broadcast(new S.ObjectLeveled { ObjectID = ObjectID });
                Owner?.Enqueue(new S.HeroLevelChanged { Level = Level, Experience = Info.Experience, MaxExperience = MaxExperience });
                Owner?.Enqueue(new S.HeroInformation { Hero = ToClientHero() });
                Owner?.Connection?.ReceiveChat($"{Name}升到了 {Level} 级。", MessageType.System);
            }
        }

        public override void ProcessHPMP()
        {
            int oldHP = DisplayHP;
            int oldMP = DisplayMP;

            base.ProcessHPMP();

            if (oldHP == DisplayHP && oldMP == DisplayMP) return;

            Owner?.Enqueue(new S.HeroHealthChanged
            {
                CurrentHP = CurrentHP,
                CurrentMP = CurrentMP,
                MaxHP = Stats[Stat.Health],
                MaxMP = Stats[Stat.Mana],
            });
        }

        public override void SetHP(int amount)
        {
            base.SetHP(amount);
            ClampHealthMana();
        }

        public override void ChangeHP(int amount)
        {
            base.ChangeHP(amount);
            ClampHealthMana();
        }

        public override void SetMP(int amount)
        {
            base.SetMP(amount);
            ClampHealthMana();
        }

        public override void ChangeMP(int amount)
        {
            base.ChangeMP(amount);
            ClampHealthMana();
        }

        protected void ClampHealthMana()
        {
            if (Info == null) return;

            Info.CurrentHP = Math.Max(0, Math.Min(Info.CurrentHP, Stats[Stat.Health]));
            Info.CurrentMP = Math.Max(0, Math.Min(Info.CurrentMP, Stats[Stat.Mana]));
        }

        protected virtual int GetAttackPower()
        {
            return Class switch
            {
                MirClass.Wizard => Math.Max(GetDC(), GetMC()),
                MirClass.Taoist => Math.Max(GetDC(), GetSC()),
                _ => GetDC()
            };
        }

        protected virtual Element GetAttackElement()
        {
            Stat element = Equipment[(int)EquipmentSlot.Weapon]?.Stats.GetWeaponElement() ?? Stat.None;

            if (element == Stat.None)
                element = Equipment[(int)EquipmentSlot.Weapon]?.Info.Stats.GetWeaponElement() ?? Stat.None;

            return element switch
            {
                Stat.FireAttack => Element.Fire,
                Stat.IceAttack => Element.Ice,
                Stat.LightningAttack => Element.Lightning,
                Stat.WindAttack => Element.Wind,
                Stat.HolyAttack => Element.Holy,
                Stat.DarkAttack => Element.Dark,
                Stat.PhantomAttack => Element.Phantom,
                _ => Element.None
            };
        }

        protected virtual bool InAttackRange()
        {
            return Target?.CurrentMap == CurrentMap &&
                   Target.CurrentLocation != CurrentLocation &&
                   Functions.InRange(CurrentLocation, Target.CurrentLocation, DesiredAttackRange);
        }

        protected virtual MagicType GetAutoMagic()
        {
            if (AutoMagic == MagicType.None || CurrentMP < AutoMagicCost || !CanCast) return MagicType.None;
            return AutoMagic;
        }

        protected virtual Element GetAutoMagicElement()
        {
            return AutoMagicElement;
        }

        protected virtual int GetAutoMagicPower()
        {
            return GetAttackPower();
        }

        protected virtual MagicType GetMeleeAttackMagic()
        {
            return MagicType.None;
        }

        protected virtual Element GetMeleeAttackElement()
        {
            return GetAttackElement();
        }

        protected virtual int GetMeleeAttackPower()
        {
            return GetAttackPower();
        }

        protected virtual void OnAttackLanded(MapObject target, MagicType magic, int damage)
        {
        }

        protected virtual void UpdateAttackTime()
        {
            AttackTime = SEnvir.Now.AddMilliseconds(AttackDelay);
            ActionTime = SEnvir.Now.AddMilliseconds(Math.Min(MoveDelay, AttackDelay - 100));
        }

        protected virtual void UpdateMagicAttackTime()
        {
            MagicTime = SEnvir.Now + AutoMagicDelay;
            AttackTime = SEnvir.Now.AddMilliseconds(AttackDelay);
            ActionTime = SEnvir.Now.AddMilliseconds(Math.Min(MoveDelay, AttackDelay - 100));
        }

        protected virtual void UpdateMoveTime()
        {
            MoveTime = SEnvir.Now.AddMilliseconds(MoveDelay);
            ActionTime = SEnvir.Now.AddMilliseconds(Math.Min(MoveDelay - 100, AttackDelay));
        }

        protected virtual void MoveTo(Point target)
        {
            if (!CanMove || CurrentLocation == target) return;

            MirDirection direction = Functions.DirectionFromPoint(CurrentLocation, target);

            if (Functions.Distance(CurrentLocation, target) > RunStartDistance && Run(direction))
                return;

            int rotation = SEnvir.Random.Next(2) == 0 ? 1 : -1;

            for (int d = 0; d < 8; d++)
            {
                if (Walk(direction)) return;

                direction = Functions.ShiftDirection(direction, rotation);
            }
        }

        public virtual bool Run(MirDirection direction)
        {
            if (!CanMove) return false;

            Cell cell = null;

            for (int distance = 1; distance <= RunDistance; distance++)
            {
                cell = CurrentMap.GetCell(Functions.Move(CurrentLocation, direction, distance));
                if (cell == null || cell.IsBlocking(this, false)) return false;
            }

            BuffRemove(BuffType.Invisibility);
            BuffRemove(BuffType.Transparency);

            Direction = direction;
            UpdateMoveTime();

            PreventSpellCheck = true;
            CurrentCell = cell;
            PreventSpellCheck = false;

            RemoveAllObjects();
            AddAllObjects();

            Broadcast(new S.ObjectMove { ObjectID = ObjectID, Direction = direction, Location = CurrentLocation, Distance = RunDistance });
            CheckSpellObjects();
            return true;
        }

        public virtual bool Walk(MirDirection direction)
        {
            if (!CanMove) return false;

            Cell cell = CurrentMap.GetCell(Functions.Move(CurrentLocation, direction));
            if (cell == null || cell.IsBlocking(this, false)) return false;

            BuffRemove(BuffType.Invisibility);
            BuffRemove(BuffType.Transparency);

            Direction = direction;
            UpdateMoveTime();

            PreventSpellCheck = true;
            CurrentCell = cell;
            PreventSpellCheck = false;

            RemoveAllObjects();
            AddAllObjects();

            Broadcast(new S.ObjectMove { ObjectID = ObjectID, Direction = direction, Location = CurrentLocation, Distance = 1 });
            CheckSpellObjects();
            return true;
        }

        public override bool CanAttackTarget(MapObject ob)
        {
            if (Owner == null || ob == this || ob?.Node == null || ob.Dead || !ob.Visible || ob is Guard || ob is CastleLord) return false;

            switch (ob.Race)
            {
                case ObjectType.Monster:
                    MonsterObject mob = (MonsterObject)ob;

                    if (mob.PetOwner == null) return true;
                    if (mob.PetOwner == Owner) return false;

                    if (mob.InSafeZone || InSafeZone) return false;

                    switch (Owner.AttackMode)
                    {
                        case AttackMode.Peace:
                            return false;
                        case AttackMode.Group:
                            if (Owner.InGroup(mob.PetOwner)) return false;
                            break;
                        case AttackMode.Guild:
                            if (Owner.InGuild(mob.PetOwner)) return false;
                            break;
                        case AttackMode.WarRedBrown:
                            if (mob.PetOwner.Stats[Stat.Brown] == 0 && mob.PetOwner.Stats[Stat.PKPoint] < Config.RedPoint && !Owner.AtWar(mob.PetOwner))
                                return false;
                            break;
                    }

                    return true;
                case ObjectType.Player:
                    PlayerObject player = (PlayerObject)ob;

                    if (player == Owner || player.GameMaster) return false;
                    if (InSafeZone || player.InSafeZone) return false;

                    switch (Owner.AttackMode)
                    {
                        case AttackMode.Peace:
                            return false;
                        case AttackMode.Group:
                            if (Owner.InGroup(player)) return false;
                            break;
                        case AttackMode.Guild:
                            if (Owner.InGuild(player)) return false;
                            break;
                        case AttackMode.WarRedBrown:
                            if (player.Stats[Stat.Brown] == 0 && player.Stats[Stat.PKPoint] < Config.RedPoint && !Owner.AtWar(player))
                                return false;
                            break;
                    }

                    return true;
                case ObjectType.Hero:
                    HeroObject hero = (HeroObject)ob;

                    if (hero.Owner == null || hero.Owner == Owner) return false;
                    if (InSafeZone || hero.InSafeZone) return false;

                    switch (Owner.AttackMode)
                    {
                        case AttackMode.Peace:
                            return false;
                        case AttackMode.Group:
                            if (Owner.InGroup(hero.Owner)) return false;
                            break;
                        case AttackMode.Guild:
                            if (Owner.InGuild(hero.Owner)) return false;
                            break;
                        case AttackMode.WarRedBrown:
                            if (hero.Owner.Stats[Stat.Brown] == 0 && hero.Owner.Stats[Stat.PKPoint] < Config.RedPoint && !Owner.AtWar(hero.Owner))
                                return false;
                            break;
                    }

                    return true;
                default:
                    return false;
            }
        }

        public override bool CanHelpTarget(MapObject ob)
        {
            if (Owner == null || ob?.Node == null || ob.Dead || !ob.Visible || ob is Guard || ob is CastleLord) return false;
            if (ob == this || ob == Owner) return true;

            return ob is HeroObject hero && hero.Owner == Owner;
        }

        public override bool CanBeSeenBy(PlayerObject ob)
        {
            if (Owner == ob)
                return base.CanBeSeenBy(ob);

            return Owner != null && Owner.CanBeSeenBy(ob);
        }

        public override Packet GetInfoPacket(PlayerObject ob)
        {
            return new S.ObjectHero
            {
                Index = Info.Index,
                ObjectID = ObjectID,
                Name = Name,
                OwnerName = Owner?.Name,
                Caption = Owner == null ? null : $"{Owner.Name}的英雄",
                GuildName = Owner?.Character.Account.GuildMember?.Guild.GuildName,
                NameColour = NameColour,
                Direction = Direction,
                Location = CurrentLocation,
                Class = Class,
                Gender = Gender,
                HairType = HairType,
                HairColour = HairColour,
                Weapon = Equipment[(int)EquipmentSlot.Weapon]?.Info.Shape ?? -1,
                Shield = Equipment[(int)EquipmentSlot.Shield]?.Info.Shape ?? -1,
                Helmet = Equipment[(int)EquipmentSlot.Helmet]?.Info.Shape ?? 0,
                HideHead = false,
                Armour = Equipment[(int)EquipmentSlot.Armour]?.Info.Shape ?? 0,
                ArmourColour = Equipment[(int)EquipmentSlot.Armour]?.Colour ?? Color.Empty,
                Costume = Equipment[(int)EquipmentSlot.Costume]?.Info.Shape ?? -1,
                ArmourEffect = Equipment[(int)EquipmentSlot.Armour]?.Info.ExteriorEffect ?? 0,
                EmblemEffect = Equipment[(int)EquipmentSlot.Emblem]?.Info.ExteriorEffect ?? 0,
                WeaponEffect = Equipment[(int)EquipmentSlot.Weapon]?.Info.ExteriorEffect ?? 0,
                ShieldEffect = Equipment[(int)EquipmentSlot.Shield]?.Info.ExteriorEffect ?? 0,
                Light = Stats[Stat.Light],
                Dead = Dead,
                Poison = Poison,
                Buffs = Buffs.Where(x => x.Visible).Select(x => new KeyValuePair<BuffType, int>(x.Type, x.Extra)).ToDictionary()
            };
        }

        public override Packet GetDataPacket(PlayerObject ob)
        {
            return new S.DataObjectPlayer
            {
                ObjectID = ObjectID,
                MapIndex = CurrentMap.Info.Index,
                CurrentLocation = CurrentLocation,
                Name = Name,
                Health = DisplayHP,
                MaxHealth = Stats[Stat.Health],
                Dead = Dead,
                Mana = DisplayMP,
                MaxMana = Stats[Stat.Mana]
            };
        }

        protected override void OnLocationChanged()
        {
            base.OnLocationChanged();

            if (CurrentCell == null) return;

            InSafeZone = CurrentCell.SafeZone != null;
        }

        public override void OnBeforeDespawned()
        {
            base.OnBeforeDespawned();

            Owner?.Enqueue(new S.UpdateHeroSpawnState { State = HeroSpawnState.Unsummoned });
        }

        public override void OnDespawned()
        {
            base.OnDespawned();

            if (Owner?.Character != null)
                Owner.Character.HeroSpawned = false;
        }

        public override void CleanUp()
        {
            base.CleanUp();

            Target = null;
        }

        public void LoadItems()
        {
            foreach (UserItem item in Info.Items)
            {
                if (item.Slot >= Globals.EquipmentOffSet)
                {
                    int slot = item.Slot - Globals.EquipmentOffSet;

                    if (slot < 0 || slot >= Equipment.Length)
                    {
                        SEnvir.Log($"[Hero Equipment] Slot: {item.Slot}, Character: {Info.Character?.CharacterName}, Hero: {Info.Name}");
                        continue;
                    }

                    Equipment[slot] = item;
                    continue;
                }

                if (item.Slot < 0 || item.Slot >= Inventory.Length)
                {
                    SEnvir.Log($"[Hero Inventory] Slot: {item.Slot}, Character: {Info.Character?.CharacterName}, Hero: {Info.Name}");
                    continue;
                }

                Inventory[item.Slot] = item;
            }
        }

        public void RefreshWeight()
        {
            BagWeight = 0;
            WearWeight = 0;
            HandWeight = 0;

            foreach (UserItem item in Inventory)
            {
                if (item == null) continue;

                BagWeight += item.Weight;
            }

            foreach (UserItem item in Equipment)
            {
                if (item == null) continue;

                switch (item.Info.ItemType)
                {
                    case ItemType.Weapon:
                    case ItemType.Torch:
                    case ItemType.Shield:
                        HandWeight += item.Weight;
                        break;
                    default:
                        WearWeight += item.Weight;
                        break;
                }
            }
        }

        public bool CanUseItem(UserItem item)
        {
            if (item?.Info == null) return false;
            if ((item.Flags & UserItemFlags.Marriage) == UserItemFlags.Marriage) return false;
            if ((item.Flags & UserItemFlags.GameMaster) == UserItemFlags.GameMaster && Owner?.Character?.Account?.TempAdmin != true) return false;
            if (item.Info.Durability > 0 && item.CurrentDurability <= 0) return false;

            switch (Gender)
            {
                case MirGender.Male:
                    if ((item.Info.RequiredGender & RequiredGender.Male) != RequiredGender.Male)
                        return false;
                    break;
                case MirGender.Female:
                    if ((item.Info.RequiredGender & RequiredGender.Female) != RequiredGender.Female)
                        return false;
                    break;
            }

            switch (Class)
            {
                case MirClass.Warrior:
                    if ((item.Info.RequiredClass & RequiredClass.Warrior) != RequiredClass.Warrior)
                        return false;
                    break;
                case MirClass.Wizard:
                    if ((item.Info.RequiredClass & RequiredClass.Wizard) != RequiredClass.Wizard)
                        return false;
                    break;
                case MirClass.Taoist:
                    if ((item.Info.RequiredClass & RequiredClass.Taoist) != RequiredClass.Taoist)
                        return false;
                    break;
                case MirClass.Assassin:
                    if ((item.Info.RequiredClass & RequiredClass.Assassin) != RequiredClass.Assassin)
                        return false;
                    break;
            }

            switch (item.Info.RequiredType)
            {
                case RequiredType.Level:
                    if (Level < item.Info.RequiredAmount) return false;
                    break;
                case RequiredType.MaxLevel:
                    if (Level > item.Info.RequiredAmount) return false;
                    break;
                case RequiredType.AC:
                    if (Stats[Stat.MaxAC] < item.Info.RequiredAmount) return false;
                    break;
                case RequiredType.MR:
                    if (Stats[Stat.MaxMR] < item.Info.RequiredAmount) return false;
                    break;
                case RequiredType.DC:
                    if (Stats[Stat.MaxDC] < item.Info.RequiredAmount) return false;
                    break;
                case RequiredType.MC:
                    if (Stats[Stat.MaxMC] < item.Info.RequiredAmount) return false;
                    break;
                case RequiredType.SC:
                    if (Stats[Stat.MaxSC] < item.Info.RequiredAmount) return false;
                    break;
                case RequiredType.Health:
                    if (Stats[Stat.Health] < item.Info.RequiredAmount) return false;
                    break;
                case RequiredType.Mana:
                    if (Stats[Stat.Mana] < item.Info.RequiredAmount) return false;
                    break;
                case RequiredType.Accuracy:
                    if (Stats[Stat.Accuracy] < item.Info.RequiredAmount) return false;
                    break;
                case RequiredType.Agility:
                    if (Stats[Stat.Agility] < item.Info.RequiredAmount) return false;
                    break;
                case RequiredType.RebirthLevel:
                case RequiredType.MaxRebirthLevel:
                case RequiredType.CompanionLevel:
                case RequiredType.MaxCompanionLevel:
                    return false;
            }

            switch (item.Info.ItemEffect)
            {
                case ItemEffect.PickAxe:
                case ItemEffect.FishingRod:
                case ItemEffect.MagicRing:
                    return false;
            }

            return true;
        }

        public bool CanWearItem(UserItem item, EquipmentSlot slot)
        {
            if (!Functions.CorrectSlot(item.Info.ItemType, slot) || !CanUseItem(item))
                return false;

            switch (slot)
            {
                case EquipmentSlot.HorseArmour:
                case EquipmentSlot.Hook:
                case EquipmentSlot.Float:
                case EquipmentSlot.Bait:
                case EquipmentSlot.Finder:
                case EquipmentSlot.Reel:
                    return false;
            }

            switch (item.Info.ItemType)
            {
                case ItemType.HorseArmour:
                case ItemType.Hook:
                case ItemType.Float:
                case ItemType.Bait:
                case ItemType.Finder:
                case ItemType.Reel:
                    return false;
                case ItemType.Weapon:
                case ItemType.Torch:
                case ItemType.Shield:
                    if (HandWeight - (Equipment[(int)slot]?.Weight ?? 0) + item.Weight > Stats[Stat.HandWeight]) return false;
                    break;
                default:
                    if (WearWeight - (Equipment[(int)slot]?.Weight ?? 0) + item.Weight > Stats[Stat.WearWeight]) return false;
                    break;
            }

            return true;
        }

        public void SendShapeUpdate()
        {
            Broadcast(new S.PlayerUpdate
            {
                ObjectID = ObjectID,
                Weapon = Equipment[(int)EquipmentSlot.Weapon]?.Info.Shape ?? -1,
                Shield = Equipment[(int)EquipmentSlot.Shield]?.Info.Shape ?? -1,
                Helmet = Equipment[(int)EquipmentSlot.Helmet]?.Info.Shape ?? 0,
                HideHead = false,
                Armour = Equipment[(int)EquipmentSlot.Armour]?.Info.Shape ?? 0,
                ArmourColour = Equipment[(int)EquipmentSlot.Armour]?.Colour ?? Color.Empty,
                Costume = Equipment[(int)EquipmentSlot.Costume]?.Info.Shape ?? -1,
                ArmourEffect = Equipment[(int)EquipmentSlot.Armour]?.Info.ExteriorEffect ?? 0,
                EmblemEffect = Equipment[(int)EquipmentSlot.Emblem]?.Info.ExteriorEffect ?? 0,
                WeaponEffect = Equipment[(int)EquipmentSlot.Weapon]?.Info.ExteriorEffect ?? 0,
                ShieldEffect = Equipment[(int)EquipmentSlot.Shield]?.Info.ExteriorEffect ?? 0,
                HorseArmour = 0,
                Light = Stats[Stat.Light]
            });
        }

        public void ProcessAutoPot()
        {
            if (!Info.AutoPot || Dead || SEnvir.Now < AutoPotTime) return;

            AutoPotTime = SEnvir.Now + AutoPotDelay;

            bool usedHealthPotion = false;

            if (Info.HPItemIndex > 0 && Stats[Stat.Health] > 0 && CurrentHP * 100 <= Stats[Stat.Health] * Math.Max(1, (int)Info.AutoHPPercent))
            {
                usedHealthPotion = UseAutoPotion(Info.HPItemIndex, true, out bool missing);

                if (missing)
                    ClearAutoPotion(true, "英雄HP自动喝药的药品不可用或已不存在，设置已清空。");
            }

            if (Info.MPItemIndex > 0 && (!usedHealthPotion || Info.MPItemIndex != Info.HPItemIndex) && Stats[Stat.Mana] > 0 && CurrentMP * 100 <= Stats[Stat.Mana] * Math.Max(1, (int)Info.AutoMPPercent))
            {
                if (!UseAutoPotion(Info.MPItemIndex, false, out bool missing))
                {
                    if (missing)
                        ClearAutoPotion(false, "英雄MP自动喝药的药品不可用或已不存在，设置已清空。");
                }
            }
        }

        public void ItemUse(int slot)
        {
            if (slot < 0 || slot >= Inventory.Length) return;

            UserItem item = Inventory[slot];
            if (item == null) return;

            S.ItemChanged result = new S.ItemChanged
            {
                Link = new CellLinkInfo { GridType = GridType.HeroInventory, Slot = slot }
            };

            Owner?.Enqueue(result);

            if (Dead || Owner?.Node == null) return;
            if (!CanUseItem(item)) return;
            if (item.Info.ItemType != ItemType.Consumable) return;
            if (SEnvir.Now < UseItemTime && item.Info.ItemEffect != ItemEffect.ElixirOfPurification) return;

            switch (item.Info.Shape)
            {
                case 0:
                    ChangeHP(item.Info.Stats[Stat.Health]);
                    ChangeMP(item.Info.Stats[Stat.Mana]);

                    if (item.Info.Stats[Stat.Experience] > 0)
                        GainExperience(item.Info.Stats[Stat.Experience]);
                    break;
                case 14:
                    if (!UsePurificationElixir()) return;
                    break;
                case 15:
                    if (CurrentHP >= Stats[Stat.Health] && CurrentMP >= Stats[Stat.Mana]) return;

                    SetHP(Stats[Stat.Health]);
                    SetMP(Stats[Stat.Mana]);
                    break;
                default:
                    return;
            }

            if (item.Info.ItemEffect != ItemEffect.ElixirOfPurification || UseItemTime < SEnvir.Now)
                UseItemTime = SEnvir.Now.AddMilliseconds(item.Info.Durability);
            else
                UseItemTime = UseItemTime.AddMilliseconds(item.Info.Durability);

            result.Success = true;

            if (item.Count > 1)
            {
                item.Count--;
                result.Link.Count = item.Count;
            }
            else
            {
                RemoveItem(item);
                Inventory[slot] = null;
                item.Delete();
                result.Link.Count = 0;
            }

            RefreshWeight();
            CheckAutoPotionAfterUse(item.Info.Index, item.Info.ItemName);
            SendInformation();
        }

        private bool UsePurificationElixir()
        {
            bool work = false;

            for (int i = PoisonList.Count - 1; i >= 0; i--)
            {
                Poison poison = PoisonList[i];

                switch (poison.Type)
                {
                    case PoisonType.Green:
                    case PoisonType.Red:
                    case PoisonType.Slow:
                    case PoisonType.Paralysis:
                    case PoisonType.HellFire:
                    case PoisonType.Silenced:
                    case PoisonType.Abyss:
                    case PoisonType.Burn:
                    case PoisonType.Containment:
                    case PoisonType.Binding:
                        PoisonList.Remove(poison);
                        work = true;
                        break;
                }
            }

            return work;
        }

        protected bool UseAutoPotion(int itemIndex, bool health)
        {
            return UseAutoPotion(itemIndex, health, out _);
        }

        protected bool UseAutoPotion(int itemIndex, bool health, out bool missing)
        {
            missing = true;

            for (int i = 0; i < Inventory.Length; i++)
            {
                UserItem item = Inventory[i];

                if (item == null || item.Info.Index != itemIndex) continue;

                missing = false;

                if (item.Info.ItemType != ItemType.Consumable || item.Info.Shape != 0 || !item.Info.CanAutoPot)
                {
                    missing = true;
                    return false;
                }

                int healthAmount = item.Info.Stats[Stat.Health];
                int manaAmount = item.Info.Stats[Stat.Mana];

                if (health && healthAmount <= 0)
                {
                    missing = true;
                    return false;
                }

                if (!health && manaAmount <= 0)
                {
                    missing = true;
                    return false;
                }

                ChangeHP(healthAmount);
                ChangeMP(manaAmount);

                if (item.Count > 1)
                {
                    item.Count--;
                    Owner?.Enqueue(new S.ItemChanged { Link = new CellLinkInfo { GridType = GridType.HeroInventory, Slot = i, Count = item.Count }, Success = true });
                }
                else
                {
                    RemoveItem(item);
                    Inventory[i] = null;
                    item.IsTemporary = true;
                    item.Delete();
                    Owner?.Enqueue(new S.ItemChanged { Link = new CellLinkInfo { GridType = GridType.HeroInventory, Slot = i, Count = 0 }, Success = true });
                }

                RefreshWeight();
                CheckAutoPotionAfterUse(itemIndex, item.Info.ItemName);
                SendInformation();
                return true;
            }

            return false;
        }

        private void CheckAutoPotionAfterUse(int itemIndex, string itemName)
        {
            long remaining = Inventory.Where(x => x?.Info.Index == itemIndex).Sum(x => x.Count);

            if (remaining <= 0)
            {
                bool changed = false;

                if (Info.HPItemIndex == itemIndex)
                {
                    Info.HPItemIndex = 0;
                    changed = true;
                }

                if (Info.MPItemIndex == itemIndex)
                {
                    Info.MPItemIndex = 0;
                    changed = true;
                }

                if (changed)
                {
                    SendAutoPotionSettings();
                    Owner?.Connection?.ReceiveChat($"{Name}的自动喝药药品 {itemName} 已用完，设置已清空。", MessageType.System);
                }

                return;
            }

            if (remaining <= 5 && SEnvir.Now >= AutoPotLowStockTime)
            {
                AutoPotLowStockTime = SEnvir.Now.AddSeconds(30);
                Owner?.Connection?.ReceiveChat($"{Name}的自动喝药药品 {itemName} 仅剩 {remaining} 个。", MessageType.System);
            }
        }

        private void ClearAutoPotion(bool health, string message)
        {
            if (health)
                Info.HPItemIndex = 0;
            else
                Info.MPItemIndex = 0;

            SendAutoPotionSettings();
            SendInformation();
            Owner?.Connection?.ReceiveChat(message, MessageType.System);
        }

        private void SendAutoPotionSettings()
        {
            Owner?.Enqueue(new S.SetHeroAutoPot
            {
                AutoPot = Info.AutoPot,
                HPItemIndex = Info.HPItemIndex,
                MPItemIndex = Info.MPItemIndex,
                AutoHPPercent = Info.AutoHPPercent,
                AutoMPPercent = Info.AutoMPPercent,
            });
        }

        public void RemoveItem(UserItem item)
        {
            item.Slot = -1;
            item.Character = null;
            item.Account = null;
            item.Mail = null;
            item.Auction = null;
            item.Companion = null;
            item.Guild = null;
            item.Hero = null;
            item.Flags &= ~UserItemFlags.Locked;
        }
    }

    public sealed class WarriorHero : HeroObject
    {
        public WarriorHero(HeroInfo info, PlayerObject owner) : base(info, owner) { }

        protected override MagicType GetMeleeAttackMagic()
        {
            return Level >= 25 ? MagicType.FlamingSword : MagicType.None;
        }

        protected override Element GetMeleeAttackElement()
        {
            return Level >= 25 ? Element.Fire : base.GetMeleeAttackElement();
        }

        protected override int GetMeleeAttackPower()
        {
            int power = base.GetMeleeAttackPower();

            if (Level >= 25)
                power += Math.Max(1, Stats[Stat.MaxDC] / 2);
            else if (Level >= 15)
                power += Math.Max(1, Stats[Stat.MaxDC] / 5);

            return power;
        }
    }

    public sealed class WizardHero : HeroObject
    {
        public WizardHero(HeroInfo info, PlayerObject owner) : base(info, owner) { }

        protected override int AttackRange => 6;
        protected override MagicType AutoMagic => Level >= 20 ? MagicType.ThunderBolt : MagicType.FireBall;
        protected override Element AutoMagicElement => Level >= 20 ? Element.Lightning : Element.Fire;
        protected override int AutoMagicCost => Math.Max(3, Level / 6);

        protected override int GetAutoMagicPower()
        {
            return Math.Max(GetMC(), GetDC()) + Level + Stats[Stat.FireAttack] + Stats[Stat.LightningAttack];
        }
    }

    public sealed class TaoistHero : HeroObject
    {
        public TaoistHero(HeroInfo info, PlayerObject owner) : base(info, owner) { }

        protected override int AttackRange => 5;
        protected override MagicType AutoMagic => MagicType.PoisonDust;
        protected override Element AutoMagicElement => Element.None;
        protected override int AutoMagicCost => Math.Max(2, Level / 8);
        protected override TimeSpan SupportDelay => TimeSpan.FromMilliseconds(1800);

        protected override int GetAutoMagicPower()
        {
            return Math.Max(GetSC(), GetDC()) + Level / 2;
        }

        protected override void ProcessSupport()
        {
            if (SEnvir.Now < SupportTime || !CanCast || CurrentMP <= 0 || Owner?.Node == null) return;

            SupportTime = SEnvir.Now + SupportDelay;

            MapObject healTarget = GetHealTarget();
            if (healTarget == null) return;

            int cost = Math.Max(3, Level / 7);
            if (CurrentMP < cost) return;

            if (!Functions.InRange(CurrentLocation, healTarget.CurrentLocation, AttackRange))
            {
                Target = null;
                MoveTo(healTarget.CurrentLocation);
                return;
            }

            Direction = Functions.DirectionFromPoint(CurrentLocation, healTarget.CurrentLocation);

            Broadcast(new S.ObjectMagic
            {
                ObjectID = ObjectID,
                Direction = Direction,
                CurrentLocation = CurrentLocation,
                Cast = true,
                Type = MagicType.Heal,
                AttackElement = Element.None,
                Targets = new List<uint> { healTarget.ObjectID },
                Locations = new List<Point> { healTarget.CurrentLocation }
            });

            ChangeMP(-cost);
            UpdateMagicAttackTime();

            int healing = Math.Max(10, GetSC() + Level + Stats[Stat.HolyAttack] * 2);
            int cap = Math.Max(20, 30 + Level / 2);

            healTarget.BuffAdd(BuffType.Heal, TimeSpan.FromSeconds(Math.Max(1, healing / cap)), new Stats
            {
                [Stat.Healing] = healing,
                [Stat.HealingCap] = cap,
            }, false, false, TimeSpan.FromSeconds(1));
        }

        private MapObject GetHealTarget()
        {
            bool selfNeedsHeal = CurrentHP > 0 &&
                                 CurrentHP * 100 <= Stats[Stat.Health] * 50 &&
                                 !Buffs.Any(x => x.Type == BuffType.Heal);

            bool ownerNeedsHeal = Owner.CurrentMap == CurrentMap &&
                Owner.CurrentHP > 0 &&
                Owner.CurrentHP * 100 <= Owner.Stats[Stat.Health] * 60 &&
                !Owner.Buffs.Any(x => x.Type == BuffType.Heal) &&
                CanHelpTarget(Owner);

            if (ownerNeedsHeal && Functions.InRange(CurrentLocation, Owner.CurrentLocation, AttackRange))
                return Owner;

            if (selfNeedsHeal)
                return this;

            if (ownerNeedsHeal)
                return Owner;

            return null;
        }

        protected override void OnAttackLanded(MapObject target, MagicType magic, int damage)
        {
            if (magic != MagicType.PoisonDust || target?.Node == null) return;
            if ((target.Poison & PoisonType.Green) == PoisonType.Green) return;

            target.ApplyPoison(new Poison
            {
                Owner = this,
                Type = PoisonType.Green,
                Value = Math.Max(1, GetSC() / 8),
                TickFrequency = TimeSpan.FromSeconds(2),
                TickCount = 5,
                TickTime = SEnvir.Now.AddSeconds(2),
                CanKill = false
            });
        }
    }

    public sealed class AssassinHero : HeroObject
    {
        public AssassinHero(HeroInfo info, PlayerObject owner) : base(info, owner) { }

        protected override MagicType GetMeleeAttackMagic()
        {
            return Level >= 20 ? MagicType.Hemorrhage : MagicType.None;
        }

        protected override int GetMeleeAttackPower()
        {
            int power = base.GetMeleeAttackPower();

            if (Level >= 20)
                power += Math.Max(1, Stats[Stat.MaxDC] / 4);

            return power;
        }

        protected override void OnAttackLanded(MapObject target, MagicType magic, int damage)
        {
            if (magic != MagicType.Hemorrhage || target?.Node == null) return;
            if ((target.Poison & PoisonType.Hemorrhage) == PoisonType.Hemorrhage) return;

            target.ApplyPoison(new Poison
            {
                Owner = this,
                Type = PoisonType.Hemorrhage,
                Value = Math.Max(1, GetDC() / 10),
                TickFrequency = TimeSpan.FromSeconds(2),
                TickCount = 4,
                TickTime = SEnvir.Now.AddSeconds(2),
                CanKill = false
            });
        }
    }
}
