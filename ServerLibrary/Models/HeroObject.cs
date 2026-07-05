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
        public override bool Blocking => base.Blocking && !Observer;

        public HeroInfo Info;
        public PlayerObject Owner;
        public bool GameMaster => Owner?.GameMaster == true;
        public bool Observer => Owner?.Observer == true;
        public bool Superman => Owner?.Superman == true;

        public UserItem[] Inventory;
        public UserItem[] Equipment;

        public int BagWeight;
        public int WearWeight;
        public int HandWeight;

        public decimal MaxExperience;
        public MapObject Target;

        public DateTime SearchTime;
        public DateTime ReconsiderTargetTime;
        public DateTime RoamTime;
        public DateTime SupportTime;
        public DateTime AutoPotTime;
        public DateTime AutoPotLowStockTime;
        public DateTime ReviveTime;
        public DateTime UseItemTime;

        public int MoveDelay = 600;
        public int AttackDelay = 900;
        private int MoveFailureCount;

        private const int RunDistance = 2;
        private const int RunStartDistance = 3;

        protected virtual int AttackRange => 1;
        protected virtual int ViewRange => Math.Max(AttackRange, Math.Min(Config.MaxViewRange, Config.HeroSearchRange));
        protected virtual int MaxPursuitRange => Math.Max(ViewRange, Math.Min(Config.MaxViewRange, Config.HeroPursuitRange));
        protected virtual int PreferredOwnerRange => 2;
        protected virtual TimeSpan SearchDelay => TimeSpan.FromMilliseconds(800);
        protected virtual TimeSpan ReconsiderTargetDelay => TimeSpan.FromMilliseconds(500);
        protected virtual TimeSpan RoamDelay => TimeSpan.FromMilliseconds(500);
        protected virtual TimeSpan SupportDelay => TimeSpan.FromSeconds(2);
        protected virtual TimeSpan AutoPotDelay => TimeSpan.FromSeconds(1);
        protected virtual TimeSpan AutoMagicDelay => TimeSpan.FromMilliseconds(1600);
        protected virtual int RangedRetreatDistance => 0;
        protected virtual MagicType AutoMagic => MagicType.None;
        protected virtual Element AutoMagicElement => Element.None;
        protected virtual int AutoMagicCost => 0;
        protected virtual bool CanUseAutoMagicForRange => AutoMagic != MagicType.None &&
                                                         CurrentMP >= AutoMagicCost &&
                                                         (Poison & PoisonType.Silenced) != PoisonType.Silenced;
        protected virtual int DesiredAttackRange
        {
            get
            {
                int customRange = GetCustomAutoSkillDesiredRange();
                return customRange > 0 ? customRange : (CanUseAutoMagicForRange ? AttackRange : 1);
            }
        }

        public MirClass Class => Info.Class;
        public MirGender Gender => Info.Gender;
        public int HairType => Info.HairType;
        public Color HairColour => Info.HairColour;
        public byte Grade => Info.Grade;

        public HeroBehaviour Behaviour => Owner?.Character?.HeroBehaviour ?? HeroBehaviour.Attack;
        private int MaxAllowedLevel => Math.Max(1, Math.Min(Config.MaxLevel, Owner?.Level ?? Config.MaxLevel));

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

            ApplyLevelLimit(false);
            LoadItems();
            RefreshStats();
            ProcessExperience();

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
            SEnvir.AddActiveObject(this);
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
                    Element element = (Element)action.Data[2];
                    int damage = Attack(target, (int)action.Data[1], element);

                    if (damage > 0)
                    {
                        LevelAttackMagic(magic);
                        LevelAttackPassives(target, magic, element);
                        OnAttackLanded(target, magic, damage);
                    }
                    return;
                case ActionType.DelayMagic:
                    if (action.Data.Length > 0 && action.Data[0] is HeroChainMagicPayload chainPayload)
                    {
                        CompleteHeroChainMagic(chainPayload);
                        return;
                    }

                    if (action.Data.Length > 0 && action.Data[0] is HeroAreaMagicPayload areaPayload)
                    {
                        CompleteHeroAreaMagic(areaPayload);
                        return;
                    }

                    if (action.Data.Length > 4 && action.Data[1] is int && action.Data[2] is CustomSkillInfo customSkill && action.Data[4] is UserMagic userMagic)
                    {
                        int remainingTicks = action.Data.Length > 5 && action.Data[5] is int ticks ? ticks : 0;
                        CompleteCustomHeroSkill(userMagic, customSkill, action.Data[3], remainingTicks);
                        return;
                    }
                    break;
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
                !CanPursueTarget(Target) ||
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
            if (!CanSearchForBehaviour()) return;
            if (Target != null || SEnvir.Now < SearchTime) return;

            SearchTime = SEnvir.Now + SearchDelay;

            if (TryFindNearestAttackableMonster(ViewRange, out MapObject monsterTarget, out _))
            {
                Target = monsterTarget;
                MoveFailureCount = 0;
                return;
            }

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

        private bool ReconsiderCloserMonsterTarget(bool force)
        {
            if (!UsesActiveAttackBehaviour()) return false;
            if (!force && SEnvir.Now < ReconsiderTargetTime) return false;

            ReconsiderTargetTime = SEnvir.Now + ReconsiderTargetDelay;

            if (!TryFindNearestAttackableMonster(ViewRange, out MapObject monsterTarget, out int monsterDistance)) return false;
            if (monsterTarget == Target) return false;

            int currentDistance = Target?.CurrentMap == CurrentMap ? Functions.Distance(CurrentLocation, Target.CurrentLocation) : int.MaxValue;
            if (!force && monsterDistance >= currentDistance) return false;

            Target = monsterTarget;
            MoveFailureCount = 0;
            return true;
        }

        private bool TryFindNearestAttackableMonster(int range, out MapObject target, out int distance)
        {
            target = null;
            distance = int.MaxValue;

            int bestScore = int.MinValue;

            foreach (MapObject ob in GetTargets(CurrentMap, CurrentLocation, range))
            {
                if (!(ob is MonsterObject)) continue;
                if (Owner != null && !Functions.InRange(Owner.CurrentLocation, ob.CurrentLocation, MaxPursuitRange)) continue;

                int currentDistance = Functions.Distance(CurrentLocation, ob.CurrentLocation);
                if (currentDistance > distance) continue;

                int score = GetTargetScore(ob, currentDistance);
                if (currentDistance == distance && score < bestScore) continue;

                if (currentDistance == distance && score == bestScore && target != null && SEnvir.Random.Next(2) != 0) continue;

                target = ob;
                distance = currentDistance;
                bestScore = score;
            }

            return target != null;
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

            ReconsiderCloserMonsterTarget(false);

            if (ShouldRetreatFromTarget() && TryRetreatFromTarget())
                return;

            if (!InAttackRange())
            {
                Point oldLocation = CurrentLocation;
                MoveTo(Target.CurrentLocation);

                if (CurrentLocation == oldLocation)
                {
                    MoveFailureCount++;

                    if (MoveFailureCount >= 2)
                        ReconsiderCloserMonsterTarget(true);
                }
                else
                    MoveFailureCount = 0;

                return;
            }

            MoveFailureCount = 0;

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

            if (TryCastCustomHeroSkill())
                return;

            if (TryCastClassSkill())
                return;

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

        private bool TryCastCustomHeroSkill()
        {
            if (!TryGetCustomAutoSkill(out UserMagic magic, out CustomSkillInfo skill, requireTargetInRange: true)) return false;
            if (Target?.Node == null || !CanAttackTarget(Target)) return false;

            Direction = Functions.DirectionFromPoint(CurrentLocation, Target.CurrentLocation);
            MirDirection castDirection = Direction;
            bool selfTarget = skill.TargetRule == CustomSkillTargetRule.Self;

            List<uint> targets = new();
            List<Point> locations = new();
            object payload;

            switch (skill.Template)
            {
                case CustomSkillTemplate.AreaDamage:
                case CustomSkillTemplate.PersistentAreaDamage:
                    Point areaLocation = selfTarget ? CurrentLocation : Target.CurrentLocation;
                    payload = CreateCustomHeroAreaPayload(areaLocation, castDirection);
                    locations.Add(areaLocation);
                    break;
                case CustomSkillTemplate.DirectDamage:
                case CustomSkillTemplate.ProjectileDamage:
                case CustomSkillTemplate.Debuff:
                case CustomSkillTemplate.Control:
                    if (selfTarget)
                    {
                        payload = CreateCustomHeroAreaPayload(CurrentLocation, castDirection);
                        locations.Add(CurrentLocation);
                    }
                    else
                    {
                        payload = CreateCustomHeroTargetPayload(Target);
                        targets.Add(Target.ObjectID);
                    }
                    break;
                case CustomSkillTemplate.ChainDamage:
                    List<MapObject> chainTargets = BuildCustomHeroChainTargets(skill, Target);
                    if (chainTargets.Count == 0) return false;
                    payload = new SkillChainPayload { Map = CurrentMap, Origin = CurrentLocation, Targets = chainTargets };
                    targets.AddRange(chainTargets.Select(x => x.ObjectID));
                    break;
                case CustomSkillTemplate.Move:
                    if (!TryGetCustomHeroMoveLocation(skill, Target, out Point moveLocation)) return false;
                    payload = new SkillLocationPayload { Map = CurrentMap, Location = moveLocation, Origin = CurrentLocation };
                    locations.Add(moveLocation);
                    break;
                default:
                    return false;
            }

            Broadcast(new S.ObjectMagic
            {
                ObjectID = ObjectID,
                Direction = Direction,
                CurrentLocation = CurrentLocation,
                Cast = true,
                Type = magic.Info.Magic,
                MagicInfoIndex = magic.Info.Index,
                AttackElement = skill.Element,
                Targets = targets,
                Locations = locations
            });

            ChangeMP(-magic.Cost);
            magic.Cooldown = SEnvir.Now.AddMilliseconds(Math.Max(0, magic.Info.Delay));
            UpdateMagicAttackTime();
            DamageItem((int)EquipmentSlot.Weapon);

            int remainingTicks = skill.Template == CustomSkillTemplate.PersistentAreaDamage ? GetCustomSkillTickCount(skill) : 0;
            ActionList.Add(new DelayedAction(
                SEnvir.Now.AddMilliseconds(GetCustomSkillDelay(skill, payload)),
                ActionType.DelayMagic,
                magic.Info.Magic,
                magic.Info.Index,
                skill,
                payload,
                magic,
                remainingTicks));

            return true;
        }

        protected bool TryCastCustomHeroSupportSkill()
        {
            if (!TryGetCustomSupportSkill(out UserMagic magic, out CustomSkillInfo skill, out MapObject supportTarget)) return false;
            if (supportTarget?.Node == null || supportTarget.CurrentMap != CurrentMap || !CanHelpTarget(supportTarget)) return false;

            int range = GetCustomSkillRange(skill);
            if (!Functions.InRange(CurrentLocation, supportTarget.CurrentLocation, range))
            {
                Target = null;
                MoveTo(supportTarget.CurrentLocation);
                return true;
            }

            MirDirection castDirection = supportTarget.CurrentLocation == CurrentLocation
                ? Direction
                : Functions.DirectionFromPoint(CurrentLocation, supportTarget.CurrentLocation);
            Direction = castDirection;
            object payload = CreateCustomHeroTargetPayload(supportTarget);
            if (skill.TargetRule == CustomSkillTargetRule.Self && GetCustomSkillSearchRadius(skill) > 0)
                payload = CreateCustomHeroAreaPayload(supportTarget.CurrentLocation, castDirection);

            Broadcast(new S.ObjectMagic
            {
                ObjectID = ObjectID,
                Direction = Direction,
                CurrentLocation = CurrentLocation,
                Cast = true,
                Type = magic.Info.Magic,
                MagicInfoIndex = magic.Info.Index,
                AttackElement = skill.Element,
                Targets = new List<uint> { supportTarget.ObjectID },
                Locations = new List<Point> { supportTarget.CurrentLocation }
            });

            ChangeMP(-magic.Cost);
            magic.Cooldown = SEnvir.Now.AddMilliseconds(Math.Max(0, magic.Info.Delay));
            UpdateMagicAttackTime();

            ActionList.Add(new DelayedAction(
                SEnvir.Now.AddMilliseconds(GetCustomSkillDelay(skill, payload)),
                ActionType.DelayMagic,
                magic.Info.Magic,
                magic.Info.Index,
                skill,
                payload,
                magic,
                0));

            return true;
        }

        protected bool TryCastCustomHeroSummonSkill()
        {
            if (!TryGetCustomSummonSkill(out UserMagic magic, out CustomSkillInfo skill)) return false;
            if (CurrentMap == null) return false;

            if (Owner?.Node != null && Owner.CurrentMap == CurrentMap)
                Direction = Owner.Direction;

            Point summonLocation = Functions.Move(CurrentLocation, Direction, -1);
            SkillLocationPayload payload = new() { Map = CurrentMap, Location = summonLocation, Origin = CurrentLocation };

            Broadcast(new S.ObjectMagic
            {
                ObjectID = ObjectID,
                Direction = Direction,
                CurrentLocation = CurrentLocation,
                Cast = true,
                Type = magic.Info.Magic,
                MagicInfoIndex = magic.Info.Index,
                AttackElement = skill.Element,
                Locations = new List<Point> { summonLocation }
            });

            ChangeMP(-magic.Cost);
            magic.Cooldown = SEnvir.Now.AddMilliseconds(Math.Max(0, magic.Info.Delay));
            UpdateMagicAttackTime();

            ActionList.Add(new DelayedAction(
                SEnvir.Now.AddMilliseconds(GetCustomSkillDelay(skill, payload)),
                ActionType.DelayMagic,
                magic.Info.Magic,
                magic.Info.Index,
                skill,
                payload,
                magic,
                0));

            return true;
        }

        protected void QueueHeroAreaMagic(UserMagic magic, MagicType magicType, Element element, Point location, int radius, int delay, bool spawnFireWall = false, int powerRate = 100, int slow = 0)
        {
            ActionList.Add(new DelayedAction(
                SEnvir.Now.AddMilliseconds(Math.Max(0, delay)),
                ActionType.DelayMagic,
                new HeroAreaMagicPayload
                {
                    Magic = magic,
                    MagicType = magicType,
                    Element = element,
                    Location = location,
                    Radius = Math.Max(0, radius),
                    SpawnFireWall = spawnFireWall,
                    PowerRate = Math.Max(1, powerRate),
                    Slow = Math.Max(0, slow),
                }));
        }

        private void CompleteHeroAreaMagic(HeroAreaMagicPayload payload)
        {
            if (payload == null || CurrentMap == null) return;

            if (payload.SpawnFireWall)
            {
                SpawnHeroFireWall(payload);
                return;
            }

            bool hit = false;
            foreach (Cell cell in CurrentMap.GetCells(payload.Location, 0, payload.Radius))
            {
                if (cell?.Objects == null) continue;

                foreach (MapObject target in cell.Objects.ToArray())
                {
                    if (target?.Node == null || target.Dead || target.CurrentMap != CurrentMap || !CanAttackTarget(target)) continue;

                    int damage = Attack(target, GetHeroAreaMagicPower(payload), payload.Element);
                    if (damage <= 0) continue;

                    hit = true;
                    LevelAttackPassives(target, payload.MagicType, payload.Element);
                    OnAttackLanded(target, payload.MagicType, damage);

                    if (payload.Slow > 0)
                    {
                        target.ApplyPoison(new Poison
                        {
                            Owner = this,
                            Type = PoisonType.Slow,
                            Value = payload.Slow,
                            TickFrequency = TimeSpan.FromSeconds(1),
                            TickCount = 3,
                            CanKill = false,
                        });
                    }
                }
            }

            if (hit)
                LevelAttackMagic(payload.MagicType);
        }

        private int GetHeroAreaMagicPower(HeroAreaMagicPayload payload)
        {
            int power = Math.Max(GetMC(), GetDC()) + Level + (payload.Magic?.GetPower() ?? 0);

            power += payload.Element switch
            {
                Element.Fire => Stats[Stat.FireAttack],
                Element.Ice => Stats[Stat.IceAttack],
                Element.Lightning => Stats[Stat.LightningAttack],
                Element.Wind => Stats[Stat.WindAttack],
                Element.Holy => Stats[Stat.HolyAttack],
                Element.Dark => Stats[Stat.DarkAttack],
                Element.Phantom => Stats[Stat.PhantomAttack],
                _ => 0,
            };

            return Math.Max(1, power * payload.PowerRate / 100);
        }

        private void SpawnHeroFireWall(HeroAreaMagicPayload payload)
        {
            bool spawned = false;
            Point[] locations =
            {
                Functions.Move(payload.Location, MirDirection.Up),
                Functions.Move(payload.Location, MirDirection.Down),
                payload.Location,
                Functions.Move(payload.Location, MirDirection.Left),
                Functions.Move(payload.Location, MirDirection.Right),
            };

            foreach (Point location in locations)
            {
                Cell cell = CurrentMap.GetCell(location);
                if (cell == null) continue;

                if (cell.Objects != null)
                {
                    for (int i = cell.Objects.Count - 1; i >= 0; i--)
                    {
                        if (cell.Objects[i].Race != ObjectType.Spell) continue;

                        SpellObject spell = (SpellObject)cell.Objects[i];
                        if (spell.Effect != SpellEffect.FireWall && spell.Effect != SpellEffect.Tempest) continue;

                        spell.Despawn();
                    }
                }

                SpellObject ob = new SpellObject
                {
                    DisplayLocation = cell.Location,
                    TickCount = ((payload.Magic?.Level ?? 0) + 2) * 5,
                    TickFrequency = TimeSpan.FromSeconds(2),
                    Owner = this,
                    Effect = SpellEffect.FireWall,
                    Magic = payload.Magic,
                };

                if (ob.Spawn(cell.Map, cell.Location))
                    spawned = true;
            }

            if (spawned)
                LevelAttackMagic(payload.MagicType);
        }

        protected void QueueHeroFireBounce(UserMagic magic, MapObject source, MapObject target, int remainingBounces, bool broadcastProjectile)
        {
            if (magic == null || source?.Node == null || target?.Node == null) return;

            int delay = Functions.Distance(source.CurrentLocation, target.CurrentLocation) * 48;
            if (!broadcastProjectile)
                delay += 500;

            ActionList.Add(new DelayedAction(
                SEnvir.Now.AddMilliseconds(Math.Max(0, delay)),
                ActionType.DelayMagic,
                new HeroChainMagicPayload
                {
                    Magic = magic,
                    MagicType = MagicType.FireBounce,
                    Element = Element.Fire,
                    Source = source,
                    Target = target,
                    TargetLocation = target.CurrentLocation,
                    RemainingBounces = Math.Max(0, remainingBounces),
                    BroadcastProjectile = broadcastProjectile,
                    JumpRadius = 3,
                    PowerDivisor = 0,
                }));
        }

        protected void QueueHeroChainLightning(UserMagic magic, List<Point> locations, List<Point> previousLocations, bool primary, int powerDivisor)
        {
            if (magic == null || locations == null || locations.Count == 0) return;

            ActionList.Add(new DelayedAction(
                SEnvir.Now.AddMilliseconds(primary ? 600 : 200),
                ActionType.DelayMagic,
                new HeroChainMagicPayload
                {
                    Magic = magic,
                    MagicType = MagicType.ChainLightning,
                    Element = Element.Lightning,
                    Locations = locations,
                    PreviousLocations = previousLocations ?? new List<Point>(),
                    Primary = primary,
                    PowerDivisor = Math.Max(0, powerDivisor),
                    JumpRadius = 1,
                }));
        }

        private void CompleteHeroChainMagic(HeroChainMagicPayload payload)
        {
            if (payload == null || CurrentMap == null) return;

            switch (payload.MagicType)
            {
                case MagicType.FireBounce:
                    CompleteHeroFireBounce(payload);
                    break;
                case MagicType.ChainLightning:
                    CompleteHeroChainLightning(payload);
                    break;
            }
        }

        private void CompleteHeroFireBounce(HeroChainMagicPayload payload)
        {
            MapObject target = payload.Target;
            if (target?.Node == null || target.Dead || target.CurrentMap != CurrentMap || !CanAttackTarget(target)) return;
            if (!Functions.InRange(target.CurrentLocation, payload.TargetLocation, Globals.MagicRange)) return;

            MapObject source = payload.Source?.Node == null ? this : payload.Source;

            if (payload.BroadcastProjectile)
            {
                Broadcast(new S.ObjectProjectile
                {
                    ObjectID = source.ObjectID,
                    Direction = source.Direction,
                    CurrentLocation = source.CurrentLocation,
                    Type = MagicType.FireBounce,
                    MagicInfoIndex = payload.Magic?.Info?.Index ?? 0,
                    Targets = new List<uint> { target.ObjectID },
                    Locations = new List<Point>(),
                });
            }

            int damage = Attack(target, GetHeroChainMagicPower(payload), Element.Fire);
            if (damage <= 0) return;

            LevelAttackMagic(MagicType.FireBounce);
            LevelAttackPassives(target, MagicType.FireBounce, Element.Fire);
            OnAttackLanded(target, MagicType.FireBounce, damage);

            if (payload.RemainingBounces <= 1) return;

            List<MapObject> targets = GetHeroFireBounceTargets(target, Math.Max(1, payload.JumpRadius));

            if (targets.Count == 0) return;

            MapObject nextTarget = targets[SEnvir.Random.Next(targets.Count)];
            QueueHeroFireBounce(payload.Magic, target, nextTarget, payload.RemainingBounces - 1, true);
        }

        private void CompleteHeroChainLightning(HeroChainMagicPayload payload)
        {
            if (payload.Locations == null || payload.Locations.Count == 0) return;

            List<Point> hitLocations = new List<Point>();
            HashSet<Point> checkedLocations = payload.Locations.Count > 1 ? new HashSet<Point>() : null;

            foreach (Point location in payload.Locations)
            {
                if (checkedLocations != null && !checkedLocations.Add(location)) continue;

                Cell cell = CurrentMap.GetCell(location);
                if (cell?.Objects == null) continue;

                bool hitCell = false;
                foreach (MapObject target in cell.Objects.ToArray())
                {
                    if (target?.Node == null || target.Dead || target.CurrentMap != CurrentMap) continue;
                    if (target.Race != ObjectType.Monster || !CanAttackTarget(target)) continue;
                    if (!Functions.InRange(CurrentLocation, target.CurrentLocation, Globals.MagicRange)) continue;

                    int damage = Attack(target, GetHeroChainMagicPower(payload), Element.Lightning);
                    if (damage <= 0) continue;

                    hitCell = true;
                    LevelAttackMagic(MagicType.ChainLightning);
                    LevelAttackPassives(target, MagicType.ChainLightning, Element.Lightning);
                    OnAttackLanded(target, MagicType.ChainLightning, damage);
                }

                if (hitCell)
                    hitLocations.Add(location);
            }

            if (!payload.Primary && hitLocations.Count > 0)
            {
                Broadcast(new S.ObjectProjectile
                {
                    ObjectID = ObjectID,
                    Direction = Direction,
                    CurrentLocation = CurrentLocation,
                    Type = MagicType.ChainLightning,
                    MagicInfoIndex = payload.Magic?.Info?.Index ?? 0,
                    Targets = new List<uint>(),
                    Locations = hitLocations,
                });
            }

            if (hitLocations.Count == 0) return;

            List<Point> previousLocations = payload.PreviousLocations ?? new List<Point>();
            previousLocations.AddRange(hitLocations);

            int nextPowerDivisor = payload.PowerDivisor + 1;
            List<Point> nextLocations = GetNextHeroChainLightningLocations(hitLocations, previousLocations, Math.Max(1, nextPowerDivisor));
            if (nextLocations.Count == 0) return;

            QueueHeroChainLightning(payload.Magic, nextLocations, previousLocations, false, nextPowerDivisor);
        }

        private List<Point> GetNextHeroChainLightningLocations(IEnumerable<Point> locations, List<Point> previousLocations, int powerDivisor)
        {
            List<Point> nextLocations = new List<Point>();
            HashSet<Point> usedLocations = previousLocations == null
                ? new HashSet<Point>()
                : new HashSet<Point>(previousLocations);

            foreach (Point location in locations)
            {
                foreach (Cell cell in CurrentMap.GetCells(location, 0, 1))
                {
                    if (cell?.Objects == null) continue;
                    if (usedLocations.Contains(cell.Location)) continue;

                    foreach (MapObject target in cell.Objects.ToArray())
                    {
                        if (target?.Node == null || target.Dead || target.CurrentMap != CurrentMap) continue;
                        if (target.Race != ObjectType.Monster || !CanAttackTarget(target)) continue;
                        if (!Functions.InRange(CurrentLocation, target.CurrentLocation, Globals.MagicRange)) continue;

                        if (SEnvir.Random.Next(powerDivisor) == 0 && usedLocations.Add(cell.Location))
                            nextLocations.Add(cell.Location);

                        break;
                    }
                }
            }

            return nextLocations;
        }

        private List<MapObject> GetHeroFireBounceTargets(MapObject source, int radius)
        {
            List<MapObject> targets = new List<MapObject>();
            if (source?.CurrentMap == null) return targets;

            foreach (Cell cell in source.CurrentMap.GetCells(source.CurrentLocation, 0, radius))
            {
                if (cell?.Objects == null) continue;

                foreach (MapObject target in cell.Objects.ToArray())
                {
                    if (target?.Node == null || target.Dead || target.CurrentMap != CurrentMap) continue;
                    if (target.Race != ObjectType.Monster || target == source || !CanAttackTarget(target)) continue;

                    targets.Add(target);
                }
            }

            return targets;
        }

        private int GetHeroChainMagicPower(HeroChainMagicPayload payload)
        {
            int power = Math.Max(GetMC(), GetDC()) + Level + (payload.Magic?.GetPower() ?? 0);

            power += payload.Element switch
            {
                Element.Fire => Stats[Stat.FireAttack],
                Element.Lightning => Stats[Stat.LightningAttack],
                _ => 0,
            };

            if (payload.MagicType == MagicType.ChainLightning)
                power = power * 5 / (payload.PowerDivisor + 5);

            return Math.Max(1, power);
        }

        private bool TryGetCustomAutoSkill(out UserMagic magic, out CustomSkillInfo skill, bool requireTargetInRange = false)
        {
            magic = null;
            skill = null;

            if (Info?.Magics == null || !CanCast || (Poison & PoisonType.Silenced) == PoisonType.Silenced) return false;

            foreach (UserMagic candidate in Info.Magics
                         .Where(CanUseKnownMagic)
                         .Where(x => CurrentMP >= x.Cost && SEnvir.Now >= x.Cooldown)
                         .OrderByDescending(x => x.Info.NeedLevel1)
                         .ThenBy(x => x.Info.Index))
            {
                if (!SEnvir.CustomSystem.TryGetSkill(candidate.Info, out CustomSkillInfo candidateSkill)) continue;
                if (!CanHeroAutoCastCustomSkill(candidateSkill)) continue;
                if (requireTargetInRange && !CanUseCustomAutoSkillOnCurrentTarget(candidateSkill)) continue;

                magic = candidate;
                skill = candidateSkill;
                return true;
            }

            return false;
        }

        private bool CanUseCustomAutoSkillOnCurrentTarget(CustomSkillInfo skill)
        {
            if (skill == null || Target?.Node == null || !CanAttackTarget(Target)) return false;

            if (skill.TargetRule == CustomSkillTargetRule.Self)
            {
                int searchRadius = GetCustomSkillSearchRadius(skill);
                if (searchRadius <= 0 || !Functions.InRange(CurrentLocation, Target.CurrentLocation, searchRadius)) return false;

                MirDirection direction = CurrentLocation == Target.CurrentLocation
                    ? Direction
                    : Functions.DirectionFromPoint(CurrentLocation, Target.CurrentLocation);

                return IsCustomSkillLocationInShape(skill, CurrentLocation, Target.CurrentLocation, direction, CurrentLocation);
            }

            return Functions.InRange(CurrentLocation, Target.CurrentLocation, GetCustomSkillRange(skill));
        }

        private bool TryGetCustomSupportSkill(out UserMagic magic, out CustomSkillInfo skill, out MapObject supportTarget)
        {
            magic = null;
            skill = null;
            supportTarget = null;

            if (Info?.Magics == null || !CanCast || (Poison & PoisonType.Silenced) == PoisonType.Silenced) return false;

            foreach (UserMagic candidate in Info.Magics
                         .Where(CanUseKnownMagic)
                         .Where(x => CurrentMP >= x.Cost && SEnvir.Now >= x.Cooldown)
                         .OrderByDescending(x => x.Info.NeedLevel1)
                         .ThenBy(x => x.Info.Index))
            {
                if (!SEnvir.CustomSystem.TryGetSkill(candidate.Info, out CustomSkillInfo candidateSkill)) continue;
                if (!CanHeroAutoCastCustomSupportSkill(candidateSkill)) continue;

                MapObject candidateTarget = GetCustomSupportTarget(candidateSkill);
                if (candidateTarget == null) continue;

                magic = candidate;
                skill = candidateSkill;
                supportTarget = candidateTarget;
                return true;
            }

            return false;
        }

        private bool TryGetCustomSummonSkill(out UserMagic magic, out CustomSkillInfo skill)
        {
            magic = null;
            skill = null;

            if (Info?.Magics == null || !CanCast || (Poison & PoisonType.Silenced) == PoisonType.Silenced) return false;

            foreach (UserMagic candidate in Info.Magics
                         .Where(CanUseKnownMagic)
                         .Where(x => CurrentMP >= x.Cost && SEnvir.Now >= x.Cooldown)
                         .OrderByDescending(x => x.Info.NeedLevel1)
                         .ThenBy(x => x.Info.Index))
            {
                if (!SEnvir.CustomSystem.TryGetSkill(candidate.Info, out CustomSkillInfo candidateSkill)) continue;
                if (!CanHeroAutoCastCustomSummonSkill(candidateSkill)) continue;
                if (!CustomSummonNeedsSkill(candidateSkill)) continue;

                magic = candidate;
                skill = candidateSkill;
                return true;
            }

            return false;
        }

        private int GetCustomAutoSkillDesiredRange()
        {
            if (Info?.Magics == null || !CanCast || (Poison & PoisonType.Silenced) == PoisonType.Silenced) return 0;

            return Info.Magics
                .Where(CanUseKnownMagic)
                .Where(x => CurrentMP >= x.Cost && SEnvir.Now >= x.Cooldown)
                .Select(x => SEnvir.CustomSystem.TryGetSkill(x.Info, out CustomSkillInfo skill) ? skill : null)
                .Where(CanHeroAutoCastCustomSkill)
                .Select(GetCustomAutoSkillDesiredRange)
                .DefaultIfEmpty(0)
                .Max();
        }

        private static int GetCustomAutoSkillDesiredRange(CustomSkillInfo skill)
        {
            return skill?.TargetRule == CustomSkillTargetRule.Self
                ? GetCustomSkillSearchRadius(skill)
                : GetCustomSkillRange(skill);
        }

        private static bool CanHeroAutoCastCustomSkill(CustomSkillInfo skill)
        {
            if (skill == null || !skill.Enabled) return false;
            if (skill.Template == CustomSkillTemplate.Move)
                return skill.TargetRule is CustomSkillTargetRule.Ground or CustomSkillTargetRule.Enemy or CustomSkillTargetRule.EnemyOrGround;

            if (skill.TargetRule is not (CustomSkillTargetRule.Self or CustomSkillTargetRule.Enemy or CustomSkillTargetRule.EnemyOrGround)) return false;

            if (skill.TargetRule == CustomSkillTargetRule.Self)
            {
                return skill.Template is CustomSkillTemplate.DirectDamage
                    or CustomSkillTemplate.ProjectileDamage
                    or CustomSkillTemplate.AreaDamage
                    or CustomSkillTemplate.PersistentAreaDamage
                    or CustomSkillTemplate.Debuff
                    or CustomSkillTemplate.Control;
            }

            return skill.Template is CustomSkillTemplate.DirectDamage
                or CustomSkillTemplate.ProjectileDamage
                or CustomSkillTemplate.ChainDamage
                or CustomSkillTemplate.AreaDamage
                or CustomSkillTemplate.PersistentAreaDamage
                or CustomSkillTemplate.Debuff
                or CustomSkillTemplate.Control;
        }

        private static bool CanHeroAutoCastCustomSupportSkill(CustomSkillInfo skill)
        {
            if (skill == null || !skill.Enabled) return false;
            if (skill.TargetRule is not (CustomSkillTargetRule.Self or CustomSkillTargetRule.Ally or CustomSkillTargetRule.AllyOrSelf)) return false;

            return skill.Template is CustomSkillTemplate.Heal or CustomSkillTemplate.Buff;
        }

        private static bool CanHeroAutoCastCustomSummonSkill(CustomSkillInfo skill)
        {
            if (skill == null || !skill.Enabled || skill.SummonMonster == null) return false;
            if (skill.TargetRule is not (CustomSkillTargetRule.Self or CustomSkillTargetRule.Ally or CustomSkillTargetRule.AllyOrSelf)) return false;

            return skill.Template == CustomSkillTemplate.Summon;
        }

        private bool CustomSummonNeedsSkill(CustomSkillInfo skill)
        {
            if (skill?.SummonMonster == null || Owner == null) return false;

            int maxSummons = Math.Max(1, skill.MaxSummons);
            int existing = Owner.Pets.Count(x => x.MonsterInfo == skill.SummonMonster);

            return existing < maxSummons;
        }

        private MapObject GetCustomSupportTarget(CustomSkillInfo skill)
        {
            if (skill == null) return null;

            List<MapObject> candidates = skill.TargetRule switch
            {
                CustomSkillTargetRule.Self => new List<MapObject> { this },
                CustomSkillTargetRule.Ally => new List<MapObject> { Owner },
                CustomSkillTargetRule.AllyOrSelf => new List<MapObject> { Owner, this },
                _ => new List<MapObject>(),
            };

            return candidates
                .Where(x => x?.CurrentMap == CurrentMap && CanHelpTarget(x))
                .Where(x => CustomSupportTargetNeedsSkill(skill, x))
                .OrderBy(x => ReferenceEquals(x, Owner) ? 0 : 1)
                .ThenBy(x => Functions.Distance(CurrentLocation, x.CurrentLocation))
                .FirstOrDefault();
        }

        private bool CustomSupportTargetNeedsSkill(CustomSkillInfo skill, MapObject target)
        {
            if (skill == null || target?.Node == null || target.Dead) return false;

            switch (skill.Template)
            {
                case CustomSkillTemplate.Heal:
                    if (target.Stats[Stat.Health] <= 0 || target.CurrentHP <= 0 || target.CurrentHP >= target.Stats[Stat.Health]) return false;

                    int threshold = ReferenceEquals(target, Owner) ? 60 : 50;
                    return (long)target.CurrentHP * 100 <= (long)target.Stats[Stat.Health] * threshold;
                case CustomSkillTemplate.Buff:
                    return skill.BuffType != BuffType.None && !target.Buffs.Any(x => x.Type == skill.BuffType);
                default:
                    return false;
            }
        }

        private static int GetCustomSkillRange(CustomSkillInfo skill)
        {
            return Math.Max(1, skill?.Range > 0 ? skill.Range : Globals.MagicRange);
        }

        private int GetCustomSkillDelay(CustomSkillInfo skill, object payload)
        {
            int delay = Math.Max(0, skill?.CastDelay > 0 ? skill.CastDelay : 500);

            if (skill != null && SkillUsesProjectileTravel(skill))
            {
                Point? targetLocation = payload switch
                {
                    SkillAreaPayload area => area.Location,
                    SkillTargetPayload targetPayload when targetPayload.Target?.Node != null => targetPayload.Target.CurrentLocation,
                    SkillChainPayload chainPayload when chainPayload.Targets?.Count > 0 && chainPayload.Targets[0]?.Node != null => chainPayload.Targets[0].CurrentLocation,
                    SkillLocationPayload locationPayload => locationPayload.Location,
                    MapObject target when target?.Node != null => target.CurrentLocation,
                    List<MapObject> targets when targets.Count > 0 && targets[0]?.Node != null => targets[0].CurrentLocation,
                    Point point => point,
                    _ => null,
                };

                if (targetLocation != null)
                    delay += Functions.Distance(CurrentLocation, targetLocation.Value) * Math.Max(0, skill.ProjectileSpeed > 0 ? skill.ProjectileSpeed : Globals.ProjectileSpeed);
            }

            return Math.Max(0, delay);
        }

        private void CompleteCustomHeroSkill(UserMagic magic, CustomSkillInfo skill, object payload, int remainingTicks)
        {
            if (magic == null || skill == null || !skill.Enabled) return;

            bool applied = false;

            switch (skill.Template)
            {
                case CustomSkillTemplate.DirectDamage:
                case CustomSkillTemplate.ProjectileDamage:
                    if (!TryApplyCustomHeroTargetAreaSkill(magic, skill, payload))
                        ApplyCustomHeroDamage(magic, skill, GetCustomHeroPayloadTarget(payload));
                    applied = true;
                    break;
                case CustomSkillTemplate.ChainDamage:
                    if (payload is SkillChainPayload chainPayload)
                    {
                        ApplyCustomHeroChainDamage(magic, skill, chainPayload);
                        applied = true;
                    }
                    else if (payload is List<MapObject> chainTargets)
                    {
                        foreach (MapObject target in chainTargets)
                            ApplyCustomHeroDamage(magic, skill, target);

                        applied = true;
                    }
                    break;
                case CustomSkillTemplate.AreaDamage:
                    if (TryGetCustomHeroAreaPayload(payload, out SkillAreaPayload areaPayload))
                    {
                        ApplyCustomHeroAreaDamage(magic, skill, areaPayload);
                        applied = true;
                    }
                    break;
                case CustomSkillTemplate.PersistentAreaDamage:
                    if (TryGetCustomHeroAreaPayload(payload, out SkillAreaPayload persistentAreaPayload))
                    {
                        ApplyCustomHeroAreaDamage(magic, skill, persistentAreaPayload);

                        if (remainingTicks > 1)
                        {
                            ActionList.Add(new DelayedAction(
                                SEnvir.Now.AddMilliseconds(GetCustomSkillTickRate(skill)),
                                ActionType.DelayMagic,
                                magic.Info.Magic,
                                magic.Info.Index,
                                skill,
                                persistentAreaPayload,
                                magic,
                                remainingTicks - 1));
                        }
                    }
                    break;
                case CustomSkillTemplate.Debuff:
                    if (!TryApplyCustomHeroTargetAreaSkill(magic, skill, payload))
                        ApplyCustomHeroStatus(magic, skill, GetCustomHeroPayloadTarget(payload), applyMovement: false);
                    applied = true;
                    break;
                case CustomSkillTemplate.Control:
                    if (!TryApplyCustomHeroTargetAreaSkill(magic, skill, payload))
                        ApplyCustomHeroStatus(magic, skill, GetCustomHeroPayloadTarget(payload, out Point? origin), applyMovement: true, origin);
                    applied = true;
                    break;
                case CustomSkillTemplate.Heal:
                    applied = TryApplyCustomHeroHelpfulAreaSkill(magic, skill, payload) ||
                              ApplyCustomHeroHeal(magic, skill, GetCustomHeroPayloadTarget(payload));
                    break;
                case CustomSkillTemplate.Buff:
                    applied = TryApplyCustomHeroHelpfulAreaSkill(magic, skill, payload) ||
                              ApplyCustomHeroHelpfulBuff(magic, skill, GetCustomHeroPayloadTarget(payload));
                    break;
                case CustomSkillTemplate.Summon:
                    if (payload is SkillLocationPayload summonPayload)
                        applied = ApplyCustomHeroSummon(magic, skill, summonPayload);
                    else if (payload is Point summonPoint)
                        applied = ApplyCustomHeroSummon(magic, skill, new SkillLocationPayload { Map = CurrentMap, Location = summonPoint, Origin = CurrentLocation });
                    break;
                case CustomSkillTemplate.Move:
                    if (payload is SkillLocationPayload movePayload)
                        applied = ApplyCustomHeroMove(magic, skill, movePayload);
                    else if (payload is Point movePoint)
                        applied = ApplyCustomHeroMove(magic, skill, new SkillLocationPayload { Map = CurrentMap, Location = movePoint, Origin = CurrentLocation });
                    break;
            }

            if (applied)
                QueueNextCustomHeroSkillWave(magic, skill, payload, remainingTicks <= 0 ? GetCustomSkillWaveCount(skill) : remainingTicks);
        }

        private void QueueNextCustomHeroSkillWave(UserMagic magic, CustomSkillInfo skill, object payload, int remainingWaves)
        {
            if (!SkillSupportsWaves(skill) || remainingWaves <= 1) return;

            ActionList.Add(new DelayedAction(
                SEnvir.Now.AddMilliseconds(Math.Max(100, skill.WaveInterval <= 0 ? skill.TickRate : skill.WaveInterval)),
                ActionType.DelayMagic,
                magic.Info.Magic,
                magic.Info.Index,
                skill,
                payload,
                magic,
                remainingWaves - 1));
        }

        private static bool SkillSupportsWaves(CustomSkillInfo skill)
        {
            return skill?.Template is CustomSkillTemplate.DirectDamage
                or CustomSkillTemplate.ProjectileDamage
                or CustomSkillTemplate.ChainDamage
                or CustomSkillTemplate.AreaDamage
                or CustomSkillTemplate.Heal
                or CustomSkillTemplate.Buff
                or CustomSkillTemplate.Debuff
                or CustomSkillTemplate.Control;
        }

        private static int GetCustomSkillWaveCount(CustomSkillInfo skill)
        {
            return Math.Max(1, skill?.WaveCount > 0 ? skill.WaveCount : 1);
        }

        private bool TryGetCustomHeroAreaPayload(object payload, out SkillAreaPayload areaPayload)
        {
            areaPayload = payload switch
            {
                SkillAreaPayload area => area,
                Point point => CreateCustomHeroAreaPayload(point),
                _ => null,
            };

            return areaPayload != null;
        }

        private SkillAreaPayload CreateCustomHeroAreaPayload(Point location, MirDirection? direction = null)
        {
            return new SkillAreaPayload
            {
                Map = CurrentMap,
                Location = location,
                Origin = CurrentLocation,
                Direction = direction ?? (location == CurrentLocation ? Direction : Functions.DirectionFromPoint(CurrentLocation, location)),
            };
        }

        private SkillTargetPayload CreateCustomHeroTargetPayload(MapObject target)
        {
            return new SkillTargetPayload
            {
                Map = CurrentMap,
                Target = target,
                Origin = CurrentLocation,
            };
        }

        private MapObject GetCustomHeroPayloadTarget(object payload)
        {
            return GetCustomHeroPayloadTarget(payload, out _);
        }

        private MapObject GetCustomHeroPayloadTarget(object payload, out Point? origin)
        {
            origin = null;

            if (payload is SkillTargetPayload targetPayload)
            {
                origin = targetPayload.Origin;
                Map map = targetPayload.Map ?? CurrentMap;
                if (map == null || CurrentMap != map || targetPayload.Target?.CurrentMap != map)
                    return null;

                return targetPayload.Target;
            }

            return payload as MapObject;
        }

        private void ApplyCustomHeroChainDamage(UserMagic magic, CustomSkillInfo skill, SkillChainPayload payload)
        {
            if (payload?.Targets == null) return;
            Map map = payload.Map ?? CurrentMap;
            if (map == null || CurrentMap != map) return;

            foreach (MapObject target in payload.Targets.ToArray())
            {
                if (target?.CurrentMap != map) continue;
                ApplyCustomHeroDamage(magic, skill, target);
            }
        }

        private static bool SkillUsesProjectileTravel(CustomSkillInfo skill)
        {
            return skill?.Template == CustomSkillTemplate.ProjectileDamage
                || skill?.Effects?.Any(x => x?.Stage == CustomSkillEffectStage.Projectile) == true;
        }

        private void ApplyCustomHeroAreaDamage(UserMagic magic, CustomSkillInfo skill, SkillAreaPayload payload)
        {
            if (payload == null) return;
            Map map = payload.Map ?? CurrentMap;
            if (map == null || CurrentMap != map) return;

            Point point = payload.Location;
            if (!Functions.InRange(payload.Origin, point, GetCustomSkillRange(skill))) return;

            int remainingTargets = skill.MaxTargets > 0 ? skill.MaxTargets : int.MaxValue;

            foreach (Cell cell in map.GetCells(point, 0, GetCustomSkillSearchRadius(skill)))
            {
                if (cell?.Objects == null) continue;

                foreach (MapObject target in cell.Objects.ToArray())
                {
                    if (target?.Node == null || target.Dead || target.CurrentMap != map || !CanAttackTarget(target)) continue;
                    if (!IsCustomSkillLocationInShape(skill, point, target.CurrentLocation, payload.Direction, payload.Origin)) continue;

                    ApplyCustomHeroDamage(magic, skill, target);
                    if (remainingTargets != int.MaxValue && --remainingTargets <= 0)
                        return;
                }
            }
        }

        private bool TryApplyCustomHeroTargetAreaSkill(UserMagic magic, CustomSkillInfo skill, object payload)
        {
            if (skill == null || GetCustomSkillSearchRadius(skill) <= 0 || CurrentMap == null) return false;

            Point? center;
            MirDirection direction;
            SkillTargetPayload targetPayload = payload as SkillTargetPayload;
            MapObject payloadTarget = null;
            Point? payloadOrigin = null;

            if (targetPayload != null)
            {
                payloadTarget = GetCustomHeroPayloadTarget(targetPayload, out payloadOrigin);
                if (payloadTarget == null) return false;
            }

            if (payload is SkillAreaPayload areaPayload)
            {
                center = areaPayload.Location;
                direction = areaPayload.Direction;
            }
            else
            {
                center = payload switch
                {
                    _ when payloadTarget?.Node != null => payloadTarget.CurrentLocation,
                    MapObject target when target?.Node != null => target.CurrentLocation,
                    Point point => point,
                    _ => null,
                };

                if (center == null) return false;
                direction = payloadTarget?.Node != null && !ReferenceEquals(payloadTarget, this)
                    ? Functions.DirectionFromPoint(payloadOrigin ?? CurrentLocation, payloadTarget.CurrentLocation)
                    : center.Value == CurrentLocation
                    ? Direction
                    : Functions.DirectionFromPoint(CurrentLocation, center.Value);
            }

            SkillAreaPayload payloadArea = payload as SkillAreaPayload;
            Map map = payloadArea?.Map ?? targetPayload?.Map ?? CurrentMap;
            Point origin = payloadArea?.Origin ?? payloadOrigin ?? CurrentLocation;
            if (map == null || CurrentMap != map) return false;
            if (!Functions.InRange(origin, center.Value, GetCustomSkillRange(skill))) return false;

            int remainingTargets = skill.MaxTargets > 0 ? skill.MaxTargets : int.MaxValue;
            bool applied = false;

            foreach (Cell cell in map.GetCells(center.Value, 0, GetCustomSkillSearchRadius(skill)))
            {
                if (cell?.Objects == null) continue;

                foreach (MapObject target in cell.Objects.ToArray())
                {
                    if (target?.Node == null || target.Dead || target.CurrentMap != map || !CanAttackTarget(target)) continue;
                    if (!IsCustomSkillLocationInShape(skill, center.Value, target.CurrentLocation, direction, origin)) continue;

                    ApplyCustomHeroSkillTemplateToTarget(magic, skill, target, origin);
                    applied = true;

                    if (remainingTargets != int.MaxValue && --remainingTargets <= 0)
                        return applied;
                }
            }

            return applied;
        }

        private bool TryApplyCustomHeroHelpfulAreaSkill(UserMagic magic, CustomSkillInfo skill, object payload)
        {
            if (skill == null || GetCustomSkillSearchRadius(skill) <= 0 || CurrentMap == null) return false;

            Point? center;
            MirDirection direction;
            SkillTargetPayload targetPayload = payload as SkillTargetPayload;
            MapObject payloadTarget = null;
            Point? payloadOrigin = null;

            if (targetPayload != null)
            {
                payloadTarget = GetCustomHeroPayloadTarget(targetPayload, out payloadOrigin);
                if (payloadTarget == null) return false;
            }

            if (payload is SkillAreaPayload areaPayload)
            {
                center = areaPayload.Location;
                direction = areaPayload.Direction;
            }
            else
            {
                center = payload switch
                {
                    _ when payloadTarget?.Node != null => payloadTarget.CurrentLocation,
                    MapObject target when target?.Node != null => target.CurrentLocation,
                    Point point => point,
                    _ => null,
                };

                if (center == null) return false;
                direction = payloadTarget?.Node != null && !ReferenceEquals(payloadTarget, this)
                    ? Functions.DirectionFromPoint(payloadOrigin ?? CurrentLocation, payloadTarget.CurrentLocation)
                    : center.Value == CurrentLocation ? Direction : Functions.DirectionFromPoint(CurrentLocation, center.Value);
            }

            SkillAreaPayload payloadArea = payload as SkillAreaPayload;
            Map map = payloadArea?.Map ?? targetPayload?.Map ?? CurrentMap;
            Point origin = payloadArea?.Origin ?? payloadOrigin ?? CurrentLocation;
            if (map == null || CurrentMap != map) return false;
            if (!Functions.InRange(origin, center.Value, GetCustomSkillRange(skill))) return false;

            int remainingTargets = skill.MaxTargets > 0 ? skill.MaxTargets : int.MaxValue;
            bool applied = false;

            foreach (Cell cell in map.GetCells(center.Value, 0, GetCustomSkillSearchRadius(skill)))
            {
                if (cell?.Objects == null) continue;

                foreach (MapObject target in cell.Objects.ToArray())
                {
                    if (target?.Node == null || target.Dead || target.CurrentMap != map || !CanHelpTarget(target)) continue;
                    if (!CustomSupportTargetNeedsSkill(skill, target)) continue;
                    if (!IsCustomSkillLocationInShape(skill, center.Value, target.CurrentLocation, direction, origin)) continue;

                    applied = skill.Template switch
                    {
                        CustomSkillTemplate.Heal => ApplyCustomHeroHeal(magic, skill, target) || applied,
                        CustomSkillTemplate.Buff => ApplyCustomHeroHelpfulBuff(magic, skill, target) || applied,
                        _ => applied,
                    };

                    if (remainingTargets != int.MaxValue && --remainingTargets <= 0)
                        return applied;
                }
            }

            return applied;
        }

        private void ApplyCustomHeroSkillTemplateToTarget(UserMagic magic, CustomSkillInfo skill, MapObject target, Point? origin = null)
        {
            switch (skill.Template)
            {
                case CustomSkillTemplate.Debuff:
                    ApplyCustomHeroStatus(magic, skill, target, applyMovement: false);
                    break;
                case CustomSkillTemplate.Control:
                    ApplyCustomHeroStatus(magic, skill, target, applyMovement: true, origin);
                    break;
                default:
                    ApplyCustomHeroDamage(magic, skill, target);
                    break;
            }
        }

        private bool ApplyCustomHeroHeal(UserMagic magic, CustomSkillInfo skill, MapObject target)
        {
            if (target?.Node == null || target.CurrentMap != CurrentMap || !CanHelpTarget(target) || target.CurrentHP >= target.Stats[Stat.Health]) return false;

            int missing = target.Stats[Stat.Health] - target.CurrentHP;
            if (missing <= 0) return false;

            target.ChangeHP(Math.Min(GetCustomHeroSkillPower(magic, skill), missing));
            LevelMagic(magic);
            return true;
        }

        private bool ApplyCustomHeroHelpfulBuff(UserMagic magic, CustomSkillInfo skill, MapObject target)
        {
            if (target?.Node == null || target.CurrentMap != CurrentMap || !CanHelpTarget(target) || skill.BuffType == BuffType.None) return false;
            if (target.Buffs.Any(x => x.Type == skill.BuffType)) return false;

            TimeSpan duration = GetCustomHeroSkillDuration(magic, skill);
            TimeSpan tickRate = TimeSpan.FromMilliseconds(Math.Max(0, skill.TickRate));

            target.BuffAdd(skill.BuffType, duration, CloneStats(skill.BuffStats), skill.BuffVisible, skill.BuffPause, tickRate, skill.BuffHidden, skill.BuffExtra);
            LevelMagic(magic);
            return true;
        }

        private bool ApplyCustomHeroSummon(UserMagic magic, CustomSkillInfo skill, SkillLocationPayload payload)
        {
            if (magic == null || skill?.SummonMonster == null || Owner?.Node == null || payload == null) return false;
            Map map = payload.Map ?? CurrentMap;
            if (map == null || CurrentMap != map) return false;
            if (!CustomSummonNeedsSkill(skill)) return false;

            MonsterObject monster = MonsterObject.GetMonster(skill.SummonMonster);
            if (monster == null) return false;

            monster.PetOwner = Owner;
            monster.Master?.MinionList.Remove(monster);
            monster.Master = null;
            monster.Magics.Add(magic);
            monster.SummonLevel = magic.Level;
            monster.TameTime = skill.SummonDuration > 0 ? SEnvir.Now.AddMilliseconds(skill.SummonDuration) : SEnvir.Now.AddDays(365);
            Owner.Pets.Add(monster);

            if (!TrySpawnCustomHeroSummon(monster, map, payload.Location) &&
                !TrySpawnCustomHeroSummon(monster, map, payload.Origin))
            {
                Owner.Pets.Remove(monster);
                monster.PetOwner = null;
                monster.Magics.Remove(magic);
                return false;
            }

            monster.SetHP(monster.Stats[Stat.Health]);
            Owner.LogMilestone(MilestoneType.PetSummon, 1, monster: monster.MonsterInfo);
            LevelMagic(magic);

            return true;
        }

        private static bool TrySpawnCustomHeroSummon(MonsterObject monster, Map map, Point origin)
        {
            if (monster == null || map == null) return false;

            foreach (Cell cell in map.GetCells(origin, 0, 3, true))
            {
                if (cell == null || cell.Movements != null || cell.IsBlocking(monster, false)) continue;

                if (monster.Spawn(map, cell.Location))
                    return true;
            }

            return false;
        }

        private bool ApplyCustomHeroMove(UserMagic magic, CustomSkillInfo skill, SkillLocationPayload payload)
        {
            if (magic == null || skill == null || payload == null || !CanMove) return false;
            Map map = payload.Map ?? CurrentMap;
            if (map == null || CurrentMap != map) return false;
            if (!Functions.InRange(payload.Origin, payload.Location, GetCustomSkillRange(skill))) return false;

            Cell cell = map.GetCell(payload.Location);
            if (cell == null || cell.Movements != null || cell.IsBlocking(this, false)) return false;

            if (!Teleport(map, payload.Location, true)) return false;

            LevelMagic(magic);
            return true;
        }

        private bool TryGetCustomHeroMoveLocation(CustomSkillInfo skill, MapObject target, out Point location)
        {
            location = Point.Empty;

            if (skill == null || target?.Node == null || target.CurrentMap != CurrentMap || CurrentMap == null || !CanMove) return false;

            int currentDistance = Functions.Distance(CurrentLocation, target.CurrentLocation);
            if (currentDistance <= 1) return false;

            MirDirection baseDirection = Functions.DirectionFromPoint(target.CurrentLocation, CurrentLocation);
            int[] offsets = { 0, -1, 1, -2, 2, -3, 3, 4 };

            foreach (int offset in offsets)
            {
                MirDirection direction = Functions.ShiftDirection(baseDirection, offset);
                Point candidate = Functions.Move(target.CurrentLocation, direction);

                if (!Functions.InRange(CurrentLocation, candidate, GetCustomSkillRange(skill))) continue;
                if (Functions.Distance(candidate, target.CurrentLocation) != 1) continue;
                if (Functions.Distance(candidate, target.CurrentLocation) >= currentDistance) continue;

                Cell cell = CurrentMap.GetCell(candidate);
                if (cell == null || cell.Movements != null || cell.IsBlocking(this, false)) continue;

                location = candidate;
                return true;
            }

            return false;
        }

        private void ApplyCustomHeroDamage(UserMagic magic, CustomSkillInfo skill, MapObject target)
        {
            if (target?.Node == null || target.Dead || target.CurrentMap != CurrentMap || !CanAttackTarget(target)) return;

            int power = GetCustomHeroSkillPower(magic, skill);
            Element element = skill.Element;

            if (!skill.IgnoreArmour)
                power -= element == Element.None ? target.GetAC() : target.GetMR();

            power = ApplyCustomSkillResistance(power, target, element);
            if (power <= 0)
            {
                target.Blocked();
                return;
            }

            int damage = target.Attacked(this, power, element, false, skill.IgnoreShield);
            if (damage <= 0) return;

            LevelMagic(magic);
            LevelAttackPassives(target, magic.Info.Magic, element);
            OnAttackLanded(target, magic.Info.Magic, damage);
        }

        private List<MapObject> BuildCustomHeroChainTargets(CustomSkillInfo skill, MapObject primaryTarget)
        {
            List<MapObject> targets = new();
            if (primaryTarget?.Node == null || !CanAttackTarget(primaryTarget)) return targets;

            targets.Add(primaryTarget);

            int maxTargets = skill.MaxTargets > 0 ? skill.MaxTargets : 3;
            int jumpRadius = Math.Max(1, skill.Radius > 0 ? skill.Radius : 4);
            MapObject previous = primaryTarget;

            while (targets.Count < maxTargets)
            {
                MapObject next = GetNextCustomHeroChainTarget(previous, targets, jumpRadius);
                if (next == null) break;

                targets.Add(next);
                previous = next;
            }

            return targets;
        }

        private MapObject GetNextCustomHeroChainTarget(MapObject previous, IReadOnlyCollection<MapObject> usedTargets, int jumpRadius)
        {
            if (previous?.CurrentMap == null) return null;

            return previous.CurrentMap.GetCells(previous.CurrentLocation, 0, jumpRadius)
                .Where(x => x?.Objects != null)
                .SelectMany(x => x.Objects.ToArray())
                .Where(x => x?.Node != null && !x.Dead && !usedTargets.Contains(x) && CanAttackTarget(x))
                .OrderBy(x => Functions.Distance(previous.CurrentLocation, x.CurrentLocation))
                .FirstOrDefault();
        }

        private void ApplyCustomHeroStatus(UserMagic magic, CustomSkillInfo skill, MapObject target, bool applyMovement, Point? origin = null)
        {
            if (target?.Node == null || target.Dead || target.CurrentMap != CurrentMap || !CanAttackTarget(target)) return;

            TimeSpan duration = GetCustomHeroSkillDuration(magic, skill);
            TimeSpan tickRate = TimeSpan.FromMilliseconds(Math.Max(0, skill.TickRate));

            if (skill.BuffType != BuffType.None)
            {
                target.BuffAdd(skill.BuffType, duration, CloneStats(skill.BuffStats), skill.BuffVisible, skill.BuffPause, tickRate, skill.BuffHidden, skill.BuffExtra);
                LevelMagic(magic);
            }
            else
            {
                TimeSpan poisonTickRate = tickRate <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : tickRate;
                target.ApplyPoison(new Poison
                {
                    Owner = this,
                    Type = skill.PoisonType == PoisonType.None ? PoisonType.Paralysis : skill.PoisonType,
                    Value = Math.Max(0, skill.PoisonValue),
                    TickFrequency = poisonTickRate,
                    TickCount = Math.Max(1, (int)Math.Ceiling(duration.TotalMilliseconds / poisonTickRate.TotalMilliseconds)),
                    CanKill = skill.PoisonCanKill,
                });
                LevelMagic(magic);
            }

            if (!applyMovement) return;

            Point source = origin ?? CurrentLocation;

            if (skill.KnockbackDistance > 0)
                target.Pushed(Functions.DirectionFromPoint(source, target.CurrentLocation), skill.KnockbackDistance);

            if (skill.PullDistance > 0)
                target.Pushed(Functions.DirectionFromPoint(target.CurrentLocation, source), skill.PullDistance);
        }

        private int GetCustomHeroSkillPower(UserMagic magic, CustomSkillInfo skill)
        {
            int source = skill.PowerSource switch
            {
                CustomSkillPowerSource.DC => GetDC(),
                CustomSkillPowerSource.MC => GetMC(),
                CustomSkillPowerSource.SC => GetSC(),
                CustomSkillPowerSource.Fixed => 0,
                _ => GetCustomHeroAutoPowerSource(magic, skill),
            };

            int power = skill.FixedPower + magic.GetPower() + source * Math.Max(0, skill.PowerRate) / 100;

            return Math.Max(1, power);
        }

        private TimeSpan GetCustomHeroSkillDuration(UserMagic magic, CustomSkillInfo skill)
        {
            int duration = Math.Max(0, skill.Duration);

            if (skill.DurationPowerRate > 0)
                duration += GetCustomHeroSkillPower(magic, skill) * skill.DurationPowerRate;

            return TimeSpan.FromMilliseconds(Math.Max(1000, duration));
        }

        private int GetCustomHeroAutoPowerSource(UserMagic magic, CustomSkillInfo skill)
        {
            return skill.Element switch
            {
                Element.Holy or Element.Dark or Element.Phantom => GetSC(),
                Element.None when magic.Info.School is MagicSchool.Physical or MagicSchool.Atrocity or MagicSchool.Kill or MagicSchool.Assassination => GetDC(),
                _ => GetMC(),
            };
        }

        private static int GetCustomSkillTickRate(CustomSkillInfo skill)
        {
            return Math.Max(100, skill.TickRate);
        }

        private static int GetCustomSkillTickCount(CustomSkillInfo skill)
        {
            int tickRate = GetCustomSkillTickRate(skill);
            int duration = skill.Duration <= 0 ? tickRate : skill.Duration;
            return Math.Max(1, (int)Math.Ceiling(duration / (double)tickRate));
        }

        private static int GetCustomSkillSearchRadius(CustomSkillInfo skill)
        {
            return skill.Shape switch
            {
                CustomSkillShape.Line or CustomSkillShape.Cone or CustomSkillShape.Rectangle => Math.Max(Math.Max(skill.Range, skill.Length), skill.Radius),
                _ => Math.Max(0, skill.Radius),
            };
        }

        private bool IsCustomSkillLocationInShape(CustomSkillInfo skill, Point center, Point location, MirDirection direction, Point origin)
        {
            if (skill.Shape == CustomSkillShape.Circle)
                return Functions.Distance(center, location) <= Math.Max(0, skill.Radius);

            (double fx, double fy) = GetCustomSkillDirectionVector(direction);
            double vx = location.X - origin.X;
            double vy = location.Y - origin.Y;
            double forward = vx * fx + vy * fy;
            double side = Math.Abs(vx * -fy + vy * fx);
            double length = Math.Max(1, skill.Length > 0 ? skill.Length : Math.Max(skill.Range, skill.Radius));

            return skill.Shape switch
            {
                CustomSkillShape.Line => forward >= 0 && forward <= length && side <= Math.Max(0.5D, (skill.Width <= 0 ? 1 : skill.Width) / 2D),
                CustomSkillShape.Rectangle => forward >= 0 && forward <= length && side <= Math.Max(0.5D, (skill.Width <= 0 ? Math.Max(1, skill.Radius) : skill.Width) / 2D),
                CustomSkillShape.Cone => IsInsideCustomSkillCone(skill, vx, vy, fx, fy, length),
                _ => true,
            };
        }

        private static bool IsInsideCustomSkillCone(CustomSkillInfo skill, double vx, double vy, double fx, double fy, double length)
        {
            double distance = Math.Sqrt(vx * vx + vy * vy);
            if (distance <= 0 || distance > length) return false;

            double dot = (vx / distance) * fx + (vy / distance) * fy;
            double minDot = Math.Cos((Math.Max(1, skill.Angle <= 0 ? 90 : skill.Angle) / 2D) * Math.PI / 180D);
            return dot >= minDot;
        }

        private static (double X, double Y) GetCustomSkillDirectionVector(MirDirection direction)
        {
            return direction switch
            {
                MirDirection.Up => (0, -1),
                MirDirection.UpRight => (Math.Sqrt(0.5D), -Math.Sqrt(0.5D)),
                MirDirection.Right => (1, 0),
                MirDirection.DownRight => (Math.Sqrt(0.5D), Math.Sqrt(0.5D)),
                MirDirection.Down => (0, 1),
                MirDirection.DownLeft => (-Math.Sqrt(0.5D), Math.Sqrt(0.5D)),
                MirDirection.Left => (-1, 0),
                MirDirection.UpLeft => (-Math.Sqrt(0.5D), -Math.Sqrt(0.5D)),
                _ => (0, 1),
            };
        }

        private static int ApplyCustomSkillResistance(int power, MapObject target, Element element)
        {
            int res = target.Stats.GetResistanceValue(element);

            if (res > 0)
                power -= power * res / 10;
            else if (res < 0)
                power -= power * res / 5;

            return power;
        }

        private static Stats CloneStats(Stats stats)
        {
            Stats result = new();
            if (stats == null) return result;

            foreach (KeyValuePair<Stat, int> pair in stats.Values)
                result[pair.Key] = pair.Value;

            return result;
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
                Stats[Stat.Invincibility] > 0 || Superman) return 0;

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

            if (power > 0)
                LevelDefencePassives(element);

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
            if (!AcceptsOwnerEngagedTarget()) return;
            if (!CanAcceptOwnerTarget(target)) return;

            if (Behaviour == HeroBehaviour.Attack || IsFocusOwnerTargetCustomBehaviour())
            {
                Target = target;
                SearchTime = DateTime.MinValue;
                return;
            }

            if (!ShouldReplaceTarget(target, 5500)) return;

            Target = target;
            SearchTime = DateTime.MinValue;
        }

        private bool CanSearchForBehaviour()
        {
            return Behaviour switch
            {
                HeroBehaviour.Follow => false,
                HeroBehaviour.CounterAttack => Target != null,
                HeroBehaviour.Custom => IsAttackCompatibleCustomBehaviour(),
                _ => true
            };
        }

        private bool AcceptsOwnerEngagedTarget()
        {
            return Behaviour == HeroBehaviour.Attack ||
                   IsAttackCompatibleCustomBehaviour() ||
                   IsFocusOwnerTargetCustomBehaviour();
        }

        private bool UsesActiveAttackBehaviour()
        {
            return Behaviour == HeroBehaviour.Attack || IsAttackCompatibleCustomBehaviour();
        }

        private bool IsAttackCompatibleCustomBehaviour()
        {
            return Behaviour == HeroBehaviour.Custom &&
                   Owner?.Character?.HeroCustomBehaviourProfile == HeroCustomBehaviourProfile.AttackCompatible;
        }

        private bool IsFocusOwnerTargetCustomBehaviour()
        {
            return Behaviour == HeroBehaviour.Custom &&
                   Owner?.Character?.HeroCustomBehaviourProfile == HeroCustomBehaviourProfile.FocusOwnerTarget;
        }

        protected virtual bool CanPursueTarget(MapObject target)
        {
            if (target?.Node == null || target.CurrentMap != CurrentMap) return false;
            if (Functions.InRange(CurrentLocation, target.CurrentLocation, MaxPursuitRange)) return true;

            return Owner?.Node != null &&
                   Owner.CurrentMap == CurrentMap &&
                   Functions.InRange(Owner.CurrentLocation, target.CurrentLocation, MaxPursuitRange);
        }

        protected virtual bool CanAcceptOwnerTarget(MapObject target)
        {
            if (Owner?.Node == null || target?.Node == null || target.CurrentMap != CurrentMap || Owner.CurrentMap != CurrentMap) return false;
            if (!Functions.InRange(Owner.CurrentLocation, target.CurrentLocation, MaxPursuitRange)) return false;

            return CanAttackTarget(target);
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

            Dictionary<SetInfo, List<ItemInfo>> sets = new Dictionary<SetInfo, List<ItemInfo>>();

            foreach (UserItem item in Equipment)
            {
                if (item == null || item.CurrentDurability == 0 && item.Info.Durability > 0) continue;

                if (item.Info.Set != null)
                {
                    if (!sets.TryGetValue(item.Info.Set, out List<ItemInfo> items))
                        sets[item.Info.Set] = items = new List<ItemInfo>();

                    if (!items.Contains(item.Info))
                        items.Add(item.Info);
                }

                Stats.Add(item.Info.Stats, item.Info.ItemType != ItemType.Weapon);
                Stats.Add(item.Stats, item.Info.ItemType != ItemType.Weapon);

                if (item.Info.ItemType != ItemType.Weapon) continue;

                Stat element = item.Stats.GetWeaponElement();

                if (element == Stat.None)
                    element = item.Info.Stats.GetWeaponElement();

                if (element != Stat.None)
                    Stats[element] += item.Stats.GetWeaponElementValue() + item.Info.Stats.GetWeaponElementValue();
            }

            ApplyPassiveMagicStats();

            foreach (BuffInfo buff in Buffs)
            {
                if (buff.Pause) continue;

                if (buff.Type == BuffType.ItemBuff)
                {
                    ItemInfo info = SEnvir.ItemInfoList.Binding.FirstOrDefault(x => x.Index == buff.ItemIndex);
                    if (info == null) continue;

                    Stats.Add(info.Stats);
                    continue;
                }

                if (buff.Stats == null) continue;

                Stats.Add(buff.Stats);
            }

            foreach (KeyValuePair<SetInfo, List<ItemInfo>> pair in sets)
            {
                if (pair.Key.Items.Count != pair.Value.Count) continue;

                foreach (SetInfoStat stat in pair.Key.SetStats)
                {
                    if (Level < stat.Level) continue;

                    switch (Class)
                    {
                        case MirClass.Warrior:
                            if ((stat.Class & RequiredClass.Warrior) != RequiredClass.Warrior) continue;
                            break;
                        case MirClass.Wizard:
                            if ((stat.Class & RequiredClass.Wizard) != RequiredClass.Wizard) continue;
                            break;
                        case MirClass.Taoist:
                            if ((stat.Class & RequiredClass.Taoist) != RequiredClass.Taoist) continue;
                            break;
                        case MirClass.Assassin:
                            if ((stat.Class & RequiredClass.Assassin) != RequiredClass.Assassin) continue;
                            break;
                    }

                    Stats[stat.Stat] += stat.Amount;
                }
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
            ApplyGradeGrowthStats();
            Stats[Stat.BaseHealth] = Stats[Stat.Health];
            Stats[Stat.BaseMana] = Stats[Stat.Mana];
        }

        private void ApplyGradeGrowthStats()
        {
            int maxGrade = Math.Max(0, Math.Min(4, Config.HeroMaxGrade));
            int grade = Math.Min(Grade, maxGrade);
            int bonusRate = Math.Max(0, Math.Min(20, Config.HeroGradeStatBonusRate));
            int rate = grade * bonusRate;

            if (rate <= 0) return;

            ApplyGradeStatBonus(Stat.Health, rate);
            ApplyGradeStatBonus(Stat.Mana, rate);
            ApplyGradeStatBonus(Stat.Accuracy, rate);
            ApplyGradeStatBonus(Stat.Agility, rate);
            ApplyGradeStatBonus(Stat.MinAC, rate);
            ApplyGradeStatBonus(Stat.MaxAC, rate);
            ApplyGradeStatBonus(Stat.MinMR, rate);
            ApplyGradeStatBonus(Stat.MaxMR, rate);
            ApplyGradeStatBonus(Stat.MinDC, rate);
            ApplyGradeStatBonus(Stat.MaxDC, rate);
            ApplyGradeStatBonus(Stat.MinMC, rate);
            ApplyGradeStatBonus(Stat.MaxMC, rate);
            ApplyGradeStatBonus(Stat.MinSC, rate);
            ApplyGradeStatBonus(Stat.MaxSC, rate);
        }

        private void ApplyGradeStatBonus(Stat stat, int rate)
        {
            int value = Stats[stat];
            if (value <= 0) return;

            Stats[stat] = value + value * rate / 100;
        }

        private void ApplyPassiveMagicStats()
        {
            if (Info?.Magics == null) return;

            foreach (UserMagic magic in Info.Magics)
            {
                if (!CanUseKnownMagic(magic)) continue;

                Stats stats = GetPassiveMagicStats(magic);
                if (stats == null) continue;

                Stats.Add(stats);
            }
        }

        private Stats GetPassiveMagicStats(UserMagic magic)
        {
            switch (magic.Info.Magic)
            {
                case MagicType.Swordsmanship:
                case MagicType.SpiritSword:
                case MagicType.VineTreeDance:
                    return new Stats { [Stat.Accuracy] = magic.GetPower() };
                case MagicType.Slaying:
                    return new Stats
                    {
                        [Stat.Accuracy] = magic.Level * 2,
                        [Stat.MinDC] = magic.Level * 2,
                        [Stat.MaxDC] = magic.Level * 2,
                    };
                case MagicType.DefensiveMastery:
                    return new Stats { [Stat.DefensiveMastery] = magic.GetPower() };
                case MagicType.Renounce:
                    return new Stats { [Stat.MCPercent] = (1 + magic.Level) * 10 };
                case MagicType.WillowDance:
                    return new Stats { [Stat.Agility] = magic.GetPower() };
                case MagicType.AdventOfDemon:
                    return new Stats { [Stat.MaxAC] = magic.GetPower() };
                case MagicType.AdventOfDevil:
                    return new Stats { [Stat.MaxMR] = magic.GetPower() };
                case MagicType.BloodyFlower:
                    return new Stats { [Stat.LifeSteal] = magic.GetPower() };
                case MagicType.Discipline:
                    int power = magic.GetPower();
                    return new Stats
                    {
                        [Stat.Accuracy] = power / 3,
                        [Stat.MinDC] = power,
                    };
                default:
                    return null;
            }
        }

        public void GainExperience(decimal amount, bool rateEffected = true)
        {
            if (rateEffected)
                amount *= 1M + Config.HeroExperienceRate / 100M;

            if (amount <= 0) return;

            Info.Experience += amount;
            Owner?.Enqueue(new S.GainHeroExperience { Amount = amount, Experience = Info.Experience, MaxExperience = MaxExperience });

            ProcessExperience();
        }

        public void ProcessExperience()
        {
            ApplyLevelLimit();

            while (MaxExperience > 0 && Info.Experience >= MaxExperience && Level < MaxAllowedLevel)
            {
                Info.Experience -= MaxExperience;
                Level++;

                HeroLevelUp();
            }
        }

        public void ApplyLevelLimit(bool notify = true)
        {
            if (Level <= MaxAllowedLevel) return;

            Level = MaxAllowedLevel;

            if (!notify) return;

            RefreshStats();

            if (CurrentHP > Stats[Stat.Health]) SetHP(Stats[Stat.Health]);
            if (CurrentMP > Stats[Stat.Mana]) SetMP(Stats[Stat.Mana]);

            Owner?.Enqueue(new S.HeroLevelChanged { Level = Level, Experience = Info.Experience, MaxExperience = MaxExperience });
            Owner?.Enqueue(new S.HeroInformation { Hero = ToClientHero() });
        }

        private void HeroLevelUp()
        {
            RefreshStats();
            SetHP(Stats[Stat.Health]);
            SetMP(Stats[Stat.Mana]);

            Broadcast(new S.ObjectLeveled { ObjectID = ObjectID });
            Owner?.Enqueue(new S.HeroLevelChanged { Level = Level, Experience = Info.Experience, MaxExperience = MaxExperience });
            Owner?.Enqueue(new S.HeroInformation { Hero = ToClientHero() });
            Owner?.Connection?.ReceiveChat($"{Name}升到了 {Level} 级。", MessageType.System);
        }

        public void LevelMagic(UserMagic magic)
        {
            if (!CanUseKnownMagic(magic)) return;

            int maxExperience;
            switch (magic.Level)
            {
                case 0:
                    if (Level < magic.Info.NeedLevel1) return;

                    maxExperience = magic.Info.Experience1;
                    break;
                case 1:
                    if (Level < magic.Info.NeedLevel2) return;

                    maxExperience = magic.Info.Experience2;
                    break;
                case 2:
                    if (Level < magic.Info.NeedLevel3) return;

                    maxExperience = magic.Info.Experience3;
                    break;
                default:
                    return;
            }

            int experience = (SEnvir.Random.Next(Config.SkillExp) + 1) * Math.Max(1, Stats[Stat.SkillRate]);
            magic.Experience += experience;

            bool leveled = false;
            if (magic.Experience >= maxExperience)
            {
                magic.Experience -= maxExperience;
                magic.Level++;
                leveled = true;

                RefreshStats();
            }

            Owner?.Enqueue(new S.HeroMagicLeveled
            {
                HeroIndex = Info.Index,
                InfoIndex = magic.Info.Index,
                Level = magic.Level,
                Experience = magic.Experience,
            });

            if (leveled)
                SendInformation();
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
            if (Superman)
            {
                CurrentHP = Stats[Stat.Health];
                ClampHealthMana();
                return;
            }

            base.SetHP(amount);
            ClampHealthMana();
        }

        public override void ChangeHP(int amount)
        {
            if (Superman)
            {
                CurrentHP = Stats[Stat.Health];
                ClampHealthMana();
                return;
            }

            base.ChangeHP(amount);
            ClampHealthMana();
        }

        public override void SetMP(int amount)
        {
            if (Superman)
            {
                CurrentMP = Stats[Stat.Mana];
                ClampHealthMana();
                return;
            }

            base.SetMP(amount);
            ClampHealthMana();
        }

        public override void ChangeMP(int amount)
        {
            if (Superman)
            {
                CurrentMP = Stats[Stat.Mana];
                ClampHealthMana();
                return;
            }

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

        protected virtual bool TryCastClassSkill()
        {
            return false;
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

        protected virtual bool ShouldRetreatFromTarget()
        {
            if (RangedRetreatDistance <= 0 || Target?.Node == null || Owner?.Node == null) return false;
            if (!CanMove || !CanUseAutoMagicForRange) return false;
            if (!Functions.InRange(CurrentLocation, Target.CurrentLocation, RangedRetreatDistance)) return false;

            return Functions.Distance(CurrentLocation, Owner.CurrentLocation) <= MaxPursuitRange;
        }

        protected bool TryRetreatFromTarget()
        {
            if (Target?.CurrentMap != CurrentMap) return false;

            MirDirection direction = Functions.DirectionFromPoint(Target.CurrentLocation, CurrentLocation);
            if (Target.CurrentLocation == CurrentLocation && Owner != null)
                direction = Functions.DirectionFromPoint(Owner.CurrentLocation, CurrentLocation);

            int[] shifts = { 0, -1, 1, -2, 2 };
            foreach (int shift in shifts)
            {
                MirDirection retreatDirection = Functions.ShiftDirection(direction, shift);
                Point location = Functions.Move(CurrentLocation, retreatDirection);

                if (Owner != null && Functions.Distance(location, Owner.CurrentLocation) > MaxPursuitRange) continue;
                if (Walk(retreatDirection)) return true;
            }

            return false;
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

                    if (hero.Owner == null || hero.Owner == Owner || hero.GameMaster) return false;
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

            bool oldSafeZone = InSafeZone;
            InSafeZone = CurrentCell.SafeZone != null;

            if (oldSafeZone != InSafeZone)
                PauseItemBuffs();
        }

        private void PauseItemBuffs()
        {
            bool changed = false;

            foreach (BuffInfo buff in Buffs)
            {
                if (buff.Type != BuffType.ItemBuff) continue;

                bool pause = buff.RemainingTime != TimeSpan.MaxValue && InSafeZone;
                if (buff.Pause == pause) continue;

                buff.Pause = pause;
                changed = true;
            }

            if (!changed) return;

            RefreshStats();
            SendInformation();
        }

        public override void BuffRemove(BuffInfo info)
        {
            BuffType type = info.Type;

            base.BuffRemove(info);

            if (type != BuffType.ItemBuff) return;

            RefreshStats();
            RemoveAllObjects();
            SendInformation();
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
            return GetUseItemFailureMessage(item) == null;
        }

        public string GetUseItemFailureMessage(UserItem item)
        {
            if (item?.Info == null) return "物品无效。";
            if ((item.Flags & UserItemFlags.Marriage) == UserItemFlags.Marriage) return "婚戒不能由英雄使用。";
            if ((item.Flags & UserItemFlags.GameMaster) == UserItemFlags.GameMaster && Owner?.Character?.Account?.TempAdmin != true) return "GM 专属物品不能由英雄使用。";
            if (item.Info.Durability > 0 && item.CurrentDurability <= 0) return "物品耐久不足。";

            switch (Gender)
            {
                case MirGender.Male:
                    if ((item.Info.RequiredGender & RequiredGender.Male) != RequiredGender.Male)
                        return "英雄性别不符合。";
                    break;
                case MirGender.Female:
                    if ((item.Info.RequiredGender & RequiredGender.Female) != RequiredGender.Female)
                        return "英雄性别不符合。";
                    break;
            }

            switch (Class)
            {
                case MirClass.Warrior:
                    if ((item.Info.RequiredClass & RequiredClass.Warrior) != RequiredClass.Warrior)
                        return "英雄职业不符合。";
                    break;
                case MirClass.Wizard:
                    if ((item.Info.RequiredClass & RequiredClass.Wizard) != RequiredClass.Wizard)
                        return "英雄职业不符合。";
                    break;
                case MirClass.Taoist:
                    if ((item.Info.RequiredClass & RequiredClass.Taoist) != RequiredClass.Taoist)
                        return "英雄职业不符合。";
                    break;
                case MirClass.Assassin:
                    if ((item.Info.RequiredClass & RequiredClass.Assassin) != RequiredClass.Assassin)
                        return "英雄职业不符合。";
                    break;
            }

            switch (item.Info.RequiredType)
            {
                case RequiredType.Level:
                    if (Level < item.Info.RequiredAmount) return $"英雄等级不足，需要 {item.Info.RequiredAmount} 级。";
                    break;
                case RequiredType.MaxLevel:
                    if (Level > item.Info.RequiredAmount) return $"英雄等级过高，需要不高于 {item.Info.RequiredAmount} 级。";
                    break;
                case RequiredType.AC:
                    if (Stats[Stat.MaxAC] < item.Info.RequiredAmount) return $"英雄防御不足，需要 {item.Info.RequiredAmount}。";
                    break;
                case RequiredType.MR:
                    if (Stats[Stat.MaxMR] < item.Info.RequiredAmount) return $"英雄魔防不足，需要 {item.Info.RequiredAmount}。";
                    break;
                case RequiredType.DC:
                    if (Stats[Stat.MaxDC] < item.Info.RequiredAmount) return $"英雄攻击不足，需要 {item.Info.RequiredAmount}。";
                    break;
                case RequiredType.MC:
                    if (Stats[Stat.MaxMC] < item.Info.RequiredAmount) return $"英雄魔法不足，需要 {item.Info.RequiredAmount}。";
                    break;
                case RequiredType.SC:
                    if (Stats[Stat.MaxSC] < item.Info.RequiredAmount) return $"英雄道术不足，需要 {item.Info.RequiredAmount}。";
                    break;
                case RequiredType.Health:
                    if (Stats[Stat.Health] < item.Info.RequiredAmount) return $"英雄生命不足，需要 {item.Info.RequiredAmount}。";
                    break;
                case RequiredType.Mana:
                    if (Stats[Stat.Mana] < item.Info.RequiredAmount) return $"英雄魔法值不足，需要 {item.Info.RequiredAmount}。";
                    break;
                case RequiredType.Accuracy:
                    if (Stats[Stat.Accuracy] < item.Info.RequiredAmount) return $"英雄命中不足，需要 {item.Info.RequiredAmount}。";
                    break;
                case RequiredType.Agility:
                    if (Stats[Stat.Agility] < item.Info.RequiredAmount) return $"英雄敏捷不足，需要 {item.Info.RequiredAmount}。";
                    break;
                case RequiredType.RebirthLevel:
                case RequiredType.MaxRebirthLevel:
                case RequiredType.CompanionLevel:
                case RequiredType.MaxCompanionLevel:
                    return "英雄不支持该物品的使用条件。";
            }

            switch (item.Info.ItemEffect)
            {
                case ItemEffect.PickAxe:
                case ItemEffect.FishingRod:
                case ItemEffect.MagicRing:
                    return "该工具不能由英雄使用。";
            }

            if (item.Info.ItemType == ItemType.Book)
            {
                MagicInfo magic = SEnvir.GetMagicInfo(item.Info.Shape);
                if (magic == null || magic.School == MagicSchool.None) return "英雄不能学习这本技能书。";
                if (TryGetMagic(magic.Magic, out UserMagic userMagic) && userMagic.Level < 3) return "英雄已经学会该技能。";
                if (userMagic != null && (item.Flags & UserItemFlags.NonRefinable) == UserItemFlags.NonRefinable) return "这本技能书不能用于英雄进阶。";
            }

            if (item.Info.ItemType == ItemType.Consumable && !CanUseConsumableItem(item.Info))
                return "英雄不能使用该消耗品。";

            return null;
        }

        private bool CanUseConsumableItem(ItemInfo info)
        {
            switch (info.Shape)
            {
                case 0:
                case 14:
                case 15:
                    return true;
                case 1:
                    switch (info.ItemEffect)
                    {
                        case ItemEffect.DestructionElixir:
                        case ItemEffect.HasteElixir:
                        case ItemEffect.LifeElixir:
                        case ItemEffect.ManaElixir:
                        case ItemEffect.NatureElixir:
                        case ItemEffect.SpiritElixir:
                            return !Buffs.Any(x => x.Type == BuffType.ItemBuff && x.ItemIndex == info.Index && x.RemainingTime == TimeSpan.MaxValue);
                        default:
                            return false;
                    }
                default:
                    return false;
            }
        }

        private bool TryGetMagic(MagicType magicType, out UserMagic magic)
        {
            magic = Info?.Magics?.FirstOrDefault(x => x?.Info?.Magic == magicType);
            return magic != null;
        }

        protected bool TryGetUsableMagic(MagicType magicType, out UserMagic magic)
        {
            return TryGetMagic(magicType, out magic) && CanUseKnownMagic(magic);
        }

        private bool CanUseKnownMagic(UserMagic magic)
        {
            return magic?.Info != null &&
                   magic.Hero == Info &&
                   !magic.ItemRequired &&
                   magic.Info.School != MagicSchool.None &&
                   Level >= magic.Info.NeedLevel1;
        }

        private void LevelAttackMagic(MagicType magicType)
        {
            if (magicType == MagicType.None) return;

            if (TryGetUsableMagic(magicType, out UserMagic magic))
                LevelMagic(magic);

            switch (magicType)
            {
                case MagicType.FireBall:
                case MagicType.FireBounce:
                    if (TryGetUsableMagic(MagicType.Burning, out UserMagic burning))
                        LevelMagic(burning);
                    break;
                case MagicType.ChainLightning:
                    if (TryGetUsableMagic(MagicType.Shocked, out UserMagic shocked))
                        LevelMagic(shocked);
                    break;
            }
        }

        private void LevelAttackPassives(MapObject target, MagicType magicType, Element element)
        {
            if (target?.Node == null) return;

            bool physical = element == Element.None;
            bool melee = physical && Functions.InRange(CurrentLocation, target.CurrentLocation, 1);

            switch (Class)
            {
                case MirClass.Warrior:
                    if (!melee) break;

                    LevelKnownMagic(MagicType.Swordsmanship);

                    if (SEnvir.Random.Next(5) == 0)
                        LevelKnownMagic(MagicType.Slaying);
                    break;
                case MirClass.Wizard:
                    if (magicType != MagicType.None)
                        LevelKnownMagic(MagicType.Renounce);
                    break;
                case MirClass.Taoist:
                    if (melee)
                        LevelKnownMagic(MagicType.SpiritSword);
                    break;
                case MirClass.Assassin:
                    if (!melee) break;

                    LevelKnownMagic(MagicType.VineTreeDance);
                    LevelKnownMagic(MagicType.Discipline);

                    if (Stats[Stat.LifeSteal] > 0)
                        LevelKnownMagic(MagicType.BloodyFlower);
                    break;
            }
        }

        private void LevelDefencePassives(Element element)
        {
            switch (Class)
            {
                case MirClass.Warrior:
                    LevelKnownMagic(MagicType.DefensiveMastery);
                    break;
                case MirClass.Assassin:
                    LevelKnownMagic(MagicType.WillowDance);

                    if (element == Element.None)
                        LevelKnownMagic(MagicType.AdventOfDemon);
                    else
                        LevelKnownMagic(MagicType.AdventOfDevil);
                    break;
            }
        }

        private void LevelKnownMagic(MagicType magicType)
        {
            if (TryGetUsableMagic(magicType, out UserMagic magic))
                LevelMagic(magic);
        }

        public bool CanWearItem(UserItem item, EquipmentSlot slot)
        {
            return GetWearItemFailureMessage(item, slot) == null;
        }

        public string GetWearItemFailureMessage(UserItem item, EquipmentSlot slot)
        {
            if (item?.Info == null) return "物品无效。";
            if (!Functions.CorrectSlot(item.Info.ItemType, slot)) return "该物品不能装备到这个英雄装备位。";

            string useFailure = GetUseItemFailureMessage(item);
            if (useFailure != null) return useFailure;

            switch (slot)
            {
                case EquipmentSlot.HorseArmour:
                case EquipmentSlot.Hook:
                case EquipmentSlot.Float:
                case EquipmentSlot.Bait:
                case EquipmentSlot.Finder:
                case EquipmentSlot.Reel:
                    return "英雄暂不支持该装备位。";
            }

            switch (item.Info.ItemType)
            {
                case ItemType.HorseArmour:
                case ItemType.Hook:
                case ItemType.Float:
                case ItemType.Bait:
                case ItemType.Finder:
                case ItemType.Reel:
                    return "英雄暂不支持该类装备。";
                case ItemType.Weapon:
                case ItemType.Torch:
                case ItemType.Shield:
                    if (HandWeight - (Equipment[(int)slot]?.Weight ?? 0) + item.Weight > Stats[Stat.HandWeight]) return "英雄腕力不足。";
                    break;
                default:
                    if (WearWeight - (Equipment[(int)slot]?.Weight ?? 0) + item.Weight > Stats[Stat.WearWeight]) return "英雄穿戴负重不足。";
                    break;
            }

            return null;
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

            if (Info.HPItemIndex > 0 &&
                Stats[Stat.Health] > 0 &&
                (long)CurrentHP * 100 <= (long)Stats[Stat.Health] * Math.Max(1, (int)Info.AutoHPPercent))
            {
                usedHealthPotion = UseAutoPotion(Info.HPItemIndex, true, out bool missing);

                if (missing)
                    ClearAutoPotion(true, "英雄HP自动喝药的药品不可用或已不存在，设置已清空。");
            }

            if (Info.MPItemIndex > 0 &&
                (!usedHealthPotion || Info.MPItemIndex != Info.HPItemIndex) &&
                Stats[Stat.Mana] > 0 &&
                (long)CurrentMP * 100 <= (long)Stats[Stat.Mana] * Math.Max(1, (int)Info.AutoMPPercent))
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

            string failureMessage = GetUseItemFailureMessage(item);
            if (failureMessage != null)
            {
                Owner.Connection?.ReceiveChat($"{Name}不能使用{item.Info.ItemName}：{failureMessage}", MessageType.System);
                return;
            }

            bool checkAutoPotion = false;

            switch (item.Info.ItemType)
            {
                case ItemType.Consumable:
                    if (SEnvir.Now < UseItemTime && item.Info.ItemEffect != ItemEffect.ElixirOfPurification) return;

                    switch (item.Info.Shape)
                    {
                        case 0:
                            ChangeHP(item.Info.Stats[Stat.Health]);
                            ChangeMP(item.Info.Stats[Stat.Mana]);

                            if (item.Info.Stats[Stat.Experience] > 0)
                                GainExperience(item.Info.Stats[Stat.Experience]);
                            break;
                        case 1:
                            if (!ItemBuffAdd(item.Info)) return;
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

                    checkAutoPotion = true;
                    break;
                case ItemType.Book:
                    if (SEnvir.Now < UseItemTime) return;
                    if (!UseBook(item)) return;
                    break;
                default:
                    return;
            }

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
            if (checkAutoPotion)
                CheckAutoPotionAfterUse(item.Info.Index, item.Info.ItemName);
            SendInformation();
        }

        private bool ItemBuffAdd(ItemInfo info)
        {
            if (!CanUseConsumableItem(info)) return false;

            for (int i = Buffs.Count - 1; i >= 0; i--)
            {
                BuffInfo buff = Buffs[i];
                if (buff.Type != BuffType.ItemBuff || info.Index == buff.ItemIndex) continue;

                ItemInfo buffItemInfo = SEnvir.ItemInfoList.Binding.FirstOrDefault(x => x.Index == buff.ItemIndex);
                if (buffItemInfo?.ItemEffect == info.ItemEffect)
                    BuffRemove(buff);
            }

            BuffInfo currentBuff = Buffs.FirstOrDefault(x => x.Type == BuffType.ItemBuff && x.ItemIndex == info.Index);

            if (currentBuff != null)
            {
                if (info.Stats[Stat.Duration] >= 0)
                {
                    TimeSpan duration = TimeSpan.FromSeconds(info.Stats[Stat.Duration]);
                    long ticks = currentBuff.RemainingTime.Ticks - long.MaxValue + duration.Ticks;

                    currentBuff.RemainingTime = ticks >= 0 ? TimeSpan.MaxValue : currentBuff.RemainingTime + duration;
                }
                else
                    currentBuff.RemainingTime = TimeSpan.MaxValue;

                return true;
            }

            currentBuff = SEnvir.BuffInfoList.CreateNewObject();

            currentBuff.Type = BuffType.ItemBuff;
            currentBuff.ItemIndex = info.Index;
            currentBuff.RemainingTime = info.Stats[Stat.Duration] > 0 ? TimeSpan.FromSeconds(info.Stats[Stat.Duration]) : TimeSpan.MaxValue;
            currentBuff.Pause = InSafeZone;
            currentBuff.IsTemporary = true;

            Buffs.Add(currentBuff);

            RefreshStats();
            AddAllObjects();

            return true;
        }

        private bool UseBook(UserItem item)
        {
            MagicInfo info = SEnvir.GetMagicInfo(item.Info.Shape);
            if (info == null || info.School == MagicSchool.None) return false;

            TryGetMagic(info.Magic, out UserMagic magic);

            if (magic != null)
            {
                if (magic.ItemRequired)
                    magic.ItemRequired = false;

                if (magic.Level < 3) return false;

                if (magic.Level >= Globals.MagicMaxLevel)
                {
                    Owner.Connection.ReceiveChatWithObservers(con => string.Format(con.Language.MagicMaxLevelReached, magic.Info.Name), MessageType.System);
                    return false;
                }
            }

            if (SEnvir.Random.Next(100) >= item.CurrentDurability)
            {
                Owner.Connection.ReceiveChatWithObservers(con => con.Language.LearnBookFailed, MessageType.System);
                return true;
            }

            if (magic != null)
            {
                int rate = (magic.Level - 2) * 500;

                magic.Experience += item.CurrentDurability;

                if (magic.Experience >= rate || (magic.Level == 3 && SEnvir.Random.Next(rate) == 0))
                {
                    magic.Level++;
                    magic.Experience = 0;

                    Owner.Connection.ReceiveChatWithObservers(con => string.Format(con.Language.LearnBook4Success, magic.Info.Name, magic.Level), MessageType.System);
                    RefreshStats();
                }
                else
                {
                    long remaining = rate - magic.Experience;
                    Owner.Connection.ReceiveChatWithObservers(con => string.Format(con.Language.LearnBook4Failed, remaining, magic.Level + 1), MessageType.System);
                }
            }
            else
            {
                magic = SEnvir.UserMagicList.CreateNewObject();
                magic.Hero = Info;
                magic.Info = info;

                Owner.Connection.ReceiveChatWithObservers(con => string.Format(con.Language.LearnBookSuccess, magic.Info.Name), MessageType.System);
                RefreshStats();
            }

            return true;
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

        private sealed class HeroAreaMagicPayload
        {
            public UserMagic Magic { get; init; }
            public MagicType MagicType { get; init; }
            public Element Element { get; init; }
            public Point Location { get; init; }
            public int Radius { get; init; }
            public bool SpawnFireWall { get; init; }
            public int PowerRate { get; init; }
            public int Slow { get; init; }
        }

        protected sealed class HeroChainMagicPayload
        {
            public UserMagic Magic { get; init; }
            public MagicType MagicType { get; init; }
            public Element Element { get; init; }
            public MapObject Source { get; init; }
            public MapObject Target { get; init; }
            public Point TargetLocation { get; init; }
            public List<Point> Locations { get; init; }
            public List<Point> PreviousLocations { get; init; }
            public bool Primary { get; init; }
            public bool BroadcastProjectile { get; init; }
            public int RemainingBounces { get; init; }
            public int JumpRadius { get; init; }
            public int PowerDivisor { get; init; }
        }

        private sealed class SkillAreaPayload
        {
            public Map Map { get; init; }
            public Point Location { get; init; }
            public Point Origin { get; init; }
            public MirDirection Direction { get; init; }
        }

        private sealed class SkillChainPayload
        {
            public Map Map { get; init; }
            public Point Origin { get; init; }
            public List<MapObject> Targets { get; init; }
        }

        private sealed class SkillTargetPayload
        {
            public Map Map { get; init; }
            public MapObject Target { get; init; }
            public Point Origin { get; init; }
        }

        private sealed class SkillLocationPayload
        {
            public Map Map { get; init; }
            public Point Location { get; init; }
            public Point Origin { get; init; }
        }
    }

    public sealed class WarriorHero : HeroObject
    {
        private DateTime FlamingSwordReadyTime;
        private DateTime SlayingReadyTime;
        private MagicType CurrentMeleeMagic;

        public WarriorHero(HeroInfo info, PlayerObject owner) : base(info, owner) { }

        protected override int GetTargetScore(MapObject ob, int distance)
        {
            int score = base.GetTargetScore(ob, distance);

            if (Owner != null && Functions.InRange(Owner.CurrentLocation, ob.CurrentLocation, PreferredOwnerRange + 1))
                score += 1200;

            if (distance <= 1)
                score += 900;
            else
                score -= Math.Min(900, distance * 120);

            return score;
        }

        protected override MagicType GetMeleeAttackMagic()
        {
            CurrentMeleeMagic = MagicType.None;

            if (Level >= 25 && SEnvir.Now >= FlamingSwordReadyTime)
            {
                FlamingSwordReadyTime = SEnvir.Now.AddSeconds(TryGetUsableMagic(MagicType.FlamingSword, out UserMagic magic) ? Math.Max(5, 10 - magic.Level * 2) : 8);
                CurrentMeleeMagic = MagicType.FlamingSword;
                return CurrentMeleeMagic;
            }

            if (Level >= 15 && SEnvir.Now >= SlayingReadyTime)
            {
                SlayingReadyTime = SEnvir.Now.AddSeconds(TryGetUsableMagic(MagicType.Slaying, out UserMagic magic) ? Math.Max(3, 7 - magic.Level) : 6);
                CurrentMeleeMagic = MagicType.Slaying;
                return CurrentMeleeMagic;
            }

            return MagicType.None;
        }

        protected override Element GetMeleeAttackElement()
        {
            return CurrentMeleeMagic == MagicType.FlamingSword ? Element.Fire : base.GetMeleeAttackElement();
        }

        protected override int GetMeleeAttackPower()
        {
            int power = base.GetMeleeAttackPower();

            if (CurrentMeleeMagic == MagicType.FlamingSword)
                power += Math.Max(1, Stats[Stat.MaxDC] / 2);
            else if (CurrentMeleeMagic == MagicType.Slaying)
                power += Math.Max(1, Stats[Stat.MaxDC] / 5);

            return power;
        }
    }

    public sealed class WizardHero : HeroObject
    {
        private DateTime AreaMagicReadyTime;
        private DateTime ChainMagicReadyTime;
        private DateTime ShieldReadyTime;
        private MagicType CurrentAutoMagic;

        public WizardHero(HeroInfo info, PlayerObject owner) : base(info, owner) { }

        protected override int AttackRange => 6;
        protected override int PreferredOwnerRange => 3;
        protected override int RangedRetreatDistance => 2;
        protected override MagicType AutoMagic => Level >= 20 ? MagicType.ThunderBolt : MagicType.FireBall;
        protected override Element AutoMagicElement => Level >= 20 ? Element.Lightning : Element.Fire;
        protected override int AutoMagicCost => Math.Max(3, Level / 6);

        protected override MagicType GetAutoMagic()
        {
            CurrentAutoMagic = MagicType.None;
            if (CurrentMP < AutoMagicCost || !CanCast) return MagicType.None;

            if (Level >= 20 && TryGetUsableMagic(MagicType.ThunderBolt, out _))
            {
                CurrentAutoMagic = MagicType.ThunderBolt;
                return CurrentAutoMagic;
            }

            if (TryGetUsableMagic(MagicType.FireBall, out _))
            {
                CurrentAutoMagic = MagicType.FireBall;
                return CurrentAutoMagic;
            }

            CurrentAutoMagic = base.GetAutoMagic();
            return CurrentAutoMagic;
        }

        protected override Element GetAutoMagicElement()
        {
            return CurrentAutoMagic == MagicType.ThunderBolt ? Element.Lightning : Element.Fire;
        }

        protected override int GetAutoMagicPower()
        {
            return Math.Max(GetMC(), GetDC()) + Level + Stats[Stat.FireAttack] + Stats[Stat.LightningAttack];
        }

        protected override bool TryCastClassSkill()
        {
            return TryCastMagicShield() || TryCastWizardChainMagic() || TryCastWizardAreaMagic();
        }

        private bool TryCastMagicShield()
        {
            if (Level < 25 || SEnvir.Now < ShieldReadyTime || !CanCast) return false;
            if (Buffs.Any(x => x.Type == BuffType.MagicShield || x.Type == BuffType.SuperiorMagicShield)) return false;
            if (!TryGetUsableMagic(MagicType.MagicShield, out UserMagic magic)) return false;
            if (CurrentMP < magic.Cost || SEnvir.Now < magic.Cooldown) return false;

            Broadcast(new S.ObjectMagic
            {
                ObjectID = ObjectID,
                Direction = Direction,
                CurrentLocation = CurrentLocation,
                Cast = true,
                Type = MagicType.MagicShield,
                MagicInfoIndex = magic.Info.Index,
                AttackElement = Element.None,
                Locations = new List<Point> { CurrentLocation },
            });

            ChangeMP(-magic.Cost);
            magic.Cooldown = SEnvir.Now.AddMilliseconds(Math.Max(0, magic.Info.Delay));
            ShieldReadyTime = SEnvir.Now.AddSeconds(Math.Max(12, 28 - magic.Level * 4));
            UpdateMagicAttackTime();

            BuffAdd(BuffType.MagicShield, TimeSpan.FromSeconds(30 + magic.Level * 20 + GetMC() / 2 + Stats[Stat.PhantomAttack] * 2), new Stats
            {
                [Stat.MagicShield] = 50,
            }, true, false, TimeSpan.Zero);

            LevelMagic(magic);
            return true;
        }

        private bool TryCastWizardChainMagic()
        {
            if (Target?.Node == null || !CanAttackTarget(Target) || SEnvir.Now < ChainMagicReadyTime || !CanCast) return false;
            if ((Poison & PoisonType.Silenced) == PoisonType.Silenced) return false;
            if (!Functions.InRange(CurrentLocation, Target.CurrentLocation, AttackRange)) return false;

            return TryCastWizardChainMagic(MagicType.ChainLightning, Element.Lightning) ||
                   TryCastWizardChainMagic(MagicType.FireBounce, Element.Fire);
        }

        private bool TryCastWizardChainMagic(MagicType type, Element element)
        {
            if (!TryGetUsableMagic(type, out UserMagic magic)) return false;
            if (CurrentMP < magic.Cost || SEnvir.Now < magic.Cooldown) return false;
            if (!CanUseWizardChainMagic(type, 2)) return false;

            Direction = Functions.DirectionFromPoint(CurrentLocation, Target.CurrentLocation);

            Broadcast(new S.ObjectMagic
            {
                ObjectID = ObjectID,
                Direction = Direction,
                CurrentLocation = CurrentLocation,
                Cast = true,
                Type = type,
                MagicInfoIndex = magic.Info.Index,
                AttackElement = element,
                Targets = new List<uint> { Target.ObjectID },
                Locations = new List<Point> { Target.CurrentLocation },
            });

            ChangeMP(-magic.Cost);
            magic.Cooldown = SEnvir.Now.AddMilliseconds(Math.Max(0, magic.Info.Delay));
            ChainMagicReadyTime = SEnvir.Now + GetWizardChainCooldown(type, magic);
            UpdateMagicAttackTime();
            DamageItem((int)EquipmentSlot.Weapon);

            if (type == MagicType.FireBounce)
                QueueHeroFireBounce(magic, this, Target, magic.Level + 2, false);
            else
                QueueHeroChainLightning(magic, new List<Point> { Target.CurrentLocation }, new List<Point>(), true, 0);

            return true;
        }

        private bool TryCastWizardAreaMagic()
        {
            if (Target?.Node == null || !CanAttackTarget(Target) || SEnvir.Now < AreaMagicReadyTime || !CanCast) return false;
            if ((Poison & PoisonType.Silenced) == PoisonType.Silenced) return false;

            var candidates = new[]
            {
                (Type: MagicType.IceStorm, Element: Element.Ice, Radius: 1, MinimumTargets: 2, Delay: 500, PowerRate: 100, Slow: 4, FireWall: false),
                (Type: MagicType.FireStorm, Element: Element.Fire, Radius: 1, MinimumTargets: 2, Delay: 500, PowerRate: 115, Slow: 0, FireWall: false),
                (Type: MagicType.FireWall, Element: Element.Fire, Radius: 1, MinimumTargets: 1, Delay: 500, PowerRate: 100, Slow: 0, FireWall: true),
            };

            foreach (var candidate in candidates)
            {
                if (!TryGetUsableMagic(candidate.Type, out UserMagic magic)) continue;
                if (CurrentMP < magic.Cost || SEnvir.Now < magic.Cooldown) continue;
                if (!TryGetBestWizardAreaLocation(candidate.Radius, candidate.MinimumTargets, out Point location, out _)) continue;

                Direction = Functions.DirectionFromPoint(CurrentLocation, location);

                Broadcast(new S.ObjectMagic
                {
                    ObjectID = ObjectID,
                    Direction = Direction,
                    CurrentLocation = CurrentLocation,
                    Cast = true,
                    Type = candidate.Type,
                    MagicInfoIndex = magic.Info.Index,
                    AttackElement = candidate.Element,
                    Locations = new List<Point> { location },
                });

                ChangeMP(-magic.Cost);
                magic.Cooldown = SEnvir.Now.AddMilliseconds(Math.Max(0, magic.Info.Delay));
                AreaMagicReadyTime = SEnvir.Now + GetWizardAreaCooldown(candidate.Type, magic);
                UpdateMagicAttackTime();
                DamageItem((int)EquipmentSlot.Weapon);

                QueueHeroAreaMagic(magic, candidate.Type, candidate.Element, location, candidate.Radius, candidate.Delay, candidate.FireWall, candidate.PowerRate, candidate.Slow);
                return true;
            }

            return false;
        }

        private bool TryGetBestWizardAreaLocation(int radius, int minimumTargets, out Point location, out int targetCount)
        {
            location = Point.Empty;
            targetCount = 0;

            if (Target?.Node == null || CurrentMap == null) return false;

            IEnumerable<Point> candidates = CurrentMap.GetCells(Target.CurrentLocation, 0, Math.Max(1, radius + 2))
                .Where(x => x?.Objects != null)
                .SelectMany(x => x.Objects.ToArray())
                .Where(x => x?.Node != null && !x.Dead && x.CurrentMap == CurrentMap && CanAttackTarget(x))
                .Select(x => x.CurrentLocation)
                .Append(Target.CurrentLocation)
                .Distinct()
                .Where(x => Functions.InRange(CurrentLocation, x, AttackRange));

            foreach (Point candidate in candidates)
            {
                int count = CountWizardAreaTargets(candidate, radius);
                if (count < minimumTargets || count <= targetCount) continue;

                targetCount = count;
                location = candidate;
            }

            return targetCount >= minimumTargets;
        }

        private int CountWizardAreaTargets(Point location, int radius)
        {
            if (CurrentMap == null) return 0;

            return CurrentMap.GetCells(location, 0, radius)
                .Where(x => x?.Objects != null)
                .SelectMany(x => x.Objects.ToArray())
                .Count(x => x?.Node != null && !x.Dead && x.CurrentMap == CurrentMap && CanAttackTarget(x));
        }

        private bool CanUseWizardChainMagic(MagicType magicType, int minimumTargets)
        {
            if (Target?.Node == null || CurrentMap == null) return false;

            return magicType switch
            {
                MagicType.FireBounce => HasWizardBounceTargets(Target, minimumTargets),
                MagicType.ChainLightning => HasWizardChainLightningTargets(Target.CurrentLocation, minimumTargets),
                _ => false,
            };
        }

        private bool HasWizardBounceTargets(MapObject primaryTarget, int minimumTargets)
        {
            if (primaryTarget?.Node == null || CurrentMap == null) return false;
            int count = 1;

            foreach (Cell cell in CurrentMap.GetCells(primaryTarget.CurrentLocation, 0, 3))
            {
                if (cell?.Objects == null) continue;

                foreach (MapObject target in cell.Objects.ToArray())
                {
                    if (target?.Node == null || target.Dead || target.CurrentMap != CurrentMap) continue;
                    if (target.Race != ObjectType.Monster || target == primaryTarget || !CanAttackTarget(target)) continue;
                    if (++count >= minimumTargets) return true;
                }
            }

            return count >= minimumTargets;
        }

        private bool HasWizardChainLightningTargets(Point location, int minimumTargets)
        {
            if (CurrentMap == null) return false;

            int count = 0;

            foreach (Cell cell in CurrentMap.GetCells(location, 0, 1))
            {
                if (cell?.Objects == null) continue;

                foreach (MapObject target in cell.Objects.ToArray())
                {
                    if (target?.Node == null || target.Dead || target.CurrentMap != CurrentMap) continue;
                    if (target.Race != ObjectType.Monster || !CanAttackTarget(target)) continue;
                    if (!Functions.InRange(CurrentLocation, target.CurrentLocation, Globals.MagicRange)) continue;
                    if (++count >= minimumTargets) return true;
                }
            }

            return false;
        }

        private static TimeSpan GetWizardAreaCooldown(MagicType magicType, UserMagic magic)
        {
            int level = magic?.Level ?? 0;

            return magicType switch
            {
                MagicType.IceStorm => TimeSpan.FromSeconds(Math.Max(6, 12 - level * 2)),
                MagicType.FireStorm => TimeSpan.FromSeconds(Math.Max(5, 10 - level * 2)),
                MagicType.FireWall => TimeSpan.FromSeconds(Math.Max(8, 14 - level * 2)),
                _ => TimeSpan.FromSeconds(8),
            };
        }

        private static TimeSpan GetWizardChainCooldown(MagicType magicType, UserMagic magic)
        {
            int level = magic?.Level ?? 0;

            return magicType switch
            {
                MagicType.ChainLightning => TimeSpan.FromSeconds(Math.Max(6, 12 - level * 2)),
                MagicType.FireBounce => TimeSpan.FromSeconds(Math.Max(5, 10 - level * 2)),
                _ => TimeSpan.FromSeconds(8),
            };
        }
    }

    public sealed class TaoistHero : HeroObject
    {
        private DateTime SupportBuffReadyTime;
        private int SupportBuffCursor;

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

            if (TryCastCustomHeroSupportSkill())
                return;

            if (TryCastTaoistSupportBuff())
                return;

            MapObject healTarget = GetHealTarget();
            if (healTarget == null)
            {
                TryCastCustomHeroSummonSkill();
                return;
            }

            int cost = Math.Max(3, Level / 7);
            if (CurrentMP < cost) return;

            if (!Functions.InRange(CurrentLocation, healTarget.CurrentLocation, AttackRange))
            {
                Target = null;
                MoveTo(healTarget.CurrentLocation);
                return;
            }

            Direction = Functions.DirectionFromPoint(CurrentLocation, healTarget.CurrentLocation);

            bool hasHealMagic = TryGetUsableMagic(MagicType.Heal, out UserMagic healMagic);
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

            int bonus = 0;
            int cap = Math.Max(20, 30 + Level / 2);

            if (TryGetUsableMagic(MagicType.EmpoweredHealing, out UserMagic empoweredHealing))
            {
                bonus = empoweredHealing.GetPower();
                cap += (1 + empoweredHealing.Level) * 30;
                LevelMagic(empoweredHealing);
            }

            int basePower = hasHealMagic ? healMagic.GetPower() : Level;
            int healing = Math.Max(10, GetSC() + basePower + Stats[Stat.HolyAttack] * 2 + bonus);

            healTarget.BuffAdd(BuffType.Heal, TimeSpan.FromSeconds(Math.Max(1, healing / cap)), new Stats
            {
                [Stat.Healing] = healing,
                [Stat.HealingCap] = cap,
            }, false, false, TimeSpan.FromSeconds(1));

            if (hasHealMagic)
                LevelMagic(healMagic);
        }

        private bool TryCastTaoistSupportBuff()
        {
            if (SEnvir.Now < SupportBuffReadyTime || !CanCast || CurrentMP <= 0 || (Poison & PoisonType.Silenced) == PoisonType.Silenced) return false;

            MagicType[] order =
            {
                MagicType.Resilience,
                MagicType.MagicResistance,
                MagicType.ElementalSuperiority,
                MagicType.BloodLust,
            };

            for (int i = 0; i < order.Length; i++)
            {
                MagicType type = order[(SupportBuffCursor + i) % order.Length];
                if (!TryGetUsableMagic(type, out UserMagic magic)) continue;
                if (CurrentMP < magic.Cost || SEnvir.Now < magic.Cooldown) continue;
                if (!TryGetTaoistSupportBuffTarget(type, out MapObject target, out BuffType buffType, out Stats buffStats)) continue;

                if (!Functions.InRange(CurrentLocation, target.CurrentLocation, AttackRange))
                {
                    Target = null;
                    MoveTo(target.CurrentLocation);
                    return true;
                }

                Direction = target.CurrentLocation == CurrentLocation
                    ? Direction
                    : Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation);

                Broadcast(new S.ObjectMagic
                {
                    ObjectID = ObjectID,
                    Direction = Direction,
                    CurrentLocation = CurrentLocation,
                    Cast = true,
                    Type = type,
                    MagicInfoIndex = magic.Info.Index,
                    AttackElement = Element.None,
                    Targets = new List<uint> { target.ObjectID },
                    Locations = new List<Point> { target.CurrentLocation },
                });

                ChangeMP(-magic.Cost);
                magic.Cooldown = SEnvir.Now.AddMilliseconds(Math.Max(0, magic.Info.Delay));
                SupportBuffReadyTime = SEnvir.Now.AddSeconds(Math.Max(4, 8 - magic.Level));
                SupportBuffCursor = (SupportBuffCursor + i + 1) % order.Length;
                UpdateMagicAttackTime();

                target.BuffAdd(buffType, TimeSpan.FromSeconds(Math.Max(20, magic.GetPower() + GetSC() * 2)), buffStats, true, false, TimeSpan.Zero);
                LevelMagic(magic);
                return true;
            }

            return false;
        }

        private bool TryGetTaoistSupportBuffTarget(MagicType type, out MapObject target, out BuffType buffType, out Stats buffStats)
        {
            buffType = GetTaoistSupportBuffType(type);
            buffStats = null;
            target = null;

            if (buffType == BuffType.None) return false;

            BuffType targetBuffType = buffType;
            target = new MapObject[] { Owner, this }
                .Where(x => x?.Node != null && x.CurrentMap == CurrentMap && !x.Dead && CanHelpTarget(x))
                .Where(x => TaoistSupportBuffMatchesTarget(type, x))
                .Where(x => x.Buffs.All(buff => buff.Type != targetBuffType))
                .OrderBy(x => ReferenceEquals(x, Owner) ? 0 : 1)
                .ThenBy(x => Functions.Distance(CurrentLocation, x.CurrentLocation))
                .FirstOrDefault();

            if (target == null) return false;

            buffStats = GetTaoistSupportBuffStats(type);
            return buffStats != null;
        }

        private static BuffType GetTaoistSupportBuffType(MagicType type)
        {
            return type switch
            {
                MagicType.MagicResistance => BuffType.MagicResistance,
                MagicType.Resilience => BuffType.Resilience,
                MagicType.ElementalSuperiority => BuffType.ElementalSuperiority,
                MagicType.BloodLust => BuffType.BloodLust,
                _ => BuffType.None,
            };
        }

        private static bool TaoistSupportBuffMatchesTarget(MagicType type, MapObject target)
        {
            bool physical = target switch
            {
                PlayerObject player => player.Class is MirClass.Warrior or MirClass.Assassin,
                HeroObject hero => hero.Class is MirClass.Warrior or MirClass.Assassin,
                _ => false,
            };

            return type switch
            {
                MagicType.BloodLust => physical,
                MagicType.ElementalSuperiority => !physical,
                _ => true,
            };
        }

        private Stats GetTaoistSupportBuffStats(MagicType type)
        {
            int value = 5;

            if (TryGetUsableMagic(type, out UserMagic magic))
                value += magic.Level;

            return type switch
            {
                MagicType.MagicResistance => new Stats { [Stat.MaxMR] = value },
                MagicType.Resilience => new Stats { [Stat.MaxAC] = value, [Stat.PhysicalResistance] = 1 },
                MagicType.ElementalSuperiority => new Stats { [Stat.MaxMC] = value, [Stat.MaxSC] = value },
                MagicType.BloodLust => new Stats { [Stat.MaxDC] = value },
                _ => null,
            };
        }

        private MapObject GetHealTarget()
        {
            int selfMaxHealth = Math.Max(1, Stats[Stat.Health]);
            int ownerMaxHealth = Math.Max(1, Owner.Stats[Stat.Health]);
            bool selfHasHeal = Buffs.Any(x => x.Type == BuffType.Heal);
            bool ownerHasHeal = Owner.Buffs.Any(x => x.Type == BuffType.Heal);

            bool selfNeedsHeal = CurrentHP > 0 &&
                                 (long)CurrentHP * 100 <= (long)selfMaxHealth * 50 &&
                                 !selfHasHeal;

            bool selfCritical = CurrentHP > 0 &&
                                (long)CurrentHP * 100 <= (long)selfMaxHealth * 30 &&
                                !selfHasHeal;

            bool ownerNeedsHeal = Owner.CurrentMap == CurrentMap &&
                Owner.CurrentHP > 0 &&
                (long)Owner.CurrentHP * 100 <= (long)ownerMaxHealth * 60 &&
                !ownerHasHeal &&
                CanHelpTarget(Owner);

            bool ownerCritical = Owner.CurrentMap == CurrentMap &&
                Owner.CurrentHP > 0 &&
                (long)Owner.CurrentHP * 100 <= (long)ownerMaxHealth * 35 &&
                !ownerHasHeal &&
                CanHelpTarget(Owner);

            if (ownerCritical)
                return Owner;

            if (selfCritical)
                return this;

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
        private DateTime HemorrhageReadyTime;
        private DateTime DisciplineReadyTime;
        private MagicType CurrentMeleeMagic;

        public AssassinHero(HeroInfo info, PlayerObject owner) : base(info, owner) { }

        protected override int GetTargetScore(MapObject ob, int distance)
        {
            int score = base.GetTargetScore(ob, distance);

            if (distance <= 1)
                score += 1000;
            else
                score -= Math.Min(1000, distance * 150);

            int maxHealth = ob?.Stats?[Stat.Health] ?? 0;
            if (maxHealth > 0 && ob.CurrentHP > 0 && (long)ob.CurrentHP * 100 <= (long)maxHealth * 45)
                score += 1400;

            return score;
        }

        protected override MagicType GetMeleeAttackMagic()
        {
            CurrentMeleeMagic = MagicType.None;
            if (Target?.Node == null) return MagicType.None;

            if (Level >= 20 && SEnvir.Now >= HemorrhageReadyTime)
            {
                int maxHealth = Math.Max(1, Target.Stats[Stat.Health]);
                bool targetLowHealth = (long)Target.CurrentHP * 100 <= (long)maxHealth * 45;
                bool targetBleeding = (Target.Poison & PoisonType.Hemorrhage) == PoisonType.Hemorrhage;

                if (!targetBleeding || targetLowHealth)
                {
                    HemorrhageReadyTime = SEnvir.Now.AddSeconds(TryGetUsableMagic(MagicType.Hemorrhage, out UserMagic magic) ? Math.Max(4, 9 - magic.Level * 2) : 7);
                    CurrentMeleeMagic = MagicType.Hemorrhage;
                    return CurrentMeleeMagic;
                }
            }

            if (Level >= 15 && SEnvir.Now >= DisciplineReadyTime)
            {
                DisciplineReadyTime = SEnvir.Now.AddSeconds(TryGetUsableMagic(MagicType.Discipline, out UserMagic magic) ? Math.Max(3, 8 - magic.Level) : 7);
                CurrentMeleeMagic = MagicType.Discipline;
                return CurrentMeleeMagic;
            }

            return MagicType.None;
        }

        protected override int GetMeleeAttackPower()
        {
            int power = base.GetMeleeAttackPower();

            if (CurrentMeleeMagic == MagicType.Hemorrhage)
                power += Math.Max(1, Stats[Stat.MaxDC] / 4);
            else if (CurrentMeleeMagic == MagicType.Discipline)
                power += Math.Max(1, Stats[Stat.MaxDC] / 6 + Stats[Stat.Accuracy] / 3);

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
