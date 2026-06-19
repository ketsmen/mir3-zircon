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
                    location = CurrentLocation;
                    break;
                case CustomSkillTargetRule.Ally:
                    if (!Player.CanHelpTarget(target))
                    {
                        response.Cast = false;
                        return response;
                    }
                    location = target.CurrentLocation;
                    break;
                case CustomSkillTargetRule.AllyOrSelf:
                    if (!Player.CanHelpTarget(target))
                        target = Player;
                    location = target.CurrentLocation;
                    break;
                case CustomSkillTargetRule.Enemy:
                    if (!Player.CanAttackTarget(target))
                    {
                        response.Cast = false;
                        return response;
                    }
                    location = target.CurrentLocation;
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
                    if (TryCreateAreaPayload(target, location, direction, range, out SkillAreaPayload areaPayload))
                    {
                        response.Ob = areaPayload.PrimaryTarget;
                        response.Locations.Add(areaPayload.Location);
                        response.Targets.AddRange(areaPayload.Targets.Select(x => x.ObjectID));
                        ActionList.Add(new DelayedAction(GetSkillDelay(areaPayload.Location), ActionType.DelayMagic, Type, Magic.Info.Index, areaPayload));
                        break;
                    }

                    if (IsInvalidSelfFallbackTarget(target))
                    {
                        response.Cast = false;
                        return response;
                    }

                    if (target == null)
                    {
                        response.Cast = false;
                        return response;
                    }

                    response.Ob = target;
                    if (target != null)
                        response.Targets.Add(target.ObjectID);
                    ActionList.Add(new DelayedAction(delay, ActionType.DelayMagic, Type, Magic.Info.Index, CreateTargetPayload(target)));
                    break;
                case CustomSkillTemplate.AreaDamage:
                case CustomSkillTemplate.PersistentAreaDamage:
                    response.Locations.Add(location);
                    ActionList.Add(new DelayedAction(GetSkillDelay(location), ActionType.DelayMagic, Type, Magic.Info.Index, CreateAreaPayload(target, location, direction)));
                    break;
                case CustomSkillTemplate.Summon:
                    Point summonLocation = Functions.Move(CurrentLocation, direction, -1);
                    response.Locations.Add(summonLocation);
                    ActionList.Add(new DelayedAction(
                        SEnvir.Now.AddMilliseconds(GetCastDelay()),
                        ActionType.DelayMagic,
                        Type,
                        Magic.Info.Index,
                        new SkillLocationPayload { Map = CurrentMap, Location = summonLocation, Origin = CurrentLocation }));
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
                        new SkillDamageProxyPayload { Map = CurrentMap, Target = target, Location = proxyLocation, Origin = CurrentLocation }));
                    break;
                case CustomSkillTemplate.Move:
                    response.Locations.Add(location);
                    ActionList.Add(new DelayedAction(
                        GetSkillDelay(location, 300),
                        ActionType.DelayMagic,
                        Type,
                        Magic.Info.Index,
                        new SkillLocationPayload { Map = CurrentMap, Location = location, Origin = CurrentLocation }));
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
                        ApplyDamage(GetPayloadTarget(payload));
                    applied = true;
                    break;
                case CustomSkillTemplate.DirectDamage:
                case CustomSkillTemplate.ProjectileDamage:
                    if (payload is SkillAreaPayload damageArea)
                        ApplyAreaTargets(damageArea);
                    else
                        ApplyDamage(GetPayloadTarget(payload));
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
                        ApplyHeal(GetPayloadTarget(payload));
                    applied = true;
                    break;
                case CustomSkillTemplate.Buff:
                    if (payload is SkillAreaPayload buffArea)
                        ApplyAreaTargets(buffArea);
                    else
                        ApplyBuff(GetPayloadTarget(payload), helpful: true);
                    applied = true;
                    break;
                case CustomSkillTemplate.Debuff:
                case CustomSkillTemplate.Control:
                    if (payload is SkillAreaPayload statusArea)
                        ApplyAreaTargets(statusArea);
                    else
                        ApplyControlTemplate(GetPayloadTarget(payload, out Point? origin), origin);
                    applied = true;
                    break;
                case CustomSkillTemplate.Summon:
                    if (payload is SkillLocationPayload summonPayload)
                        ApplySummon(summonPayload);
                    else if (payload is Point location)
                        ApplySummon(new SkillLocationPayload { Map = CurrentMap, Location = location, Origin = CurrentLocation });
                    break;
                case CustomSkillTemplate.DamageProxy:
                    if (payload is SkillDamageProxyPayload proxyPayload)
                        ApplyDamageProxy(proxyPayload);
                    break;
                case CustomSkillTemplate.Move:
                    if (payload is SkillLocationPayload movePayload)
                    {
                        ApplyMove(movePayload);
                        applied = true;
                    }
                    else if (payload is Point moveLocation)
                    {
                        ApplyMove(new SkillLocationPayload { Map = CurrentMap, Location = moveLocation, Origin = CurrentLocation });
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
                return new SkillChainPayload { Map = CurrentMap, Origin = CurrentLocation, Targets = targets };

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

            return new SkillChainPayload { Map = CurrentMap, Origin = CurrentLocation, Targets = targets };
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
            Map map = payload.Map ?? CurrentMap;
            if (map == null || CurrentMap != map) return;

            foreach (MapObject target in payload.Targets.ToArray())
            {
                if (target?.CurrentMap != map) continue;
                ApplyDamage(target);
            }
        }

        private void ApplyAreaTargets(SkillAreaPayload payload)
        {
            if (payload?.Targets == null) return;
            Map map = payload.Map ?? CurrentMap;
            if (map == null || CurrentMap != map) return;

            foreach (MapObject target in payload.Targets.ToArray())
            {
                if (target?.CurrentMap != map) continue;
                ApplyTemplateToTarget(target, payload);
            }
        }

        private void ApplyTemplateToTarget(MapObject target, SkillAreaPayload payload = null)
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
                    ApplyControlTemplate(target, payload?.Origin);
                    break;
            }
        }

        private bool TryCreateAreaPayload(MapObject target, Point location, MirDirection castDirection, int range, out SkillAreaPayload payload)
        {
            payload = null;

            if (GetSkillSearchRadius() <= 0 || CurrentMap == null)
                return false;

            Point center = _skill.TargetRule == CustomSkillTargetRule.Self
                ? CurrentLocation
                : target?.CurrentLocation ?? location;

            if (!Functions.InRange(CurrentLocation, center, range))
                return false;

            MirDirection direction = ReferenceEquals(target, Player) && center == CurrentLocation
                ? castDirection
                : GetSkillDirection(target, center);
            Point origin = CurrentLocation;
            List<MapObject> targets = GetAreaTargets(center, direction, origin);
            if (targets.Count == 0)
                return false;

            payload = new SkillAreaPayload
            {
                Map = CurrentMap,
                Location = center,
                Origin = origin,
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
                Map = CurrentMap,
                Location = location,
                Origin = CurrentLocation,
                Direction = ReferenceEquals(target, Player) && location == CurrentLocation
                    ? direction
                    : target?.Node != null ? Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation) : direction,
                PrimaryTarget = target,
                Targets = null,
            };
        }

        private SkillTargetPayload CreateTargetPayload(MapObject target)
        {
            return new SkillTargetPayload
            {
                Map = CurrentMap,
                Target = target,
                Origin = CurrentLocation,
            };
        }

        private MapObject GetPayloadTarget(object payload)
        {
            return GetPayloadTarget(payload, out _);
        }

        private MapObject GetPayloadTarget(object payload, out Point? origin)
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

        private List<MapObject> GetAreaTargets(Point center, MirDirection direction, Point origin)
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
                    if (!IsLocationInSkillShape(center, ob.CurrentLocation, direction, origin)) continue;

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

        private bool IsInvalidSelfFallbackTarget(MapObject target)
        {
            return _skill.TargetRule == CustomSkillTargetRule.Self &&
                   ReferenceEquals(target, Player) &&
                   !IsHelpfulSkill(_skill);
        }

        private void ApplyDamage(MapObject target)
        {
            if (target?.Node == null || target.Dead || target.CurrentMap != CurrentMap || !Player.CanAttackTarget(target)) return;

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
            Map map = payload.Map ?? CurrentMap;
            if (map == null || CurrentMap != map) return;

            Point point = payload.Location;
            if (!Functions.InRange(payload.Origin, point, _skill.Range > 0 ? _skill.Range : Globals.MagicRange)) return;

            int remainingTargets = GetMaxTargetsOrAll();

            foreach (Cell cell in map.GetCells(point, 0, GetSkillSearchRadius()))
            {
                if (cell?.Objects == null) continue;

                for (int i = cell.Objects.Count - 1; i >= 0; i--)
                {
                    if (i >= cell.Objects.Count) continue;
                    MapObject target = cell.Objects[i];
                    if (target?.Node == null || target.Dead || target.CurrentMap != map) continue;
                    if (!IsLocationInSkillShape(point, target.CurrentLocation, payload.Direction, payload.Origin)) continue;
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
            if (target?.Node == null || target.CurrentMap != CurrentMap || !Player.CanHelpTarget(target) || target.CurrentHP >= target.Stats[Stat.Health]) return;

            target.ChangeHP(Math.Min(GetTemplatePower(), target.Stats[Stat.Health] - target.CurrentHP));
            Player.LevelMagic(Magic);
        }

        private void ApplyBuff(MapObject target, bool helpful)
        {
            if (target?.Node == null) return;
            if (target.CurrentMap != CurrentMap) return;
            if (helpful && !Player.CanHelpTarget(target)) return;
            if (!helpful && !Player.CanAttackTarget(target)) return;

            TimeSpan duration = GetSkillDuration();
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

        private void ApplyControlTemplate(MapObject target, Point? origin = null)
        {
            ApplyBuff(target, helpful: false);
            ApplyControlMovement(target, origin);
        }

        private void ApplyControlMovement(MapObject target, Point? origin = null)
        {
            if (target?.Node == null || target == Player || target.CurrentMap != CurrentMap || !Player.CanAttackTarget(target)) return;

            Point source = origin ?? CurrentLocation;

            if (_skill.KnockbackDistance > 0)
            {
                MirDirection direction = Functions.DirectionFromPoint(source, target.CurrentLocation);
                target.Pushed(direction, _skill.KnockbackDistance);
            }

            if (_skill.PullDistance > 0)
            {
                MirDirection direction = Functions.DirectionFromPoint(target.CurrentLocation, source);
                target.Pushed(direction, _skill.PullDistance);
            }
        }

        private void ApplySummon(SkillLocationPayload payload)
        {
            if (payload == null || _skill.SummonMonster == null) return;
            Map map = payload.Map ?? CurrentMap;
            if (map == null || CurrentMap != map) return;

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

            if (!TrySpawnMonsterNear(monster, map, payload.Location) &&
                !TrySpawnMonsterNear(monster, map, payload.Origin))
            {
                Player.Pets.Remove(monster);
                monster.PetOwner = null;
                monster.Magics.Remove(Magic);
                return;
            }

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
            Map map = payload.Map ?? CurrentMap;
            if (map == null || CurrentMap != map || payload.Target.CurrentMap != map) return;

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

            if (!TrySpawnMonsterNear(monster, map, payload.Location) &&
                !TrySpawnMonsterNear(monster, map, payload.Origin))
                return;

            Player.LevelMagic(Magic);
        }

        private static bool TrySpawnMonsterNear(MonsterObject monster, Map map, Point origin)
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

        private TimeSpan GetDamageProxyDuration()
        {
            int duration = _skill.SummonDuration > 0
                ? _skill.SummonDuration
                : _skill.Duration > 0
                    ? _skill.Duration
                    : 10000 + Magic.Level * 5000;

            return TimeSpan.FromMilliseconds(Math.Max(1, duration));
        }

        private void ApplyMove(SkillLocationPayload payload)
        {
            if (payload == null) return;
            Map map = payload.Map ?? CurrentMap;
            if (map == null || CurrentMap != map) return;
            if (!Functions.InRange(payload.Origin, payload.Location, _skill.Range > 0 ? _skill.Range : Globals.MagicRange)) return;

            Cell cell = map.GetCell(payload.Location);
            if (cell == null || cell.Movements != null || cell.IsBlocking(Player, false)) return;

            Player.Teleport(map, payload.Location, true);
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

        private TimeSpan GetSkillDuration()
        {
            int duration = Math.Max(0, _skill.Duration);

            if (_skill.DurationPowerRate > 0)
                duration += GetTemplatePower() * _skill.DurationPowerRate;

            return TimeSpan.FromMilliseconds(Math.Max(1000, duration));
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

        private bool IsLocationInSkillShape(Point center, Point location, MirDirection direction, Point origin)
        {
            if (_skill.Shape == CustomSkillShape.Circle)
                return Functions.Distance(center, location) <= Math.Max(0, _skill.Radius);

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

            return GetSkillDelay(target.CurrentLocation);
        }

        private DateTime GetSkillDelay(Point location, int fallback = 500)
        {
            int delay = GetCastDelay(fallback);

            if (SkillUsesProjectileTravel(_skill))
                delay += Functions.Distance(Player.CurrentLocation, location) * GetProjectileSpeed();

            return SEnvir.Now.AddMilliseconds(delay);
        }

        private static bool SkillUsesProjectileTravel(CustomSkillInfo skill)
        {
            return skill?.Template == CustomSkillTemplate.ProjectileDamage
                || skill?.Effects?.Any(x => x?.Stage == CustomSkillEffectStage.Projectile) == true;
        }

        private static bool IsHelpfulSkill(CustomSkillInfo skill)
        {
            return skill?.Template is CustomSkillTemplate.Heal or CustomSkillTemplate.Buff
                || skill?.TargetRule is CustomSkillTargetRule.Ally
                    or CustomSkillTargetRule.AllyOrSelf;
        }

        private static Stats CloneStats(Stats stats)
        {
            return stats == null || stats.Count == 0 ? null : new Stats(stats);
        }

        private sealed class SkillAreaPayload
        {
            public Map Map { get; init; }
            public Point Location { get; init; }
            public Point Origin { get; init; }
            public MirDirection Direction { get; init; }
            public MapObject PrimaryTarget { get; init; }
            public List<MapObject> Targets { get; init; }
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

        private sealed class SkillDamageProxyPayload
        {
            public Map Map { get; init; }
            public MapObject Target { get; init; }
            public Point Location { get; init; }
            public Point Origin { get; init; }
        }

        private sealed class SkillLocationPayload
        {
            public Map Map { get; init; }
            public Point Location { get; init; }
            public Point Origin { get; init; }
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
