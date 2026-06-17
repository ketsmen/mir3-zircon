using Library;
using Library.SystemModels;
using Server.DBModels;
using Server.Envir;
using Server.Models.Monsters;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Server.Models.Magics
{
    public sealed class CustomMagicObject : MagicObject
    {
        private readonly CustomSkillInfo _skill;

        protected override Element Element => _skill?.Element ?? Element.None;
        public override bool UpdateCombatTime => _skill?.Template is not (CustomSkillTemplate.Heal or CustomSkillTemplate.Buff or CustomSkillTemplate.Summon);

        public CustomMagicObject(PlayerObject player, UserMagic magic) : base(player, magic)
        {
            SEnvir.CustomSystem.TryGetSkill(magic.Info, out _skill);
        }

        public override MagicCast MagicCast(MapObject target, Point location, MirDirection direction)
        {
            MagicCast response = new();

            if (_skill == null || !_skill.Enabled)
            {
                response.Cast = false;
                return response;
            }

            int range = _skill.Range > 0 ? _skill.Range : Globals.MagicRange;

            switch (_skill.TargetRule)
            {
                case CustomSkillTargetRule.Self:
                    target = Player;
                    break;
                case CustomSkillTargetRule.Ally:
                    if (!Player.CanHelpTarget(target))
                    {
                        response.Cast = false;
                        return response;
                    }
                    break;
                case CustomSkillTargetRule.AllyOrSelf:
                    if (!Player.CanHelpTarget(target))
                        target = Player;
                    break;
                case CustomSkillTargetRule.Enemy:
                    if (!Player.CanAttackTarget(target))
                    {
                        response.Cast = false;
                        return response;
                    }
                    break;
                case CustomSkillTargetRule.Ground:
                    if (!Functions.InRange(CurrentLocation, location, range))
                    {
                        response.Cast = false;
                        return response;
                    }
                    target = null;
                    break;
                case CustomSkillTargetRule.EnemyOrGround:
                    if (Player.CanAttackTarget(target))
                    {
                        location = target.CurrentLocation;
                    }
                    else if (!Functions.InRange(CurrentLocation, location, range))
                    {
                        response.Cast = false;
                        return response;
                    }
                    else
                    {
                        target = null;
                    }
                    break;
                default:
                    response.Cast = false;
                    return response;
            }

            if (target != null && !Functions.InRange(CurrentLocation, target.CurrentLocation, range))
            {
                response.Cast = false;
                return response;
            }

            DateTime delay = target != null ? GetSkillDelay(target) : SEnvir.Now.AddMilliseconds(GetCastDelay());

            switch (_skill.Template)
            {
                case CustomSkillTemplate.ChainDamage:
                    if (target == null)
                    {
                        response.Cast = false;
                        return response;
                    }

                    SkillChainPayload chainPayload = BuildChainPayload(target);
                    response.Ob = target;
                    response.Targets.AddRange(chainPayload.Targets.Select(x => x.ObjectID));
                    ActionList.Add(new DelayedAction(GetSkillDelay(target), ActionType.DelayMagic, Type, Magic.Info.Index, chainPayload));
                    break;
                case CustomSkillTemplate.DirectDamage:
                case CustomSkillTemplate.ProjectileDamage:
                case CustomSkillTemplate.Heal:
                case CustomSkillTemplate.Buff:
                case CustomSkillTemplate.Debuff:
                case CustomSkillTemplate.Control:
                    if (TryCreateAreaPayload(target, location, range, out SkillAreaPayload areaPayload))
                    {
                        response.Ob = areaPayload.PrimaryTarget;
                        response.Locations.Add(areaPayload.Location);
                        response.Targets.AddRange(areaPayload.Targets.Select(x => x.ObjectID));
                        ActionList.Add(new DelayedAction(GetSkillDelay(areaPayload.PrimaryTarget), ActionType.DelayMagic, Type, Magic.Info.Index, areaPayload));
                        break;
                    }

                    if (target == null)
                    {
                        response.Cast = false;
                        return response;
                    }

                    response.Ob = target;
                    if (target != null)
                        response.Targets.Add(target.ObjectID);
                    ActionList.Add(new DelayedAction(delay, ActionType.DelayMagic, Type, Magic.Info.Index, target));
                    break;
                case CustomSkillTemplate.AreaDamage:
                case CustomSkillTemplate.PersistentAreaDamage:
                    response.Locations.Add(location);
                    ActionList.Add(new DelayedAction(SEnvir.Now.AddMilliseconds(GetCastDelay()), ActionType.DelayMagic, Type, Magic.Info.Index, CreateAreaPayload(target, location, direction)));
                    break;
                case CustomSkillTemplate.Summon:
                    response.Locations.Add(Functions.Move(CurrentLocation, direction, -1));
                    ActionList.Add(new DelayedAction(SEnvir.Now.AddMilliseconds(GetCastDelay()), ActionType.DelayMagic, Type, Magic.Info.Index, response.Locations[0]));
                    break;
                case CustomSkillTemplate.DamageProxy:
                    if (!CanCastDamageProxy(target))
                    {
                        response.Cast = false;
                        return response;
                    }

                    Point proxyLocation = Functions.Move(CurrentLocation, direction, 1);
                    response.Ob = target;
                    response.Targets.Add(target.ObjectID);
                    response.Locations.Add(proxyLocation);
                    ActionList.Add(new DelayedAction(
                        SEnvir.Now.AddMilliseconds(GetCastDelay()),
                        ActionType.DelayMagic,
                        Type,
                        Magic.Info.Index,
                        new SkillDamageProxyPayload { Target = target, Location = proxyLocation }));
                    break;
                case CustomSkillTemplate.Move:
                    response.Locations.Add(location);
                    ActionList.Add(new DelayedAction(SEnvir.Now.AddMilliseconds(GetCastDelay(300)), ActionType.DelayMagic, Type, Magic.Info.Index, location));
                    break;
                default:
                    response.Cast = false;
                    break;
            }

            return response;
        }

        public override void MagicComplete(params object[] data)
        {
            if (_skill == null || !_skill.Enabled || data.Length < 3) return;

            object payload = data[2];
            int remainingWaves = GetRemainingWaveCount(data);
            bool applied = false;

            switch (_skill.Template)
            {
                case CustomSkillTemplate.ChainDamage:
                    if (payload is SkillChainPayload chainPayload)
                        ApplyChainTargets(chainPayload);
                    else
                        ApplyDamage(payload as MapObject);
                    applied = true;
                    break;
                case CustomSkillTemplate.DirectDamage:
                case CustomSkillTemplate.ProjectileDamage:
                    if (payload is SkillAreaPayload damageArea)
                        ApplyAreaTargets(damageArea);
                    else
                        ApplyDamage(payload as MapObject);
                    applied = true;
                    break;
                case CustomSkillTemplate.AreaDamage:
                    if (payload is SkillAreaPayload area)
                        ApplyAreaDamage(area);
                    else if (payload is Point point)
                        ApplyAreaDamage(CreateAreaPayload(null, point, Direction));
                    applied = true;
                    break;
                case CustomSkillTemplate.PersistentAreaDamage:
                    if (payload is SkillAreaPayload persistentArea)
                    {
                        int remainingTicks = data.Length > 3 && data[3] is int remaining ? remaining : GetPersistentAreaTickCount();
                        ApplyPersistentAreaDamage(persistentArea, remainingTicks);
                    }
                    else if (payload is Point persistentPoint)
                    {
                        int remainingTicks = data.Length > 3 && data[3] is int remaining ? remaining : GetPersistentAreaTickCount();
                        ApplyPersistentAreaDamage(CreateAreaPayload(null, persistentPoint, Direction), remainingTicks);
                    }
                    break;
                case CustomSkillTemplate.Heal:
                    if (payload is SkillAreaPayload healArea)
                        ApplyAreaTargets(healArea);
                    else
                        ApplyHeal(payload as MapObject);
                    applied = true;
                    break;
                case CustomSkillTemplate.Buff:
                    if (payload is SkillAreaPayload buffArea)
                        ApplyAreaTargets(buffArea);
                    else
                        ApplyBuff(payload as MapObject, helpful: true);
                    applied = true;
                    break;
                case CustomSkillTemplate.Debuff:
                case CustomSkillTemplate.Control:
                    if (payload is SkillAreaPayload statusArea)
                        ApplyAreaTargets(statusArea);
                    else
                        ApplyControlTemplate(payload as MapObject);
                    applied = true;
                    break;
                case CustomSkillTemplate.Summon:
                    if (payload is Point location)
                        ApplySummon(location);
                    break;
                case CustomSkillTemplate.DamageProxy:
                    if (payload is SkillDamageProxyPayload proxyPayload)
                        ApplyDamageProxy(proxyPayload);
                    break;
                case CustomSkillTemplate.Move:
                    if (payload is Point moveLocation)
                    {
                        ApplyMove(moveLocation);
                        applied = true;
                    }
                    break;
            }

            if (applied)
                QueueNextWave(payload, remainingWaves);
        }

        private SkillChainPayload BuildChainPayload(MapObject primaryTarget)
        {
            List<MapObject> targets = new();
            if (primaryTarget?.Node == null || !Player.CanAttackTarget(primaryTarget))
                return new SkillChainPayload { Targets = targets };

            targets.Add(primaryTarget);

            int maxTargets = GetMaxChainTargets();
            int jumpRadius = GetChainRadius();
            MapObject previous = primaryTarget;

            while (targets.Count < maxTargets)
            {
                MapObject next = GetNextChainTarget(previous, targets, jumpRadius);
                if (next == null) break;

                targets.Add(next);
                previous = next;
            }

            return new SkillChainPayload { Targets = targets };
        }

        private MapObject GetNextChainTarget(MapObject previous, IReadOnlyCollection<MapObject> usedTargets, int jumpRadius)
        {
            if (previous?.CurrentMap != CurrentMap) return null;

            return CurrentMap.GetCells(previous.CurrentLocation, 0, jumpRadius)
                .Where(x => x?.Objects != null)
                .SelectMany(x => x.Objects.ToArray())
                .Where(x => x?.Node != null && !x.Dead && x.CurrentMap == CurrentMap && x.Visible)
                .Where(x => !usedTargets.Contains(x) && Player.CanAttackTarget(x))
                .OrderBy(x => Functions.Distance(previous.CurrentLocation, x.CurrentLocation))
                .ThenBy(x => Functions.Distance(CurrentLocation, x.CurrentLocation))
                .FirstOrDefault();
        }

        private void ApplyChainTargets(SkillChainPayload payload)
        {
            if (payload?.Targets == null) return;

            foreach (MapObject target in payload.Targets.ToArray())
                ApplyDamage(target);
        }

        private void ApplyAreaTargets(SkillAreaPayload payload)
        {
            if (payload?.Targets == null) return;

            foreach (MapObject target in payload.Targets.ToArray())
                ApplyTemplateToTarget(target);
        }

        private void ApplyTemplateToTarget(MapObject target)
        {
            switch (_skill.Template)
            {
                case CustomSkillTemplate.DirectDamage:
                case CustomSkillTemplate.ProjectileDamage:
                    ApplyDamage(target);
                    break;
                case CustomSkillTemplate.Heal:
                    ApplyHeal(target);
                    break;
                case CustomSkillTemplate.Buff:
                    ApplyBuff(target, helpful: true);
                    break;
                case CustomSkillTemplate.Debuff:
                case CustomSkillTemplate.Control:
                    ApplyControlTemplate(target);
                    break;
            }
        }

        private bool TryCreateAreaPayload(MapObject target, Point location, int range, out SkillAreaPayload payload)
        {
            payload = null;

            if (_skill.Radius <= 0 || CurrentMap == null)
                return false;

            Point center = _skill.TargetRule == CustomSkillTargetRule.Self
                ? CurrentLocation
                : target?.CurrentLocation ?? location;

            if (!Functions.InRange(CurrentLocation, center, range))
                return false;

            MirDirection direction = GetSkillDirection(target, center);
            List<MapObject> targets = GetAreaTargets(center, direction);
            if (targets.Count == 0)
                return false;

            payload = new SkillAreaPayload
            {
                Location = center,
                Direction = direction,
                PrimaryTarget = target ?? targets.FirstOrDefault(),
                Targets = targets,
            };

            return true;
        }

        private SkillAreaPayload CreateAreaPayload(MapObject target, Point location, MirDirection direction)
        {
            return new SkillAreaPayload
            {
                Location = location,
                Direction = target?.Node != null ? Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation) : direction,
                PrimaryTarget = target,
                Targets = null,
            };
        }

        private List<MapObject> GetAreaTargets(Point center, MirDirection direction)
        {
            List<MapObject> targets = new();
            int radius = GetSkillSearchRadius();
            bool helpful = IsHelpfulSkill(_skill);

            foreach (Cell cell in CurrentMap.GetCells(center, 0, radius))
            {
                if (cell?.Objects == null) continue;

                foreach (MapObject ob in cell.Objects.ToArray())
                {
                    if (ob?.Node == null || ob.Dead || ob.CurrentMap != CurrentMap) continue;
                    if (!IsLocationInSkillShape(center, ob.CurrentLocation, direction)) continue;

                    if (helpful)
                    {
                        if (!Player.CanHelpTarget(ob)) continue;
                    }
                    else if (!Player.CanAttackTarget(ob)) continue;

                    targets.Add(ob);
                }
            }

            return targets
                .Distinct()
                .OrderBy(x => ReferenceEquals(x, Player) ? 0 : 1)
                .ThenBy(x => Functions.Distance(center, x.CurrentLocation))
                .Take(GetMaxTargetsOrAll())
                .ToList();
        }

        private void ApplyDamage(MapObject target)
        {
            if (target?.Node == null || target.Dead || !Player.CanAttackTarget(target)) return;

            int power = GetTemplatePower();
            Element element = _skill.Element;

            if (!_skill.IgnoreArmour)
                power -= element == Element.None ? target.GetAC() : target.GetMR();
            power = ApplyResistance(power, target, element);

            if (power <= 0)
            {
                target.Blocked();
                return;
            }

            int damage = target.Attacked(Player, power, element, false, _skill.IgnoreShield);
            if (damage > 0)
                Player.LevelMagic(Magic);
        }

        private void ApplyAreaDamage(SkillAreaPayload payload)
        {
            if (payload == null) return;
            Point point = payload.Location;
            if (!Functions.InRange(CurrentLocation, point, _skill.Range > 0 ? _skill.Range : Globals.MagicRange)) return;

            int remainingTargets = GetMaxTargetsOrAll();

            foreach (Cell cell in CurrentMap.GetCells(point, 0, GetSkillSearchRadius()))
            {
                if (cell?.Objects == null) continue;

                for (int i = cell.Objects.Count - 1; i >= 0; i--)
                {
                    if (i >= cell.Objects.Count) continue;
                    MapObject target = cell.Objects[i];
                    if (!IsLocationInSkillShape(point, target.CurrentLocation, payload.Direction)) continue;
                    if (!Player.CanAttackTarget(target)) continue;

                    ApplyDamage(target);
                    if (remainingTargets != int.MaxValue && --remainingTargets <= 0)
                        return;
                }
            }
        }

        private void ApplyPersistentAreaDamage(SkillAreaPayload payload, int remainingTicks)
        {
            if (remainingTicks <= 0) return;

            ApplyAreaDamage(payload);

            if (remainingTicks <= 1) return;

            ActionList.Add(new DelayedAction(
                SEnvir.Now.AddMilliseconds(GetPersistentAreaTickRate()),
                ActionType.DelayMagic,
                Type,
                Magic.Info.Index,
                payload,
                remainingTicks - 1));
        }

        private void ApplyHeal(MapObject target)
        {
            if (target?.Node == null || !Player.CanHelpTarget(target) || target.CurrentHP >= target.Stats[Stat.Health]) return;

            target.ChangeHP(Math.Min(GetTemplatePower(), target.Stats[Stat.Health] - target.CurrentHP));
            Player.LevelMagic(Magic);
        }

        private void ApplyBuff(MapObject target, bool helpful)
        {
            if (target?.Node == null) return;
            if (helpful && !Player.CanHelpTarget(target)) return;
            if (!helpful && !Player.CanAttackTarget(target)) return;

            TimeSpan duration = TimeSpan.FromMilliseconds(Math.Max(1000, _skill.Duration));
            TimeSpan tickRate = TimeSpan.FromMilliseconds(Math.Max(0, _skill.TickRate));

            if (_skill.BuffType != BuffType.None)
            {
                target.BuffAdd(_skill.BuffType, duration, CloneStats(_skill.BuffStats), _skill.BuffVisible, _skill.BuffPause, tickRate, _skill.BuffHidden, _skill.BuffExtra);
                Player.LevelMagic(Magic);
                return;
            }

            if (!helpful)
            {
                TimeSpan poisonTickRate = tickRate <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : tickRate;

                target.ApplyPoison(new Poison
                {
                    Owner = Player,
                    Type = _skill.PoisonType == PoisonType.None ? PoisonType.Paralysis : _skill.PoisonType,
                    Value = Math.Max(0, _skill.PoisonValue),
                    TickFrequency = poisonTickRate,
                    TickCount = Math.Max(1, (int)Math.Ceiling(duration.TotalMilliseconds / poisonTickRate.TotalMilliseconds)),
                    CanKill = _skill.PoisonCanKill,
                });

                Player.LevelMagic(Magic);
            }
        }

        private void ApplyControlTemplate(MapObject target)
        {
            ApplyBuff(target, helpful: false);
            ApplyControlMovement(target);
        }

        private void ApplyControlMovement(MapObject target)
        {
            if (target?.Node == null || target == Player || !Player.CanAttackTarget(target)) return;

            if (_skill.KnockbackDistance > 0)
            {
                MirDirection direction = Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation);
                target.Pushed(direction, _skill.KnockbackDistance);
            }

            if (_skill.PullDistance > 0)
            {
                MirDirection direction = Functions.DirectionFromPoint(target.CurrentLocation, CurrentLocation);
                target.Pushed(direction, _skill.PullDistance);
            }
        }

        private void ApplySummon(Point location)
        {
            if (_skill.SummonMonster == null) return;

            int maxSummons = Math.Max(1, _skill.MaxSummons);
            int existing = Player.Pets.Count(x => x.MonsterInfo == _skill.SummonMonster);
            if (existing >= maxSummons) return;

            MonsterObject monster = MonsterObject.GetMonster(_skill.SummonMonster);
            if (monster == null) return;

            monster.PetOwner = Player;
            monster.Magics.Add(Magic);
            monster.SummonLevel = Magic.Level;
            monster.TameTime = _skill.SummonDuration > 0 ? SEnvir.Now.AddMilliseconds(_skill.SummonDuration) : SEnvir.Now.AddDays(365);
            Player.Pets.Add(monster);

            Cell cell = CurrentMap.GetCell(location);
            if (cell == null || cell.Movements != null || !monster.Spawn(CurrentMap, location))
                monster.Spawn(CurrentMap, CurrentLocation);

            monster.SetHP(monster.Stats[Stat.Health]);
            Player.LevelMagic(Magic);
        }

        private bool CanCastDamageProxy(MapObject target)
        {
            if (_skill.SummonMonster == null || target?.Node == null) return false;
            if (!Player.CanAttackTarget(target)) return false;
            if (Doll.CursedList.Contains(target)) return false;
            if (target is MonsterObject monster && monster.MonsterInfo.IsBoss) return false;
            if (target.Level > Player.Level + 2) return false;

            return true;
        }

        private void ApplyDamageProxy(SkillDamageProxyPayload payload)
        {
            if (!CanCastDamageProxy(payload?.Target)) return;

            MonsterObject monster = MonsterObject.GetMonster(_skill.SummonMonster);
            if (monster == null) return;

            TimeSpan duration = GetDamageProxyDuration();

            if (monster is Doll doll)
            {
                doll.DollTarget = payload.Target;
                doll.Caster = Player;
                doll.AliveTime = SEnvir.Now.Add(duration);
            }
            else if (monster is CustomMonsterObject customProxy)
            {
                if (!customProxy.ConfigureDamageProxy(Player, payload.Target, duration))
                    return;
            }
            else
            {
                return;
            }

            Cell cell = CurrentMap.GetCell(payload.Location);
            if (cell == null || cell.Movements != null || !monster.Spawn(CurrentMap, payload.Location))
                monster.Spawn(CurrentMap, CurrentLocation);

            Player.LevelMagic(Magic);
        }

        private TimeSpan GetDamageProxyDuration()
        {
            int duration = _skill.SummonDuration > 0
                ? _skill.SummonDuration
                : _skill.Duration > 0
                    ? _skill.Duration
                    : 10000 + Magic.Level * 5000;

            return TimeSpan.FromMilliseconds(Math.Max(1, duration));
        }

        private void ApplyMove(Point location)
        {
            if (!Functions.InRange(CurrentLocation, location, _skill.Range > 0 ? _skill.Range : Globals.MagicRange)) return;
            Cell cell = CurrentMap.GetCell(location);
            if (cell == null || cell.Movements != null) return;

            Player.Teleport(CurrentMap, location, true);
            Player.LevelMagic(Magic);
        }

        private int GetTemplatePower()
        {
            int source = _skill.PowerSource switch
            {
                CustomSkillPowerSource.DC => Player.GetDC(),
                CustomSkillPowerSource.MC => Player.GetMC(),
                CustomSkillPowerSource.SC => Player.GetSC(),
                CustomSkillPowerSource.Fixed => 0,
                _ => GetAutoPowerSource(),
            };

            int power = _skill.FixedPower + Magic.GetPower() + source * Math.Max(0, _skill.PowerRate) / 100;

            return Math.Max(1, power);
        }

        private int GetPersistentAreaTickRate()
        {
            return Math.Max(100, _skill.TickRate);
        }

        private int GetPersistentAreaTickCount()
        {
            int tickRate = GetPersistentAreaTickRate();
            int duration = _skill.Duration <= 0 ? tickRate : _skill.Duration;
            return Math.Max(1, (int)Math.Ceiling(duration / (double)tickRate));
        }

        private int GetAutoPowerSource()
        {
            return _skill.Element switch
            {
                Element.Holy or Element.Dark or Element.Phantom => Player.GetSC(),
                Element.None when Magic.Info.School is MagicSchool.Physical or MagicSchool.Atrocity or MagicSchool.Kill or MagicSchool.Assassination => Player.GetDC(),
                _ => Player.GetMC(),
            };
        }

        private int GetMaxTargetsOrAll()
        {
            return _skill?.MaxTargets > 0 ? _skill.MaxTargets : int.MaxValue;
        }

        private int GetSkillSearchRadius()
        {
            return _skill.Shape switch
            {
                CustomSkillShape.Line or CustomSkillShape.Cone or CustomSkillShape.Rectangle => Math.Max(Math.Max(_skill.Range, _skill.Length), _skill.Radius),
                _ => Math.Max(0, _skill.Radius),
            };
        }

        private bool IsLocationInSkillShape(Point center, Point location, MirDirection direction)
        {
            if (_skill.Shape == CustomSkillShape.Circle)
                return Functions.Distance(center, location) <= Math.Max(0, _skill.Radius);

            Point origin = CurrentLocation;
            (double fx, double fy) = GetDirectionVector(direction);
            double vx = location.X - origin.X;
            double vy = location.Y - origin.Y;
            double forward = vx * fx + vy * fy;
            double side = Math.Abs(vx * -fy + vy * fx);
            double length = Math.Max(1, _skill.Length > 0 ? _skill.Length : Math.Max(_skill.Range, _skill.Radius));

            return _skill.Shape switch
            {
                CustomSkillShape.Line => forward >= 0 && forward <= length && side <= Math.Max(0.5D, (_skill.Width <= 0 ? 1 : _skill.Width) / 2D),
                CustomSkillShape.Rectangle => forward >= 0 && forward <= length && side <= Math.Max(0.5D, (_skill.Width <= 0 ? Math.Max(1, _skill.Radius) : _skill.Width) / 2D),
                CustomSkillShape.Cone => IsInsideCone(vx, vy, fx, fy, length),
                _ => true,
            };
        }

        private bool IsInsideCone(double vx, double vy, double fx, double fy, double length)
        {
            double distance = Math.Sqrt(vx * vx + vy * vy);
            if (distance <= 0 || distance > length) return false;

            double dot = (vx * fx + vy * fy) / distance;
            double minDot = Math.Cos((Math.Max(1, _skill.Angle <= 0 ? 90 : _skill.Angle) / 2D) * Math.PI / 180D);
            return dot >= minDot;
        }

        private MirDirection GetSkillDirection(MapObject target, Point center)
        {
            if (target?.Node != null)
                return Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation);

            return center == CurrentLocation ? Direction : Functions.DirectionFromPoint(CurrentLocation, center);
        }

        private static (double X, double Y) GetDirectionVector(MirDirection direction)
        {
            (double x, double y) = direction switch
            {
                MirDirection.Up => (0, -1),
                MirDirection.UpRight => (1, -1),
                MirDirection.Right => (1, 0),
                MirDirection.DownRight => (1, 1),
                MirDirection.Down => (0, 1),
                MirDirection.DownLeft => (-1, 1),
                MirDirection.Left => (-1, 0),
                MirDirection.UpLeft => (-1, -1),
                _ => (0, 1),
            };

            double length = Math.Sqrt(x * x + y * y);
            return length <= 0 ? (0, 1) : (x / length, y / length);
        }

        private int GetRemainingWaveCount(object[] data)
        {
            return data.Length > 3 && data[3] is int remaining ? remaining : Math.Max(1, _skill.WaveCount <= 0 ? 1 : _skill.WaveCount);
        }

        private void QueueNextWave(object payload, int remainingWaves)
        {
            if (!SkillSupportsWaves() || remainingWaves <= 1) return;

            ActionList.Add(new DelayedAction(
                SEnvir.Now.AddMilliseconds(Math.Max(100, _skill.WaveInterval <= 0 ? _skill.TickRate : _skill.WaveInterval)),
                ActionType.DelayMagic,
                Type,
                Magic.Info.Index,
                payload,
                remainingWaves - 1));
        }

        private bool SkillSupportsWaves()
        {
            return _skill.Template is CustomSkillTemplate.DirectDamage
                or CustomSkillTemplate.ProjectileDamage
                or CustomSkillTemplate.ChainDamage
                or CustomSkillTemplate.AreaDamage
                or CustomSkillTemplate.Heal
                or CustomSkillTemplate.Buff
                or CustomSkillTemplate.Debuff
                or CustomSkillTemplate.Control
                or CustomSkillTemplate.Move;
        }

        private int GetMaxChainTargets()
        {
            return Math.Max(1, _skill?.MaxTargets > 0 ? _skill.MaxTargets : 3);
        }

        private int GetChainRadius()
        {
            return Math.Max(1, _skill?.Radius > 0 ? _skill.Radius : 4);
        }

        private int GetCastDelay(int fallback = 500)
        {
            return Math.Max(0, _skill?.CastDelay > 0 ? _skill.CastDelay : fallback);
        }

        private int GetProjectileSpeed()
        {
            return Math.Max(0, _skill?.ProjectileSpeed > 0 ? _skill.ProjectileSpeed : Globals.ProjectileSpeed);
        }

        private DateTime GetSkillDelay(MapObject target)
        {
            if (target == null) return SEnvir.Now.AddMilliseconds(GetCastDelay());

            return SEnvir.Now.AddMilliseconds(GetCastDelay() + Functions.Distance(Player.CurrentLocation, target.CurrentLocation) * GetProjectileSpeed());
        }

        private static bool IsHelpfulSkill(CustomSkillInfo skill)
        {
            return skill?.Template is CustomSkillTemplate.Heal or CustomSkillTemplate.Buff
                || skill?.TargetRule is CustomSkillTargetRule.Ally
                    or CustomSkillTargetRule.AllyOrSelf
                    or CustomSkillTargetRule.Self;
        }

        private static Stats CloneStats(Stats stats)
        {
            return stats == null || stats.Count == 0 ? null : new Stats(stats);
        }

        private sealed class SkillAreaPayload
        {
            public Point Location { get; init; }
            public MirDirection Direction { get; init; }
            public MapObject PrimaryTarget { get; init; }
            public List<MapObject> Targets { get; init; }
        }

        private sealed class SkillChainPayload
        {
            public List<MapObject> Targets { get; init; }
        }

        private sealed class SkillDamageProxyPayload
        {
            public MapObject Target { get; init; }
            public Point Location { get; init; }
        }

        private static int ApplyResistance(int power, MapObject target, Element element)
        {
            int res = target.Stats.GetResistanceValue(element);

            if (res > 0)
                power -= power * res / 10;
            else if (res < 0)
                power -= power * res / 5;

            return power;
        }
    }
}
