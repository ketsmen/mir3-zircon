using Library;
using Library.SystemModels;
using Server.DBModels;
using Server.Envir;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using S = Library.Network.ServerPackets;

namespace Server.Models.Monsters
{
    public sealed class CustomMonsterObject : MonsterObject
    {
        private const string ScheduledAiActionMarker = "CustomMonsterObject.ScheduleAction";
        private const string CustomDelayedAttackMarker = "CustomMonsterObject.DelayAttack";
        private const string DamageProxyRevealMarker = "CustomMonsterObject.DamageProxyReveal";

        private readonly Dictionary<int, DateTime> _skillCooldowns = new();
        private readonly Dictionary<string, DateTime> _ruleCooldowns = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _ruleExecutionCounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _aiCounters = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, StoredAiLocation> _storedAiLocations = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<MonsterObject, DateTime> _customMinionExpiry = new();
        private MapObject _ruleContextTarget;
        private ItemObject _pickupTargetItem;
        private DateTime _protectionCheckTime;
        private bool _protectedByMonsterFlag;
        private bool _reviveInitialized;
        private int _revivesRemaining;
        private int _reviveHpPercent = 100;
        private int _reviveDeathCount;
        private bool _reviveHealthDecay;
        private bool _suppressDropsWhileReviving;
        private DateTime _reviveTime;
        private List<MonsterObject> _deathContextMinions;
        private MapObject _damageProxyCaster;
        private MapObject _damageProxyTarget;
        private DateTime _damageProxyExpireTime;
        private bool _damageProxyRegistered;

        private readonly record struct StoredAiLocation(Map Map, Point Location, MirDirection Direction);

        public override bool Blocking
        {
            get
            {
                if (IsPickupCompanion) return false;

                CustomMonsterBehaviorInfo behavior = Behavior;
                if (behavior?.DisableBlocking == true) return false;
                if (!Visible && behavior?.HiddenBlocks == false) return false;

                return base.Blocking;
            }
        }

        public override bool CanAttack
        {
            get
            {
                CustomMonsterBehaviorInfo behavior = Behavior;
                if (!Visible && behavior?.HiddenCanAttack == false) return false;

                return base.CanAttack;
            }
        }

        private CustomMonsterBehaviorInfo Behavior
        {
            get
            {
                if (SEnvir.CustomSystem.TryGetBehavior(MonsterInfo, out CustomMonsterBehaviorInfo behavior))
                    return behavior;

                return null;
            }
        }

        private bool IsPickupCompanion
        {
            get
            {
                CustomMonsterBehaviorInfo behavior = Behavior;
                return behavior?.Template == CustomMonsterBehaviorTemplate.PickupCompanion || behavior?.IsPickupCompanion == true;
            }
        }

        public override bool CanBeSeenBy(PlayerObject ob)
        {
            return Visible && base.CanBeSeenBy(ob);
        }

        public bool ConfigureDamageProxy(MapObject caster, MapObject target, TimeSpan duration)
        {
            if (Behavior?.DamageProxy != true || caster == null || target == null) return false;

            _damageProxyCaster = caster;
            _damageProxyTarget = target;
            _damageProxyExpireTime = SEnvir.Now.Add(duration);

            Visible = false;
            Direction = MirDirection.DownLeft;
            return true;
        }

        public override void Activate()
        {
            if (Behavior?.AlwaysActive == true)
            {
                if (Activated) return;

                Activated = true;
                SEnvir.AddActiveObject(this);
                return;
            }

            base.Activate();
        }

        public override void DeActivate()
        {
            if (Behavior?.AlwaysActive == true) return;

            base.DeActivate();
        }

        protected override void OnSpawned()
        {
            base.OnSpawned();

            CustomMonsterBehaviorInfo behavior = Behavior;
            ApplyBehaviorRuntimeSettings(behavior, includeSpawnOnly: true);

            if (behavior?.DamageProxy == true)
                InitializeDamageProxy(behavior);

            if (behavior?.Template == CustomMonsterBehaviorTemplate.RuleSet)
                TryExecuteRules(behavior, CustomAiTriggerType.OnSpawn);
        }

        private void InitializeDamageProxy(CustomMonsterBehaviorInfo behavior)
        {
            if (_damageProxyTarget == null) return;

            if ((int)behavior.DamageProxySpawnEffect > 0)
                CurrentMap?.Broadcast(CurrentLocation, new S.MapEffect { Location = CurrentLocation, Effect = behavior.DamageProxySpawnEffect });

            ActionTime = SEnvir.Now.AddSeconds(1);
            RegisterDamageProxyTarget();

            int revealDelay = Math.Max(0, behavior.DamageProxyRevealDelay);
            if (revealDelay == 0)
            {
                RevealDamageProxy();
                return;
            }

            ActionList.Add(new DelayedAction(
                SEnvir.Now.AddMilliseconds(revealDelay),
                ActionType.Function,
                DamageProxyRevealMarker));
        }

        private void RegisterDamageProxyTarget()
        {
            if (_damageProxyRegistered || _damageProxyTarget == null) return;

            Doll.CursedList.Add(_damageProxyTarget);
            _damageProxyRegistered = true;
        }

        private void UnregisterDamageProxyTarget()
        {
            if (!_damageProxyRegistered) return;

            Doll.CursedList.Remove(_damageProxyTarget);
            _damageProxyRegistered = false;
        }

        private void RevealDamageProxy()
        {
            if (Visible) return;

            Visible = true;
            AddAllObjects();
        }

        public override void RefreshStats()
        {
            base.RefreshStats();
            ApplyBehaviorRuntimeSettings(Behavior, includeSpawnOnly: false);
        }

        private void ApplyBehaviorRuntimeSettings(CustomMonsterBehaviorInfo behavior, bool includeSpawnOnly)
        {
            if (behavior == null) return;

            Passive = behavior.Passive;
            AvoidFireWall = behavior.AvoidFireWall;
            IgnoreShield = behavior.IgnoreShield;
            IgnoreArmour = behavior.IgnoreArmour;
            PoisonType = behavior.PoisonType;
            PoisonValue = Math.Max(0, behavior.PoisonValue);
            PoisonRate = Math.Max(0, behavior.PoisonRate);
            PoisonTicks = Math.Max(1, behavior.PoisonTicks <= 0 ? 5 : behavior.PoisonTicks);
            PoisonFrequency = Math.Max(1, behavior.PoisonFrequency <= 0 ? 2 : behavior.PoisonFrequency);
            PoisonCanKill = behavior.PoisonCanKill;
            PoisonStackable = behavior.PoisonStackable;
            if (behavior.MaxMinions > 0)
                MaxMinions = behavior.MaxMinions;

            if (behavior.DisableMovement)
                MoveDelay = 0;

            if (behavior.DisableAttack)
                AttackDelay = 0;

            if (!includeSpawnOnly) return;

            NeedHarvest = behavior.NeedHarvest;
            if (behavior.HarvestCount > 0)
                HarvestCount = behavior.HarvestCount;
        }

        protected override bool InAttackRange()
        {
            if (Target?.CurrentMap != CurrentMap) return false;

            int range = Math.Max(1, Behavior?.AttackRange ?? 1);
            return Target.CurrentLocation != CurrentLocation && Functions.InRange(CurrentLocation, Target.CurrentLocation, range);
        }

        public override bool CanAttackTarget(MapObject ob)
        {
            CustomMonsterBehaviorInfo behavior = Behavior;

            if (behavior?.CanAttackPlayers == false && ob?.Race is ObjectType.Player or ObjectType.Hero)
                return false;

            return base.CanAttackTarget(ob);
        }

        public override int Attacked(MapObject attacker, int power, Element element, bool canReflect = true, bool ignoreShield = false, bool canCrit = true, bool canStruck = true)
        {
            CustomMonsterBehaviorInfo behavior = Behavior;

            if (behavior?.DamageProxy == true)
                return RedirectDamageProxyAttack(attacker, power, element, canReflect, ignoreShield, canCrit, canStruck, behavior);

            if (behavior?.CanBeKilled == false)
                return 0;

            if (!Visible && behavior?.HiddenCanBeAttacked == false)
                return 0;

            if (IsProtectedByMonsterFlag(behavior))
                return 0;

            int result = base.Attacked(attacker, power, element, canReflect, ignoreShield, canCrit, canStruck);

            if (result > 0 && !Dead && behavior?.Template == CustomMonsterBehaviorTemplate.RuleSet)
                TryExecuteRules(behavior, CustomAiTriggerType.OnStruck, attacker);

            return result;
        }

        public override bool ApplyPoison(Poison p)
        {
            CustomMonsterBehaviorInfo behavior = Behavior;

            if (behavior?.CanBeKilled == false)
                return false;

            if (!Visible && behavior?.HiddenCanBeAttacked == false)
                return false;

            if (IsProtectedByMonsterFlag(behavior))
                return false;

            return base.ApplyPoison(p);
        }

        public override void OnDespawned()
        {
            UnregisterDamageProxyTarget();
            base.OnDespawned();
        }

        public override Library.Network.Packet GetInfoPacket(PlayerObject ob)
        {
            Library.Network.Packet packet = base.GetInfoPacket(ob);
            if (packet is S.ObjectMonster monsterPacket && Behavior?.DamageProxy == true && _damageProxyTarget != null)
                monsterPacket.CustomName = FormatDamageProxyName(Behavior);

            return packet;
        }

        private string FormatDamageProxyName(CustomMonsterBehaviorInfo behavior)
        {
            string targetName = GetDamageProxyTargetName();
            string format = string.IsNullOrWhiteSpace(behavior?.DamageProxyNameFormat)
                ? "{Target} Doll"
                : behavior.DamageProxyNameFormat;

            return format
                .Replace("{Target}", targetName, StringComparison.OrdinalIgnoreCase)
                .Replace("{Name}", targetName, StringComparison.OrdinalIgnoreCase)
                .Replace("{Monster}", MonsterInfo?.MonsterName ?? Name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private string GetDamageProxyTargetName()
        {
            return _damageProxyTarget switch
            {
                MonsterObject monster => monster.MonsterInfo?.MonsterName ?? monster.Name,
                _ => _damageProxyTarget?.Name ?? string.Empty,
            };
        }

        public override void Die()
        {
            CustomMonsterBehaviorInfo behavior = Behavior;
            if (behavior?.DamageProxy == true)
            {
                UnregisterDamageProxyTarget();
                base.Die();
                DeadTime = SEnvir.Now.AddSeconds(1);
                return;
            }

            bool ruleSet = behavior?.Template == CustomMonsterBehaviorTemplate.RuleSet;

            if (ruleSet)
                _deathContextMinions = GetTrackedMinions(includeDead: false).ToList();

            if (ruleSet)
                PrepareReviveBeforeDeath(behavior);

            try
            {
                base.Die();

                if (ruleSet)
                    TryExecuteRules(behavior, CustomAiTriggerType.OnDeath);
            }
            finally
            {
                _deathContextMinions = null;
            }
        }

        public override void ProcessTarget()
        {
            CustomMonsterBehaviorInfo behavior = Behavior;
            if (behavior?.Template != CustomMonsterBehaviorTemplate.RuleSet)
            {
                base.ProcessTarget();
                return;
            }

            if (Target == null)
            {
                TryExecuteRules(behavior, CustomAiTriggerType.OnTargetTick);
                return;
            }

            if ((Poison & PoisonType.Fear) == PoisonType.Fear)
            {
                Walk(Functions.DirectionFromPoint(Target.CurrentLocation, CurrentLocation));
                return;
            }

            if (TryExecuteRules(behavior, CustomAiTriggerType.OnTargetTick))
                return;

            if (!behavior.UseBaseFallback)
                return;

            base.ProcessTarget();
        }

        public override void ProcessAI()
        {
            if (IsPickupCompanion && PetOwner?.Node != null)
            {
                ProcessPickupCompanion();
                return;
            }

            base.ProcessAI();
        }

        public override void ProcessRegen()
        {
            if (Behavior?.DisableRegen == true) return;

            base.ProcessRegen();
        }

        protected override void OnLocationChanged()
        {
            base.OnLocationChanged();

            CustomMonsterBehaviorInfo behavior = Behavior;
            if (!Dead && behavior?.Template == CustomMonsterBehaviorTemplate.RuleSet)
                TryExecuteRules(behavior, CustomAiTriggerType.OnMove);
        }

        public override void Process()
        {
            base.Process();
            ProcessCustomMinionLifetimes();

            CustomMonsterBehaviorInfo behavior = Behavior;
            if (!Dead && behavior?.DamageProxy == true)
                ProcessDamageProxy(behavior);

            if (!Dead && behavior?.Template == CustomMonsterBehaviorTemplate.RuleSet)
                TryExecuteRules(behavior, CustomAiTriggerType.OnTick);

            if (!Dead || _reviveTime == DateTime.MinValue || SEnvir.Now < _reviveTime) return;

            ReviveFromRule();
        }

        private void ProcessDamageProxy(CustomMonsterBehaviorInfo behavior)
        {
            if (_damageProxyCaster?.Node == null || _damageProxyCaster.Dead)
            {
                SetHP(0);
                return;
            }

            if (_damageProxyTarget?.Node == null || _damageProxyTarget.Dead)
            {
                SetHP(0);
                return;
            }

            if (_damageProxyExpireTime != DateTime.MinValue && _damageProxyExpireTime < SEnvir.Now)
                SetHP(0);
        }

        private int RedirectDamageProxyAttack(MapObject attacker, int power, Element element, bool canReflect, bool ignoreShield, bool canCrit, bool canStruck, CustomMonsterBehaviorInfo behavior)
        {
            if (_damageProxyTarget == null || attacker?.CurrentMap == null) return 0;

            int range = behavior.DamageProxyRange > 0 ? behavior.DamageProxyRange : Globals.MagicRange;
            if (attacker.CurrentMap != _damageProxyTarget.CurrentMap || !Functions.InRange(attacker.CurrentLocation, _damageProxyTarget.CurrentLocation, range))
                return 0;

            _damageProxyTarget.Attacked(attacker, power, element, canReflect, ignoreShield, canCrit, canStruck);
            return 0;
        }

        public override void Drop(PlayerObject owner, int players, decimal rate)
        {
            if (_suppressDropsWhileReviving && (_revivesRemaining > 0 || _reviveTime != DateTime.MinValue))
                return;

            base.Drop(owner, players, rate);
        }

        protected override void Attack()
        {
            CustomMonsterBehaviorInfo behavior = Behavior;
            if (behavior == null)
            {
                base.Attack();
                return;
            }

            if (behavior.Template == CustomMonsterBehaviorTemplate.RuleSet)
            {
                if (TryExecuteRules(behavior, CustomAiTriggerType.OnAttack))
                    return;

                if (behavior.UseBaseFallback)
                    base.Attack();
                return;
            }

            Direction = Functions.DirectionFromPoint(CurrentLocation, Target.CurrentLocation);

            if (TryCastSkill(behavior))
                return;

            if (behavior.Template is (CustomMonsterBehaviorTemplate.Ranged or CustomMonsterBehaviorTemplate.Caster or CustomMonsterBehaviorTemplate.Boss) && Math.Max(1, behavior.AttackRange) > 1)
            {
                Broadcast(new S.ObjectRangeAttack
                {
                    ObjectID = ObjectID,
                    Direction = Direction,
                    Location = CurrentLocation,
                    Targets = new List<uint> { Target.ObjectID },
                    AttackElement = AttackElement
                });

                UpdateAttackTime();

                ActionList.Add(new DelayedAction(
                    SEnvir.Now.AddMilliseconds(500),
                    ActionType.DelayAttack,
                    Target,
                    GetDC(),
                    AttackElement));

                return;
            }

            base.Attack();
        }

        public override void ProcessAction(DelayedAction action)
        {
            if (action.Type == ActionType.DelayMagic && action.Data.Length > 2 && action.Data[1] is int && action.Data[2] is CustomSkillInfo skill)
            {
                int remainingTicks = action.Data.Length > 4 && action.Data[4] is int ticks ? ticks : 0;
                CompleteSkill(skill, action.Data.Length > 3 ? action.Data[3] : null, remainingTicks);
                return;
            }

            if (action.Type == ActionType.DelayAttack &&
                action.Data.Length > 3 &&
                action.Data[3] is string attackMarker &&
                attackMarker.Equals(CustomDelayedAttackMarker, StringComparison.Ordinal))
            {
                ProcessCustomDelayedAttack(action);
                return;
            }

            if (action.Type == ActionType.Function &&
                action.Data.Length > 1 &&
                action.Data[0] is string marker &&
                marker.Equals(ScheduledAiActionMarker, StringComparison.Ordinal) &&
                action.Data[1] is string actionName)
            {
                ExecuteScheduledAiAction(
                    actionName,
                    action.Data.Length > 2 && action.Data[2] is int depth ? depth : 0,
                    action.Data.Length > 3 ? action.Data[3] as MapObject : null);
                return;
            }

            if (action.Type == ActionType.Function &&
                action.Data.Length > 0 &&
                action.Data[0] is string revealMarker &&
                revealMarker.Equals(DamageProxyRevealMarker, StringComparison.Ordinal))
            {
                RevealDamageProxy();
                return;
            }

            base.ProcessAction(action);
        }

        private void ProcessCustomDelayedAttack(DelayedAction action)
        {
            if (action.Data[0] is not MapObject target || action.Data[1] is not int power || action.Data[2] is not Element element)
                return;

            CustomMonsterAiActionInfo sourceAction = action.Data.Length > 4 ? action.Data[4] as CustomMonsterAiActionInfo : null;
            int damage = AttackWithActionOptions(target, power, element, sourceAction);
            if (damage <= 0) return;

            bool drainMp = action.Data.Length > 5 && action.Data[5] is bool drain && drain ||
                           action.Data.Length > 4 && action.Data[4] is bool legacyDrain && legacyDrain;
            if (drainMp && target.CurrentMP > 0)
                target.ChangeMP(-Math.Min(target.CurrentMP, damage));

            ExecuteOnHitAction(sourceAction, target);
        }

        private int AttackWithActionOptions(MapObject target, int power, Element element, CustomMonsterAiActionInfo sourceAction)
        {
            if (sourceAction == null) return Attack(target, power, element);

            bool ignoreShield = GetActionOptionBool(sourceAction, "IgnoreShield", false);
            bool ignoreArmour = GetActionOptionBool(sourceAction, "IgnoreArmour", false);
            if (!ignoreShield && !ignoreArmour)
            {
                int normalDamage = Attack(target, power, element);
                ApplyActionPush(sourceAction, target, normalDamage);
                return normalDamage;
            }

            bool previousIgnoreShield = IgnoreShield;
            bool previousIgnoreArmour = IgnoreArmour;

            try
            {
                IgnoreShield |= ignoreShield;
                IgnoreArmour |= ignoreArmour;
                int damage = Attack(target, power, element);
                ApplyActionPush(sourceAction, target, damage);
                return damage;
            }
            finally
            {
                IgnoreShield = previousIgnoreShield;
                IgnoreArmour = previousIgnoreArmour;
            }
        }

        private void ApplyActionPush(CustomMonsterAiActionInfo action, MapObject target, int damage)
        {
            if (damage <= 0 || target == null || target == this) return;

            int distance = GetActionOptionInt(action, "PushDistance", 0);
            if (distance <= 0) return;

            string directionName = GetActionOptionValue(action, "PushDirection");
            MirDirection direction = string.IsNullOrWhiteSpace(directionName) || !TryGetActionDirection(directionName, target, out MirDirection configured)
                ? Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation)
                : configured;

            target.Pushed(direction, distance);
        }

        private void ProcessPickupCompanion()
        {
            ProcessRegen();

            if (!PetOwner.VisibleObjects.Contains(this))
            {
                PetRecall();
                return;
            }

            if (_pickupTargetItem?.Node == null || _pickupTargetItem.CurrentMap != CurrentMap || !Functions.InRange(CurrentLocation, _pickupTargetItem.CurrentLocation, ViewRange))
                _pickupTargetItem = null;

            if (_pickupTargetItem == null)
                SearchPickupTarget();

            if (_pickupTargetItem == null)
            {
                if (!Functions.InRange(CurrentLocation, PetOwner.CurrentLocation, 2))
                    MoveTo(PetOwner.CurrentLocation);

                return;
            }

            if (_pickupTargetItem.CurrentLocation != CurrentLocation)
            {
                MoveTo(_pickupTargetItem.CurrentLocation);
                return;
            }

            if (_pickupTargetItem.PickUpItem(PetOwner))
                _pickupTargetItem = null;
            else
            {
                _pickupTargetItem = null;
                SearchTime = SEnvir.Now.AddSeconds(5);
            }
        }

        private void SearchPickupTarget()
        {
            if (!CanMove || SEnvir.Now < SearchTime || PetOwner?.VisibleObjects == null)
                return;

            SearchTime = SEnvir.Now.AddMilliseconds(Math.Max(500, Behavior?.RoamDelay ?? 2000));

            int bestDistance = int.MaxValue;
            List<ItemObject> closest = new();

            foreach (MapObject ob in PetOwner.VisibleObjects)
            {
                if (ob?.Race != ObjectType.Item || ob.CurrentMap != CurrentMap) continue;

                int distance = Functions.Distance(ob.CurrentLocation, CurrentLocation);
                if (distance > ViewRange || distance > bestDistance) continue;

                ItemObject item = (ItemObject)ob;
                if (!item.CanPickUpItem(PetOwner)) continue;

                if (distance != bestDistance)
                    closest.Clear();

                closest.Add(item);
                bestDistance = distance;
            }

            if (closest.Count > 0)
                _pickupTargetItem = closest[SEnvir.Random.Next(closest.Count)];
        }

        private bool TryCastSkill(CustomMonsterBehaviorInfo behavior)
        {
            if (behavior.Skills == null || behavior.Skills.Count == 0)
                return false;

            int baseChance = Math.Max(0, behavior.SkillChance);
            int hpPercent = GetHealthPercent(this);

            foreach (CustomMonsterSkillInfo entry in behavior.Skills.OrderByDescending(x => x.Chance).ThenBy(x => x.Index))
            {
                if (entry?.Magic == null) continue;
                if (hpPercent < entry.MinHealthPercent || hpPercent > entry.MaxHealthPercent) continue;
                if (_skillCooldowns.TryGetValue(entry.Magic.Index, out DateTime nextTime) && SEnvir.Now < nextTime) continue;

                int chance = entry.Chance > 0 ? entry.Chance : baseChance;
                if (chance <= 0 || SEnvir.Random.Next(100) >= chance) continue;

                if (!SEnvir.CustomSystem.TryGetSkill(entry.Magic, out CustomSkillInfo skill) || skill == null || !skill.Enabled) continue;

                int range = entry.Range > 0 ? entry.Range : skill.Range > 0 ? skill.Range : Math.Max(1, behavior.AttackRange);
                CustomMonsterAiActionInfo action = CreateSkillAction(entry, range);
                MapObject target = GetSkillActionTarget(action, skill);
                Point location = GetActionLocation(action, target) ?? CurrentLocation;

                if (!SkillCastInRange(skill, target, location, range, GetSkillPreviewDirection(target, location))) continue;
                if (!CastSkill(entry, skill, target, location, range)) continue;

                _skillCooldowns[entry.Magic.Index] = SEnvir.Now.AddMilliseconds(Math.Max(100, entry.Cooldown));
                return true;
            }

            return false;
        }

        private bool TryExecuteRules(CustomMonsterBehaviorInfo behavior, CustomAiTriggerType trigger, MapObject contextTarget = null)
        {
            if (behavior?.Rules == null || behavior.Actions == null || behavior.Rules.Count == 0)
                return false;

            Dictionary<string, CustomMonsterAiActionInfo> actions = behavior.Actions
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            int hpPercent = GetHealthPercent(this);
            MapObject oldContextTarget = _ruleContextTarget;
            _ruleContextTarget = contextTarget ?? oldContextTarget;

            try
            {
                foreach (CustomMonsterAiRuleInfo rule in behavior.Rules
                             .Where(x => x != null && x.Trigger == trigger)
                             .OrderBy(x => x.Priority)
                             .ThenBy(x => x.Index))
                {
                    if (hpPercent < rule.MinHealthPercent || hpPercent > (rule.MaxHealthPercent == 0 ? 100 : rule.MaxHealthPercent)) continue;
                    if (!RuleConditionMet(rule)) continue;

                    string cooldownKey = GetRuleCooldownKey(rule);
                    if (rule.MaxExecutions > 0 && _ruleExecutionCounts.TryGetValue(cooldownKey, out int executions) && executions >= rule.MaxExecutions) continue;

                    if (rule.Cooldown > 0 && _ruleCooldowns.TryGetValue(cooldownKey, out DateTime nextTime) && SEnvir.Now < nextTime) continue;

                    int chance = rule.Chance <= 0 ? 100 : rule.Chance;
                    if (chance < 100 && SEnvir.Random.Next(100) >= chance) continue;

                    if (!actions.TryGetValue(rule.ActionName, out CustomMonsterAiActionInfo action)) continue;
                    if (!ExecuteAiAction(action, actions, 0)) continue;

                    if (rule.Cooldown > 0)
                        _ruleCooldowns[cooldownKey] = SEnvir.Now.AddMilliseconds(rule.Cooldown);

                    if (rule.MaxExecutions > 0)
                        _ruleExecutionCounts[cooldownKey] = _ruleExecutionCounts.TryGetValue(cooldownKey, out int current) ? current + 1 : 1;

                    return true;
                }
            }
            finally
            {
                _ruleContextTarget = oldContextTarget;
            }

            return false;
        }

        private void PrepareReviveBeforeDeath(CustomMonsterBehaviorInfo behavior)
        {
            if (behavior?.Rules == null || behavior.Actions == null) return;

            Dictionary<string, CustomMonsterAiActionInfo> actions = behavior.Actions
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            foreach (CustomMonsterAiRuleInfo rule in behavior.Rules
                         .Where(x => x != null && x.Trigger == CustomAiTriggerType.OnDeath)
                         .OrderBy(x => x.Priority)
                         .ThenBy(x => x.Index))
            {
                if (rule.Condition != CustomAiConditionType.Always) continue;
                if (!actions.TryGetValue(rule.ActionName, out CustomMonsterAiActionInfo action)) continue;
                if (action.Action != CustomAiActionType.ReviveSelf) continue;
                if (!GetActionOptionBool(action, "SuppressDrops", true)) continue;

                if (!_reviveInitialized)
                {
                    _revivesRemaining = Math.Max(0, action.FixedCount) + (action.RandomCount > 0 ? SEnvir.Random.Next(action.RandomCount + 1) : 0);
                    _reviveInitialized = true;
                }

                _suppressDropsWhileReviving = _revivesRemaining > 0;
                return;
            }
        }

        private bool RuleConditionMet(CustomMonsterAiRuleInfo rule)
        {
            List<RuleConditionSpec> conditions = GetRuleConditionSpecs(rule);
            if (conditions.Count == 0) return false;

            bool result = rule.ConditionJoin == CustomAiConditionJoin.Or
                ? conditions.Any(EvaluateRuleCondition)
                : conditions.All(EvaluateRuleCondition);

            return rule.InvertCondition ? !result : result;
        }

        private bool EvaluateRuleCondition(RuleConditionSpec spec)
        {
            MapObject target = GetExecutionTarget();
            int targetDistance = target == null ? int.MaxValue : Functions.Distance(CurrentLocation, target.CurrentLocation);
            int radius = spec.Radius > 0 ? spec.Radius : Config.MaxViewRange;
            int value = Math.Max(0, spec.Value);
            int counter = GetAiCounter(spec.CounterName);

            bool result = spec.Condition switch
            {
                CustomAiConditionType.Always => true,
                CustomAiConditionType.TargetInAttackRange => target != null && InActionRange(target, null),
                CustomAiConditionType.TargetOutOfAttackRange => target != null && !InActionRange(target, null),
                CustomAiConditionType.SameCellAsTarget => target != null && CurrentLocation == target.CurrentLocation,
                CustomAiConditionType.CanAttack => CanAttack,
                CustomAiConditionType.CanMove => CanMove,
                CustomAiConditionType.HealthBelow => Stats[Stat.Health] > 0 && GetHealthPercent(this) <= value,
                CustomAiConditionType.HealthAbove => Stats[Stat.Health] > 0 && GetHealthPercent(this) >= value,
                CustomAiConditionType.TargetHealthBelow => target != null && GetHealthPercent(target) <= value,
                CustomAiConditionType.TargetHealthAbove => target != null && GetHealthPercent(target) >= value,
                CustomAiConditionType.TargetCountAtLeast => CurrentMap != null && GetTargets(CurrentMap, CurrentLocation, radius).Count >= Math.Max(1, value),
                CustomAiConditionType.TargetExists => target != null,
                CustomAiConditionType.TargetMissing => target == null,
                CustomAiConditionType.TargetDistanceBelow => target != null && targetDistance <= value,
                CustomAiConditionType.TargetDistanceAbove => target != null && targetDistance >= value,
                CustomAiConditionType.TargetInLine => IsTargetInLine(target, value),
                CustomAiConditionType.TargetOutOfLine => target != null && !IsTargetInLine(target, value),
                CustomAiConditionType.TargetIsPlayer => target?.Race == ObjectType.Player,
                CustomAiConditionType.TargetIsHero => target?.Race == ObjectType.Hero,
                CustomAiConditionType.TargetIsPlayerOrHero => target?.Race is ObjectType.Player or ObjectType.Hero,
                CustomAiConditionType.TargetIsMonster => target?.Race == ObjectType.Monster,
                CustomAiConditionType.TargetIsPetMonster => target is MonsterObject { PetOwner: not null },
                CustomAiConditionType.TargetIsWildMonster => target is MonsterObject { PetOwner: null },
                CustomAiConditionType.TargetIsBossMonster => target is MonsterObject { MonsterInfo: { IsBoss: true } },
                CustomAiConditionType.TargetClassWarrior => TargetClassIs(target, MirClass.Warrior),
                CustomAiConditionType.TargetClassWizard => TargetClassIs(target, MirClass.Wizard),
                CustomAiConditionType.TargetClassTaoist => TargetClassIs(target, MirClass.Taoist),
                CustomAiConditionType.TargetClassAssassin => TargetClassIs(target, MirClass.Assassin),
                CustomAiConditionType.MinionCountBelow => MinionList.Count < value,
                CustomAiConditionType.MinionCountAbove => MinionList.Count > value,
                CustomAiConditionType.SelfHasBuff => HasRuleBuff(this, spec.BuffType),
                CustomAiConditionType.SelfMissingBuff => spec.BuffType != BuffType.None && !HasRuleBuff(this, spec.BuffType),
                CustomAiConditionType.TargetHasBuff => HasRuleBuff(target, spec.BuffType),
                CustomAiConditionType.TargetMissingBuff => target != null && spec.BuffType != BuffType.None && !HasRuleBuff(target, spec.BuffType),
                CustomAiConditionType.CounterBelow => counter < value,
                CustomAiConditionType.CounterAbove => counter > value,
                CustomAiConditionType.CounterEquals => counter == value,
                CustomAiConditionType.Visible => Visible,
                CustomAiConditionType.Hidden => !Visible,
                _ => false,
            };

            return spec.Invert ? !result : result;
        }

        private static List<RuleConditionSpec> GetRuleConditionSpecs(CustomMonsterAiRuleInfo rule)
        {
            List<RuleConditionSpec> result = new();
            if (rule == null) return result;

            if (rule.Condition != CustomAiConditionType.Always || string.IsNullOrWhiteSpace(rule.Conditions))
                result.Add(CreateRuleConditionSpec(rule, rule.Condition.ToString()));

            if (string.IsNullOrWhiteSpace(rule.Conditions))
                return result;

            foreach (string token in SplitRuleConditionTokens(rule.Conditions))
                if (TryParseRuleConditionSpec(rule, token, out RuleConditionSpec spec))
                    result.Add(spec);

            return result;
        }

        private static IEnumerable<string> SplitRuleConditionTokens(string conditions)
        {
            return conditions
                .Replace("&&", ";", StringComparison.Ordinal)
                .Replace("||", ";", StringComparison.Ordinal)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static bool TryParseRuleConditionSpec(CustomMonsterAiRuleInfo rule, string token, out RuleConditionSpec spec)
        {
            spec = default;
            if (string.IsNullOrWhiteSpace(token)) return false;

            spec = CreateRuleConditionSpec(rule, token);
            return true;
        }

        private static RuleConditionSpec CreateRuleConditionSpec(CustomMonsterAiRuleInfo rule, string token)
        {
            bool invert = false;
            token = token?.Trim() ?? string.Empty;

            if (token.StartsWith("!", StringComparison.Ordinal))
            {
                invert = true;
                token = token[1..].Trim();
            }

            string[] parts = token.Split(':', StringSplitOptions.TrimEntries);

            if (!Enum.TryParse(parts[0], true, out CustomAiConditionType condition))
                condition = rule.Condition;

            RuleConditionSpec spec = new()
            {
                Condition = condition,
                Value = rule.Value,
                Radius = rule.Radius,
                BuffType = rule.BuffType,
                CounterName = rule.CounterName,
                Invert = invert,
            };

            if (RuleConditionUsesCounter(condition))
            {
                ApplyCounterRuleConditionArguments(ref spec, parts);
                return spec;
            }

            if (parts.Length > 1)
                ApplyRuleConditionArgument(ref spec, parts[1], firstArgument: true);

            if (parts.Length > 2)
                ApplyRuleConditionArgument(ref spec, parts[2], firstArgument: false);

            if (parts.Length > 3)
                spec.CounterName = parts[3];

            return spec;
        }

        private static void ApplyCounterRuleConditionArguments(ref RuleConditionSpec spec, string[] parts)
        {
            if (parts.Length <= 1) return;

            if (parts.Length > 2)
            {
                bool firstIsValue = int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int firstValue);
                bool secondIsValue = int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int secondValue);

                if (firstIsValue)
                {
                    spec.Value = firstValue;
                    spec.CounterName = parts[2];
                }
                else if (secondIsValue)
                {
                    spec.CounterName = parts[1];
                    spec.Value = secondValue;
                }
                else
                {
                    spec.CounterName = parts[2];
                }
            }
            else
            {
                ApplyRuleConditionArgument(ref spec, parts[1], firstArgument: true);
            }

            if (parts.Length > 3)
                spec.CounterName = parts[3];
        }

        private static void ApplyRuleConditionArgument(ref RuleConditionSpec spec, string value, bool firstArgument)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            if (RuleConditionUsesBuff(spec.Condition) && Enum.TryParse(value, true, out BuffType buffType))
            {
                spec.BuffType = buffType;
                return;
            }

            if (RuleConditionUsesCounter(spec.Condition))
            {
                if (firstArgument && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int counterValue))
                    spec.Value = counterValue;
                else
                    spec.CounterName = value;

                return;
            }

            if (firstArgument && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
                spec.Value = intValue;
            else if (!firstArgument && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int radius))
                spec.Radius = radius;
        }

        private static bool RuleConditionUsesBuff(CustomAiConditionType condition)
        {
            return condition is CustomAiConditionType.SelfHasBuff
                or CustomAiConditionType.SelfMissingBuff
                or CustomAiConditionType.TargetHasBuff
                or CustomAiConditionType.TargetMissingBuff;
        }

        private static bool RuleConditionUsesCounter(CustomAiConditionType condition)
        {
            return condition is CustomAiConditionType.CounterBelow
                or CustomAiConditionType.CounterAbove
                or CustomAiConditionType.CounterEquals;
        }

        private int GetAiCounter(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return 0;

            return _aiCounters.TryGetValue(name.Trim(), out int value) ? value : 0;
        }

        private static bool HasRuleBuff(MapObject target, BuffType buffType)
        {
            return buffType != BuffType.None && target?.Buffs?.Any(x => x.Type == buffType) == true;
        }

        private bool IsProtectedByMonsterFlag(CustomMonsterBehaviorInfo behavior)
        {
            if (behavior == null || CurrentMap == null)
                return false;

            HashSet<MonsterFlag> protectionFlags = GetProtectionFlags(behavior);
            if (protectionFlags.Count == 0)
                return false;

            if (SEnvir.Now < _protectionCheckTime)
                return _protectedByMonsterFlag;

            _protectionCheckTime = SEnvir.Now.AddMilliseconds(Math.Max(100, behavior.ProtectionCheckInterval));
            _protectedByMonsterFlag = false;

            int radius = Math.Max(0, behavior.ProtectionRadius);
            IEnumerable<MapObject> objects = radius > 0
                ? GetAllObjects(CurrentLocation, radius)
                : CurrentMap.Objects;

            foreach (MapObject ob in objects)
            {
                if (ob == this || ob?.Race != ObjectType.Monster) continue;

                MonsterObject monster = (MonsterObject)ob;
                if (monster.Dead || monster.MonsterInfo == null || !protectionFlags.Contains(monster.MonsterInfo.Flag)) continue;

                _protectedByMonsterFlag = true;
                break;
            }

            return _protectedByMonsterFlag;
        }

        private static HashSet<MonsterFlag> GetProtectionFlags(CustomMonsterBehaviorInfo behavior)
        {
            HashSet<MonsterFlag> flags = new();
            if (behavior == null) return flags;

            if (behavior.ProtectedByMonsterFlag != MonsterFlag.None)
                flags.Add(behavior.ProtectedByMonsterFlag);

            if (string.IsNullOrWhiteSpace(behavior.ProtectedByMonsterFlags))
                return flags;

            foreach (string token in behavior.ProtectedByMonsterFlags.Split(['|', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (Enum.TryParse(token, true, out MonsterFlag flag) && flag != MonsterFlag.None)
                    flags.Add(flag);

            return flags;
        }

        private bool ExecuteAiAction(CustomMonsterAiActionInfo action, Dictionary<string, CustomMonsterAiActionInfo> actions, int depth)
        {
            if (action == null || depth > 8) return false;

            int actionChance = Math.Max(0, Math.Min(100, GetActionOptionInt(action, "ActionChance", 100)));
            if (actionChance <= 0 || actionChance < 100 && SEnvir.Random.Next(100) >= actionChance)
                return false;

            switch (action.Action)
            {
                case CustomAiActionType.FaceTarget:
                    MapObject faceTarget = GetActionTarget(action, requireAttackTarget: false);
                    if (faceTarget == null || faceTarget == this) return false;
                    Direction = Functions.DirectionFromPoint(CurrentLocation, faceTarget.CurrentLocation);
                    return true;

                case CustomAiActionType.MoveToTarget:
                    MapObject moveTarget = GetActionTarget(action, requireAttackTarget: false);
                    if (!CanMove || moveTarget == null || moveTarget == this) return false;
                    if (CurrentLocation == moveTarget.CurrentLocation) return ExecuteRandomStep();
                    MoveTo(moveTarget.CurrentLocation);
                    return true;

                case CustomAiActionType.MoveAwayFromTarget:
                    return ExecuteMoveAwayFromTarget(action);

                case CustomAiActionType.HazardRoam:
                    return ExecuteHazardRoam(action, actions, depth + 1);

                case CustomAiActionType.RandomStepWhenSameCell:
                    return ExecuteRandomStep();

                case CustomAiActionType.MeleeAttack:
                    MapObject meleeTarget = GetActionTarget(action, requireAttackTarget: true);
                    if (!CanAttack || meleeTarget == null || !InActionRange(meleeTarget, action)) return false;
                    ExecuteMeleeAttack(action, meleeTarget);
                    return true;

                case CustomAiActionType.RangeAttack:
                    return ExecuteRangeAttack(action);

                case CustomAiActionType.LineAttack:
                    return ExecuteLineAttack(action);

                case CustomAiActionType.AttackMagic:
                    return ExecuteAttackMagic(action);

                case CustomAiActionType.AttackAoE:
                    return ExecuteAttackAoE(action);

                case CustomAiActionType.LineAoE:
                    return ExecuteLineAoE(action);

                case CustomAiActionType.MassAreaDamage:
                    return ExecuteMassAreaDamage(action);

                case CustomAiActionType.SpawnMinions:
                    return ExecuteSpawnMinions(action);

                case CustomAiActionType.TeleportSelf:
                    return ExecuteTeleportSelf(action);

                case CustomAiActionType.TeleportNearTarget:
                    return ExecuteTeleportNearTarget(action);

                case CustomAiActionType.WeightedChoice:
                    return ExecuteWeightedChoice(action, actions, depth + 1);

                case CustomAiActionType.Sequence:
                    return ExecuteSequence(action, actions, depth + 1);

                case CustomAiActionType.SearchTarget:
                    return ExecuteSearchTarget(action);

                case CustomAiActionType.ClearTarget:
                    Target = null;
                    return true;

                case CustomAiActionType.SetCounter:
                    return ExecuteSetCounter(action, additive: false);

                case CustomAiActionType.ChangeCounter:
                    return ExecuteSetCounter(action, additive: true);

                case CustomAiActionType.Show:
                    return ExecuteVisibility(true, action);

                case CustomAiActionType.Hide:
                    return ExecuteVisibility(false, action);

                case CustomAiActionType.ApplyPoison:
                    return ExecuteApplyPoison(action);

                case CustomAiActionType.AddBuff:
                    return ExecuteAddBuff(action);

                case CustomAiActionType.RemoveBuff:
                    return ExecuteRemoveBuff(action);

                case CustomAiActionType.TeleportTarget:
                    return ExecuteTeleportTarget(action);

                case CustomAiActionType.PullTarget:
                    return ExecutePullTarget(action);

                case CustomAiActionType.PushTarget:
                    return ExecutePushTarget(action);

                case CustomAiActionType.CastSkill:
                    return ExecuteCastSkill(action);

                case CustomAiActionType.ReviveSelf:
                    return ExecuteReviveSelf(action);

                case CustomAiActionType.WakeNearby:
                    return ExecuteWakeNearby(action);

                case CustomAiActionType.DelayActionTime:
                    return ExecuteDelayTime(action, delayAction: true, delayMove: false, delayCell: false);

                case CustomAiActionType.DelayMoveTime:
                    return ExecuteDelayTime(action, delayAction: false, delayMove: true, delayCell: false);

                case CustomAiActionType.DelayCellTime:
                    return ExecuteDelayTime(action, delayAction: false, delayMove: false, delayCell: true);

                case CustomAiActionType.ScheduleAction:
                    return ExecuteScheduleAction(action, actions, depth);

                case CustomAiActionType.KillSelf:
                    SetHP(0);
                    return true;

                case CustomAiActionType.MapEffect:
                    return ExecuteMapEffect(action);

                case CustomAiActionType.ObjectEffect:
                    return ExecuteObjectEffect(action);

                case CustomAiActionType.StoreLocation:
                    return ExecuteStoreLocation(action);

                case CustomAiActionType.ReturnToStoredLocation:
                    return ExecuteReturnToStoredLocation(action);

                case CustomAiActionType.TeleportTargetsRandom:
                    return ExecuteTeleportTargetsRandom(action);

                case CustomAiActionType.PurifyTargets:
                    return ExecutePurifyTargets(action);

                case CustomAiActionType.RecallMonstersByFlag:
                    return ExecuteRecallMonstersByFlag(action);

                case CustomAiActionType.SwapRandomMinionLocations:
                    return ExecuteSwapRandomMinionLocations(action);

                case CustomAiActionType.KillMinions:
                    return ExecuteKillMinions(action);

                case CustomAiActionType.BroadcastMessage:
                    return ExecuteBroadcastMessage(action);

                case CustomAiActionType.TeleportTargetsToRegion:
                    return ExecuteTeleportTargetsToRegion(action);

                case CustomAiActionType.DespawnSelf:
                    return ExecuteDespawnSelf(action);

                case CustomAiActionType.SpawnSpellObject:
                    return ExecuteSpawnSpellObject(action);

                default:
                    return false;
            }
        }

        private bool ExecuteRandomStep()
        {
            if (!CanMove) return false;

            MirDirection direction = (MirDirection)SEnvir.Random.Next(8);
            int rotation = SEnvir.Random.Next(2) == 0 ? 1 : -1;

            for (int d = 0; d < 8; d++)
            {
                if (Walk(direction)) return true;

                direction = Functions.ShiftDirection(direction, rotation);
            }

            return false;
        }

        private bool ExecuteMoveAwayFromTarget(CustomMonsterAiActionInfo action)
        {
            if (!CanMove) return false;

            MapObject target = GetActionTarget(action, requireAttackTarget: false);
            if (target == null || target == this || target.CurrentMap != CurrentMap)
                return false;

            if (CurrentLocation == target.CurrentLocation)
                return ExecuteRandomStep();

            MirDirection direction = Functions.DirectionFromPoint(target.CurrentLocation, CurrentLocation);
            int rotation = SEnvir.Random.Next(2) == 0 ? 1 : -1;

            for (int d = 0; d < 8; d++)
            {
                if (Walk(direction)) return true;

                direction = Functions.ShiftDirection(direction, rotation);
            }

            return false;
        }

        private bool ExecuteHazardRoam(CustomMonsterAiActionInfo action, Dictionary<string, CustomMonsterAiActionInfo> actions, int depth)
        {
            if (CurrentMap == null || !CanMove) return false;

            bool executed = false;
            bool attackWhenBlocked = GetActionOptionBool(action, "AttackWhenBlocked", false);

            if (attackWhenBlocked &&
                GetActionOptionBool(action, "AttackCurrentCell", true) &&
                HasHazardCollision(CurrentCell, action))
                executed |= ExecuteHazardCollision(action, actions, depth, CurrentLocation);

            int forwardChance = Math.Max(0, Math.Min(100, GetActionOptionInt(action, "ForwardChance", 80)));
            if (SEnvir.Random.Next(100) >= forwardChance)
            {
                Turn((MirDirection)SEnvir.Random.Next(8));
                return true;
            }

            Point nextLocation = Functions.Move(CurrentLocation, Direction);
            Cell nextCell = CurrentMap.GetCell(nextLocation);

            if (attackWhenBlocked &&
                GetActionOptionBool(action, "AttackBlockedCell", false) &&
                HasHazardCollision(nextCell, action))
                executed |= ExecuteHazardCollision(action, actions, depth, nextLocation);

            if (Walk(Direction))
                return true;

            if (GetActionOptionBool(action, "SearchAlternate", false))
            {
                MirDirection direction = Direction;
                int rotation = SEnvir.Random.Next(2) == 0 ? 1 : -1;

                for (int d = 0; d < 7; d++)
                {
                    direction = Functions.ShiftDirection(direction, rotation);
                    Point shiftedLocation = Functions.Move(CurrentLocation, direction);
                    Cell shiftedCell = CurrentMap.GetCell(shiftedLocation);

                    if (attackWhenBlocked &&
                        GetActionOptionBool(action, "AttackBlockedCell", false) &&
                        HasHazardCollision(shiftedCell, action))
                        executed |= ExecuteHazardCollision(action, actions, depth, shiftedLocation);

                    if (Walk(direction))
                        return true;
                }
            }

            if (GetActionOptionBool(action, "TurnWhenBlocked", true))
            {
                Turn((MirDirection)SEnvir.Random.Next(8));
                return true;
            }

            return executed;
        }

        private bool HasHazardCollision(Cell cell, CustomMonsterAiActionInfo action)
        {
            if (cell?.Objects == null) return false;

            bool requireBlocking = GetActionOptionBool(action, "RequireBlocking", true);

            foreach (MapObject ob in cell.Objects.ToArray())
            {
                if (ob == this || ob?.Node == null || ob.Dead || !ob.Visible) continue;
                if (requireBlocking && !ob.Blocking) continue;
                if (CanAttackTarget(ob)) return true;
            }

            return false;
        }

        private bool ExecuteHazardCollision(CustomMonsterAiActionInfo action, Dictionary<string, CustomMonsterAiActionInfo> actions, int depth, Point origin)
        {
            string collisionAction = GetActionOptionValue(action, "CollisionAction");
            if (!string.IsNullOrWhiteSpace(collisionAction) &&
                actions.TryGetValue(collisionAction, out CustomMonsterAiActionInfo selected) &&
                !ReferenceEquals(selected, action))
                return ExecuteAiAction(selected, actions, depth + 1);

            if (!CanAttack && !GetActionOptionBool(action, "IgnoreAttackCooldown", false)) return false;

            int radius = Math.Max(0, GetActionOptionInt(action, "CollisionRadius", action.Radius > 0 ? action.Radius : 1));
            IEnumerable<MapObject> targets = GetTargets(CurrentMap, origin, radius)
                .Where(x => x != this && CanAttackTarget(x));

            if (action.TargetChance < 100)
                targets = targets.Where(_ => SEnvir.Random.Next(100) < Math.Max(0, action.TargetChance));

            if (action.RandomTargetCount > 0)
                targets = targets.OrderBy(_ => SEnvir.Random.Next()).Take(action.RandomTargetCount);
            else if (action.MaxTargets > 0)
                targets = targets.Take(action.MaxTargets);

            bool hit = false;
            int delay = Math.Max(0, action.Delay);

            foreach (MapObject target in targets.ToList())
            {
                AddCustomDelayedAttack(action, target, delay);
                hit = true;
            }

            if (hit)
                UpdateAttackTime();

            return hit;
        }

        private void ExecuteMeleeAttack(CustomMonsterAiActionInfo action, MapObject target)
        {
            Direction = Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation);
            Broadcast(new S.ObjectAttack { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation, AttackElement = GetActionElement(action), TargetID = target.ObjectID, AnimationVariant = action.AnimationVariant });

            UpdateAttackTime();

            if (!GetActionOptionBool(action, "NoDamage", false))
                AddCustomDelayedAttack(action, target, Math.Max(0, action.Delay));
        }

        private void AddCustomDelayedAttack(CustomMonsterAiActionInfo action, MapObject target, int delay)
        {
            AddCustomDelayedAttack(action, target, delay, GetActionPower(action, target));
        }

        private void AddCustomDelayedAttack(CustomMonsterAiActionInfo action, MapObject target, int delay, int power)
        {
            ActionList.Add(new DelayedAction(
                SEnvir.Now.AddMilliseconds(Math.Max(0, delay)),
                ActionType.DelayAttack,
                target,
                Math.Max(1, power),
                GetActionElement(action),
                CustomDelayedAttackMarker,
                action,
                GetActionOptionBool(action, "DrainMP", false)));
        }

        private bool ExecuteRangeAttack(CustomMonsterAiActionInfo action)
        {
            MapObject primaryTarget = GetActionTarget(action, requireAttackTarget: true);
            if (!CanAttack || primaryTarget == null) return false;
            if (!ActionTargetWithinDistance(action, primaryTarget)) return false;

            Direction = Functions.DirectionFromPoint(CurrentLocation, primaryTarget.CurrentLocation);

            List<MapObject> targets = action.Radius > 0 || action.Target is CustomAiTargetSelectorType.SelfRadius or CustomAiTargetSelectorType.TargetRadius or CustomAiTargetSelectorType.Line or CustomAiTargetSelectorType.AllInView
                ? GetActionTargets(action, requireAttackTarget: true)
                : new List<MapObject> { primaryTarget };

            if (targets.Count == 0) return false;

            Broadcast(new S.ObjectRangeAttack
            {
                ObjectID = ObjectID,
                Direction = Direction,
                Location = CurrentLocation,
                Targets = targets.Select(x => x.ObjectID).ToList(),
                AttackElement = GetActionElement(action),
                AttackMagic = GetActionMagicType(action),
                MagicInfoIndex = action.MagicInfo?.Index ?? 0,
                AnimationVariant = action.AnimationVariant
            });

            UpdateAttackTime();

            if (GetActionOptionBool(action, "NoDamage", false))
                return true;

            foreach (MapObject ob in targets)
            {
                if (!CanAttackTarget(ob)) continue;

                int delay = Math.Max(0, action.Delay);
                if (action.Travel)
                    delay += Functions.Distance(CurrentLocation, ob.CurrentLocation) * Globals.ProjectileSpeed;

                AddCustomDelayedAttack(action, ob, delay);
            }

            return true;
        }

        private bool ExecuteLineAttack(CustomMonsterAiActionInfo action)
        {
            MapObject target = GetActionTarget(action, requireAttackTarget: true);
            if (!CanAttack || target == null || CurrentMap == null) return false;

            int distance = Math.Max(1, action.Distance > 0 ? action.Distance : Behavior?.AttackRange ?? 1);
            bool allDirections = GetActionOptionBool(action, "AllDirections", false);
            int minOffset = allDirections ? 0 : action.MinOffset;
            int maxOffset = allDirections ? 7 : action.MaxOffset;

            if (maxOffset < minOffset)
                (minOffset, maxOffset) = (maxOffset, minOffset);

            if (!allDirections && minOffset == 0 && maxOffset == 0 && !IsTargetInLine(target, distance))
                return false;

            Direction = Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation);

            int maxTargets = action.MaxTargets > 0 ? action.MaxTargets : int.MaxValue;
            int hitCount = 0;
            HashSet<MapObject> hitTargets = new();

            for (int offset = minOffset; offset <= maxOffset && hitCount < maxTargets; offset++)
            {
                MirDirection direction = allDirections
                    ? Functions.ShiftDirection(MirDirection.Up, offset)
                    : Functions.ShiftDirection(Direction, offset);

                ExecuteLineAttackDirection(action, direction, distance, hitTargets, ref hitCount, maxTargets);
            }

            if (hitCount == 0) return false;

            if (GetActionOptionBool(action, "RangePacket", false))
            {
                Broadcast(new S.ObjectRangeAttack
                {
                    ObjectID = ObjectID,
                    Direction = Direction,
                    Location = CurrentLocation,
                    Targets = hitTargets.Select(x => x.ObjectID).ToList(),
                    AttackElement = GetActionElement(action),
                    AttackMagic = GetActionMagicType(action),
                    MagicInfoIndex = action.MagicInfo?.Index ?? 0,
                    AnimationVariant = action.AnimationVariant,
                });
            }
            else
            {
                Broadcast(new S.ObjectAttack
                {
                    ObjectID = ObjectID,
                    Direction = Direction,
                    Location = CurrentLocation,
                    AttackElement = GetActionElement(action),
                    AttackMagic = GetActionMagicType(action),
                    TargetID = target.ObjectID,
                    AnimationVariant = action.AnimationVariant,
                });
            }

            UpdateAttackTime();
            return true;
        }

        private void ExecuteLineAttackDirection(
            CustomMonsterAiActionInfo action,
            MirDirection direction,
            int distance,
            HashSet<MapObject> hitTargets,
            ref int hitCount,
            int maxTargets)
        {
            for (int i = 1; i <= distance && hitCount < maxTargets; i++)
            {
                Cell cell = CurrentMap.GetCell(Functions.Move(CurrentLocation, direction, i));
                if (cell?.Objects == null) continue;

                foreach (MapObject ob in cell.Objects.ToArray())
                {
                    if (!CanAttackTarget(ob)) continue;
                    if (hitTargets.Contains(ob)) continue;
                    if (action.TargetChance < 100 && SEnvir.Random.Next(100) >= Math.Max(0, action.TargetChance)) continue;

                    hitTargets.Add(ob);

                    int delay = Math.Max(0, action.Delay);
                    if (action.Travel)
                        delay += i * Globals.ProjectileSpeed;

                    if (!GetActionOptionBool(action, "NoDamage", false))
                        AddCustomDelayedAttack(action, ob, delay);

                    hitCount++;
                    break;
                }
            }
        }

        private bool ExecuteLineAoE(CustomMonsterAiActionInfo action)
        {
            if (!CanAttack || CurrentMap == null) return false;

            MapObject target = GetExecutionTarget();
            bool allDirections = GetActionOptionBool(action, "AllDirections", false);
            if (target == null && !allDirections) return false;

            if (target != null)
                Direction = Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation);

            int distance = Math.Max(1, action.Distance);
            int minOffset = allDirections ? 0 : action.MinOffset;
            int maxOffset = allDirections ? 7 : action.MaxOffset;
            if (maxOffset < minOffset)
                (minOffset, maxOffset) = (maxOffset, minOffset);

            List<uint> targetIDs = new();
            List<Point> locations = new();
            HashSet<Point> locationSet = new();
            HashSet<MapObject> hitTargets = new();
            MagicType magic = GetActionMagicType(action);
            Element element = GetActionElement(action);

            for (int offset = minOffset; offset <= maxOffset; offset++)
            {
                MirDirection direction = allDirections
                    ? Functions.ShiftDirection(MirDirection.Up, offset)
                    : Functions.ShiftDirection(Direction, offset);

                AddLineAoEDirection(action, direction, distance, magic, element, targetIDs, locations, locationSet, hitTargets);
            }

            if (targetIDs.Count == 0 && locations.Count == 0) return false;

            Broadcast(new S.ObjectMagic
            {
                ObjectID = ObjectID,
                Direction = Direction,
                CurrentLocation = CurrentLocation,
                Cast = true,
                Type = magic,
                MagicInfoIndex = action.MagicInfo?.Index ?? 0,
                Targets = targetIDs,
                Locations = locations,
                AttackElement = element,
                AnimationVariant = action.AnimationVariant
            });

            UpdateAttackTime();
            return true;
        }

        private void AddLineAoEDirection(
            CustomMonsterAiActionInfo action,
            MirDirection direction,
            int distance,
            MagicType magic,
            Element element,
            List<uint> targetIDs,
            List<Point> locations,
            HashSet<Point> locationSet,
            HashSet<MapObject> hitTargets)
        {
            bool endpointOnly = magic is MagicType.LightningBeam or MagicType.BlowEarth or MagicType.ElementalHurricane;

            if (endpointOnly)
                AddLineAoELocation(Functions.Move(CurrentLocation, direction, distance), locations, locationSet);

            for (int i = 1; i <= distance; i++)
            {
                Point location = Functions.Move(CurrentLocation, direction, i);
                Cell cell = CurrentMap.GetCell(location);
                int delay = Math.Max(0, action.Delay) + 500 + i * 75;

                if (cell == null) continue;

                if (!endpointOnly)
                    AddLineAoELocation(cell.Location, locations, locationSet);

                AddLineAoECellTargets(action, cell, delay, element, targetIDs, hitTargets, damageDivisor: 1);

                switch (direction)
                {
                    case MirDirection.Up:
                    case MirDirection.Right:
                    case MirDirection.Down:
                    case MirDirection.Left:
                        AddLineAoECellTargets(action, CurrentMap.GetCell(Functions.Move(location, Functions.ShiftDirection(direction, -2))), delay, element, targetIDs, hitTargets, damageDivisor: 2);
                        AddLineAoECellTargets(action, CurrentMap.GetCell(Functions.Move(location, Functions.ShiftDirection(direction, 2))), delay, element, targetIDs, hitTargets, damageDivisor: 2);
                        break;
                    case MirDirection.UpRight:
                    case MirDirection.DownRight:
                    case MirDirection.DownLeft:
                    case MirDirection.UpLeft:
                        AddLineAoECellTargets(action, CurrentMap.GetCell(Functions.Move(location, Functions.ShiftDirection(direction, -1))), delay, element, targetIDs, hitTargets, damageDivisor: 2);
                        AddLineAoECellTargets(action, CurrentMap.GetCell(Functions.Move(location, Functions.ShiftDirection(direction, 1))), delay, element, targetIDs, hitTargets, damageDivisor: 2);
                        break;
                }
            }
        }

        private static void AddLineAoELocation(Point location, List<Point> locations, HashSet<Point> locationSet)
        {
            if (locationSet.Add(location))
                locations.Add(location);
        }

        private void AddLineAoECellTargets(
            CustomMonsterAiActionInfo action,
            Cell cell,
            int delay,
            Element element,
            List<uint> targetIDs,
            HashSet<MapObject> hitTargets,
            int damageDivisor)
        {
            if (cell?.Objects == null) return;

            foreach (MapObject ob in cell.Objects.ToArray())
            {
                if (!CanAttackTarget(ob)) continue;
                if (!hitTargets.Add(ob)) continue;

                targetIDs.Add(ob.ObjectID);

                if (GetActionOptionBool(action, "NoDamage", false)) continue;

                int power = GetActionPower(action, ob);
                if (damageDivisor > 1)
                    power = Math.Max(1, power / damageDivisor);

                AddCustomDelayedAttack(action, ob, delay, power);
            }
        }

        private bool ExecuteAttackMagic(CustomMonsterAiActionInfo action)
        {
            MapObject target = GetActionTarget(action, requireAttackTarget: true);
            if (!CanAttack || target == null) return false;
            if (!ActionTargetWithinDistance(action, target)) return false;

            Direction = Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation);

            Broadcast(new S.ObjectMagic
            {
                ObjectID = ObjectID,
                Direction = Direction,
                CurrentLocation = CurrentLocation,
                Cast = true,
                Type = GetActionMagicType(action),
                MagicInfoIndex = action.MagicInfo?.Index ?? 0,
                Targets = new List<uint> { target.ObjectID },
                AttackElement = GetActionElement(action),
                AnimationVariant = action.AnimationVariant
            });

            UpdateAttackTime();

            int delay = Math.Max(0, action.Delay);
            if (action.Travel)
                delay += Functions.Distance(CurrentLocation, target.CurrentLocation) * 48;

            if (!GetActionOptionBool(action, "NoDamage", false))
                AddCustomDelayedAttack(action, target, delay);

            return true;
        }

        private bool ExecuteAttackAoE(CustomMonsterAiActionInfo action)
        {
            Point? location = GetActionLocation(action);
            if (!CanAttack || location == null) return false;
            if (action.Distance > 0 && !Functions.InRange(CurrentLocation, location.Value, action.Distance)) return false;

            MapObject faceTarget = GetExecutionTarget();
            if (faceTarget != null)
                Direction = Functions.DirectionFromPoint(CurrentLocation, faceTarget.CurrentLocation);

            List<MapObject> targets = GetActionTargets(action, requireAttackTarget: true);
            if (targets.Count == 0 && action.Target != CustomAiTargetSelectorType.CurrentTargetLocation) return false;

            Broadcast(new S.ObjectMagic
            {
                ObjectID = ObjectID,
                Direction = Direction,
                CurrentLocation = CurrentLocation,
                Cast = true,
                Type = GetActionMagicType(action),
                MagicInfoIndex = action.MagicInfo?.Index ?? 0,
                Locations = new List<Point> { location.Value },
                AttackElement = GetActionElement(action),
                AnimationVariant = action.AnimationVariant
            });

            UpdateAttackTime();

            if (!GetActionOptionBool(action, "NoDamage", false))
                foreach (MapObject ob in targets)
                {
                    AddCustomDelayedAttack(action, ob, Math.Max(0, action.Delay));
                }

            return true;
        }

        private bool ExecuteMassAreaDamage(CustomMonsterAiActionInfo action)
        {
            if (CurrentMap == null) return false;
            bool allowDead = GetActionOptionBool(action, "AllowDead", false);
            bool ignoreAttackCooldown = GetActionOptionBool(action, "IgnoreAttackCooldown", false);
            if (Dead && !allowDead) return false;
            if (!Dead && !CanAttack && !ignoreAttackCooldown) return false;

            MapObject faceTarget = GetExecutionTarget();
            Direction = faceTarget == null ? Direction : Functions.DirectionFromPoint(CurrentLocation, faceTarget.CurrentLocation);

            List<uint> targetIDs = new();
            List<Point> locations = new();

            if (action.Target == CustomAiTargetSelectorType.Line)
            {
                foreach (MapObject ob in GetActionTargets(action, requireAttackTarget: true))
                {
                    if (action.TargetChance < 100 && SEnvir.Random.Next(100) >= Math.Max(0, action.TargetChance)) continue;

                    targetIDs.Add(ob.ObjectID);

                    if (!GetActionOptionBool(action, "NoDamage", false))
                        AddCustomDelayedAttack(action, ob, Math.Max(0, action.Delay));
                }

                if (targetIDs.Count == 0)
                    return false;

                Broadcast(new S.ObjectMagic
                {
                    ObjectID = ObjectID,
                    Direction = Direction,
                    CurrentLocation = CurrentLocation,
                    Cast = true,
                    Type = GetActionMagicType(action),
                    MagicInfoIndex = action.MagicInfo?.Index ?? 0,
                    Targets = targetIDs,
                    AttackElement = GetActionElement(action),
                    AnimationVariant = action.AnimationVariant
                });

                if (!Dead)
                    UpdateAttackTime();

                return true;
            }

            int radius = action.Radius > 0 ? action.Radius : Config.MaxViewRange;
            Point origin = GetActionLocation(action) ?? CurrentLocation;
            int remainingTargets = action.RandomTargetCount > 0 ? action.RandomTargetCount : action.MaxTargets > 0 ? action.MaxTargets : int.MaxValue;

            foreach (Cell cell in CurrentMap.GetCells(origin, 0, radius))
            {
                if (cell.Objects == null)
                {
                    if (action.EmptyLocationChance > 0 && SEnvir.Random.Next(100) < action.EmptyLocationChance)
                        locations.Add(cell.Location);

                    continue;
                }

                foreach (MapObject ob in cell.Objects.ToArray())
                {
                    if (!CanAttackTarget(ob)) continue;
                    if (action.TargetChance < 100 && SEnvir.Random.Next(100) >= Math.Max(0, action.TargetChance)) continue;
                    if (remainingTargets <= 0) break;

                    targetIDs.Add(ob.ObjectID);

                    if (!GetActionOptionBool(action, "NoDamage", false))
                        AddCustomDelayedAttack(action, ob, Math.Max(0, action.Delay));

                    if (remainingTargets != int.MaxValue)
                        remainingTargets--;
                }
            }

            if (targetIDs.Count == 0 && locations.Count == 0)
                return false;

            Broadcast(new S.ObjectMagic
            {
                ObjectID = ObjectID,
                Direction = Direction,
                CurrentLocation = CurrentLocation,
                Cast = true,
                Type = GetActionMagicType(action),
                MagicInfoIndex = action.MagicInfo?.Index ?? 0,
                Targets = targetIDs,
                Locations = locations,
                AttackElement = GetActionElement(action),
                AnimationVariant = action.AnimationVariant
            });

            if (!Dead)
                UpdateAttackTime();
            return true;
        }

        private bool ExecuteSpawnMinions(CustomMonsterAiActionInfo action)
        {
            CullMinionsForSpawnAction(action);

            bool ignoreMaxMinions = GetActionOptionBool(action, "IgnoreMaxMinions", false);
            if (!ignoreMaxMinions && MaxMinions <= MinionList.Count) return false;

            bool spawned = false;
            MapObject inheritedTarget = GetExecutionTarget();
            bool perTarget = action.Target is CustomAiTargetSelectorType.ExperienceOwnerGroup or CustomAiTargetSelectorType.SummonerGroup ||
                             GetActionOptionBool(action, "PerTarget", false);
            List<Point> origins = perTarget
                ? GetActionTargets(action, requireAttackTarget: false).Select(x => x.CurrentLocation).Distinct().ToList()
                : new List<Point> { ApplyActionOriginOffset(action, GetActionLocation(action) ?? CurrentLocation) };

            if (origins.Count == 0)
                origins.Add(CurrentLocation);

            int totalLimit = GetActionOptionInt(action, "MaxTotal", action.MaxTargets > 0 ? action.MaxTargets : int.MaxValue);
            int totalSpawned = 0;

            foreach (Point origin in origins)
            {
                int count = SEnvir.Random.Next(Math.Max(0, action.RandomCount) + 1) + Math.Max(0, action.FixedCount);
                if (!ignoreMaxMinions)
                    count = Math.Min(count, MaxMinions - MinionList.Count);
                count = Math.Min(count, totalLimit - totalSpawned);
                if (count <= 0) break;

                for (int i = 0; i < count; i++)
                {
                    MonsterInfo info = action.SpawnMonster ?? GetSpawnMonsterFromActionFlag(action) ?? GetSpawnMonsterFromActionPool(action) ?? SEnvir.GetMonsterInfo(SpawnList);
                    if (info == null) break;

                    MonsterObject mob = GetMonster(info);
                    if (mob == null) continue;

                    if (!SpawnMinionNear(mob, origin, 0, Math.Max(1, action.Radius == 0 ? 6 : action.Radius))) continue;

                    mob.Target = inheritedTarget;
                    mob.Master = this;
                    MinionList.Add(mob);
                    RegisterCustomMinionLifetime(mob, action.MinionDuration);
                    totalSpawned++;
                    spawned = true;
                }
            }

            return spawned;
        }

        private void CullMinionsForSpawnAction(CustomMonsterAiActionInfo action)
        {
            int leashRadius = GetActionOptionInt(action, "MinionLeashRadius", GetActionOptionInt(action, "LeashRadius", 0));
            bool cullMissing = GetActionOptionBool(action, "CullMissingMinions", true);
            bool preserveBoss = GetActionOptionBool(action, "PreserveBossMinions", GetActionOptionBool(action, "PreserveBoss", false));
            if (leashRadius <= 0 && !cullMissing) return;

            for (int i = MinionList.Count - 1; i >= 0; i--)
            {
                MonsterObject minion = MinionList[i];
                if (preserveBoss && minion?.MonsterInfo?.IsBoss == true) continue;

                bool remove = minion == null || cullMissing && (minion.Node == null || minion.Dead);

                if (!remove && leashRadius > 0)
                    remove = minion.CurrentMap != CurrentMap || !Functions.InRange(CurrentLocation, minion.CurrentLocation, leashRadius);

                if (!remove) continue;

                MinionList.RemoveAt(i);

                if (minion == null) continue;

                _customMinionExpiry.Remove(minion);
                minion.EXPOwner = null;
                if (minion.Master == this)
                    minion.Master = null;

                if (!minion.Dead)
                    minion.Die();

                minion.Despawn();
            }
        }

        private static MonsterInfo GetSpawnMonsterFromActionFlag(CustomMonsterAiActionInfo action)
        {
            MonsterFlag flag = GetActionMonsterFlag(action);
            return flag == MonsterFlag.None ? null : SEnvir.MonsterInfoList.Binding.FirstOrDefault(x => x.Flag == flag);
        }

        private static MonsterInfo GetSpawnMonsterFromActionPool(CustomMonsterAiActionInfo action)
        {
            string value = GetActionOptionValue(action, "MonsterFlags");
            if (string.IsNullOrWhiteSpace(value))
                value = GetActionOptionValue(action, "SpawnPool");
            if (string.IsNullOrWhiteSpace(value))
                return null;

            List<(MonsterInfo Info, int Weight)> pool = [];
            foreach (string token in value.Split(['|', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = token.Split(':', 2, StringSplitOptions.TrimEntries);
                if (!Enum.TryParse(parts[0], true, out MonsterFlag flag) || flag == MonsterFlag.None) continue;

                MonsterInfo info = SEnvir.MonsterInfoList.Binding.FirstOrDefault(x => x.Flag == flag);
                if (info == null) continue;

                int weight = parts.Length == 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 1;
                if (weight <= 0) continue;

                pool.Add((info, weight));
            }

            if (pool.Count == 0) return null;

            int total = pool.Sum(x => x.Weight);
            int roll = SEnvir.Random.Next(total);
            foreach ((MonsterInfo info, int weight) in pool)
            {
                if ((roll -= weight) < 0)
                    return info;
            }

            return pool[^1].Info;
        }

        private bool ExecuteTeleportSelf(CustomMonsterAiActionInfo action)
        {
            if (CurrentMap == null) return false;

            Point origin = GetActionLocation(action) ?? CurrentLocation;

            int minRange = Math.Max(0, action.MinOffset);
            int maxRange = Math.Max(minRange, action.Radius > 0 ? action.Radius : action.Distance > 0 ? action.Distance : 5);

            return TeleportObjectNear(this, CurrentMap, origin, minRange, maxRange);
        }

        private bool ExecuteTeleportNearTarget(CustomMonsterAiActionInfo action)
        {
            MapObject target = GetActionTarget(action, requireAttackTarget: true);
            if (target == null || target == this || target.CurrentMap != CurrentMap || CurrentMap == null) return false;

            int minDistance = Math.Max(1, action.MinOffset > 0 ? action.MinOffset : 1);
            int maxDistance = Math.Max(minDistance, action.Radius > 0 ? action.Radius : action.Distance > 0 ? action.Distance : minDistance);
            MirDirection preferred = Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation);

            for (int distance = minDistance; distance <= maxDistance; distance++)
            {
                if (TryTeleportNearTarget(target, preferred, distance, clockwise: true)) return true;
                if (TryTeleportNearTarget(target, preferred, distance, clockwise: false)) return true;
            }

            return false;
        }

        private bool TryTeleportNearTarget(MapObject target, MirDirection preferred, int distance, bool clockwise)
        {
            int rotation = clockwise ? 1 : -1;
            MirDirection direction = preferred;

            for (int i = 0; i < 8; i++)
            {
                Point location = Functions.Move(target.CurrentLocation, direction, distance);
                Cell cell = CurrentMap.GetCell(location);

                if (cell != null && cell.Movements == null && !cell.IsBlocking(this, false))
                {
                    Direction = Functions.DirectionFromPoint(location, target.CurrentLocation);
                    return Teleport(CurrentMap, location);
                }

                direction = Functions.ShiftDirection(direction, rotation);
            }

            return false;
        }

        private bool ExecuteWeightedChoice(CustomMonsterAiActionInfo action, Dictionary<string, CustomMonsterAiActionInfo> actions, int depth)
        {
            List<(string Name, int Weight)> options = ParseWeightedOptions(action.Options);
            if (options.Count == 0) return false;

            int total = options.Sum(x => x.Weight);
            int roll = SEnvir.Random.Next(total);

            foreach ((string name, int weight) in options)
            {
                if (roll >= weight)
                {
                    roll -= weight;
                    continue;
                }

                return actions.TryGetValue(name, out CustomMonsterAiActionInfo selected) && ExecuteAiAction(selected, actions, depth + 1);
            }

            return false;
        }

        private bool ExecuteSequence(CustomMonsterAiActionInfo action, Dictionary<string, CustomMonsterAiActionInfo> actions, int depth)
        {
            bool executed = false;
            bool requireAll = GetActionOptionBool(action, "RequireAll", false);

            foreach ((string name, _) in ParseWeightedOptions(action.Options))
            {
                bool current = actions.TryGetValue(name, out CustomMonsterAiActionInfo selected) && ExecuteAiAction(selected, actions, depth + 1);

                if (requireAll && !current)
                    return false;

                executed |= current;
            }

            return executed;
        }

        private bool ExecuteSearchTarget(CustomMonsterAiActionInfo action)
        {
            if (CurrentMap == null) return false;

            int radius = action.Radius > 0 ? action.Radius : ViewRange;
            bool searchAllies = ActionSearchesAllies(action);
            List<MapObject> targets = UsesTargetCollection(action.Target)
                ? GetActionTargets(action, requireAttackTarget: !searchAllies)
                    .Where(x => searchAllies ? CanHelpTarget(x) : ShouldAttackTarget(x))
                    .ToList()
                : searchAllies
                    ? GetAllObjects(CurrentLocation, radius)
                    .Where(x => x != null && x != this && x.Node != null && !x.Dead && x.Visible && x.CurrentMap == CurrentMap && CanHelpTarget(x))
                    .OrderBy(x => Functions.Distance(CurrentLocation, x.CurrentLocation))
                    .ThenBy(_ => SEnvir.Random.Next())
                    .ToList()
                    : GetTargets(CurrentMap, CurrentLocation, radius)
                    .Where(ShouldAttackTarget)
                    .OrderBy(x => Functions.Distance(CurrentLocation, x.CurrentLocation))
                    .ThenBy(_ => SEnvir.Random.Next())
                    .ToList();

            MapObject target = targets.FirstOrDefault();
            if (target == null) return false;

            Target = target;
            return true;
        }

        private static bool ActionSearchesAllies(CustomMonsterAiActionInfo action)
        {
            if (action == null) return false;

            return action.Target is CustomAiTargetSelectorType.AllyRadius
                    or CustomAiTargetSelectorType.RandomAllyInView
                    or CustomAiTargetSelectorType.NearestAllyInView
                    or CustomAiTargetSelectorType.LowestHealthAllyInView
                   || GetActionOptionBool(action, "SearchAllies", false)
                   || GetActionOptionBool(action, "SearchCanHelp", false);
        }

        private bool ExecuteSetCounter(CustomMonsterAiActionInfo action, bool additive)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.CounterName)) return false;

            string name = action.CounterName.Trim();
            int value = Math.Max(0, action.FixedCount);
            _aiCounters[name] = additive ? GetAiCounter(name) + value : value;
            return true;
        }

        private bool ExecuteVisibility(bool visible, CustomMonsterAiActionInfo action = null)
        {
            bool wasVisible = Visible;
            bool packetOnly = GetActionOptionBool(action, "PacketOnly", false);
            bool stateOnly = GetActionOptionBool(action, "StateOnly", false);
            bool forcePacket = GetActionOptionBool(action, "ForcePacket", false);

            if (!packetOnly)
                Visible = visible;

            if (!stateOnly && (forcePacket || packetOnly || wasVisible != visible))
            {
                if (visible)
                    Broadcast(new S.ObjectShow { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation });
                else
                    Broadcast(new S.ObjectHide { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation });
            }

            return forcePacket || packetOnly || wasVisible != visible;
        }

        private bool ExecuteDelayTime(CustomMonsterAiActionInfo action, bool delayAction, bool delayMove, bool delayCell)
        {
            int delay = Math.Max(0, action.Delay);
            if (delay == 0)
                delay = Math.Max(0, action.Duration);

            if (delay <= 0) return false;

            bool alsoAction = delayAction || GetActionOptionBool(action, "ActionTime", false);
            bool alsoMove = delayMove || GetActionOptionBool(action, "MoveTime", false);
            bool alsoCell = delayCell || GetActionOptionBool(action, "CellTime", false);
            bool extendOnly = GetActionOptionBool(action, "ExtendOnly", true);
            DateTime time = SEnvir.Now.AddMilliseconds(delay);

            if (alsoAction)
                ActionTime = extendOnly && ActionTime > time ? ActionTime : time;

            if (alsoMove)
                MoveTime = extendOnly && MoveTime > time ? MoveTime : time;

            if (alsoCell)
                CellTime = extendOnly && CellTime > time ? CellTime : time;

            return alsoAction || alsoMove || alsoCell;
        }

        private bool ExecuteScheduleAction(CustomMonsterAiActionInfo action, Dictionary<string, CustomMonsterAiActionInfo> actions, int depth)
        {
            if (action == null || actions == null || depth > 8) return false;

            List<(string Name, int Weight)> options = ParseWeightedOptions(action.Options);
            if (options.Count == 0) return false;

            int delay = Math.Max(0, action.Delay);
            MapObject contextTarget = GetExecutionTarget();
            bool scheduled = false;

            foreach ((string name, _) in options)
            {
                if (!actions.ContainsKey(name)) continue;

                ActionList.Add(new DelayedAction(
                    SEnvir.Now.AddMilliseconds(delay),
                    ActionType.Function,
                    ScheduledAiActionMarker,
                    name,
                    depth + 1,
                    contextTarget));

                scheduled = true;
            }

            return scheduled;
        }

        private bool ExecuteScheduledAiAction(string actionName, int depth, MapObject contextTarget)
        {
            return ExecuteNamedAiAction(actionName, depth, contextTarget);
        }

        private void ExecuteOnHitAction(CustomMonsterAiActionInfo sourceAction, MapObject hitTarget)
        {
            string actionName = GetActionOptionValue(sourceAction, "OnHitAction");
            if (string.IsNullOrWhiteSpace(actionName) || hitTarget?.Node == null || hitTarget.Dead || hitTarget.CurrentMap != CurrentMap)
                return;

            int chance = Math.Max(0, Math.Min(100, GetActionOptionInt(sourceAction, "OnHitChance", 100)));
            if (chance <= 0 || chance < 100 && SEnvir.Random.Next(100) >= chance)
                return;

            ExecuteNamedAiAction(actionName, 0, hitTarget);
        }

        private bool ExecuteNamedAiAction(string actionName, int depth, MapObject contextTarget)
        {
            if (string.IsNullOrWhiteSpace(actionName) || depth > 8) return false;

            CustomMonsterBehaviorInfo behavior = Behavior;
            if (behavior?.Actions == null) return false;

            Dictionary<string, CustomMonsterAiActionInfo> actions = behavior.Actions
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            if (!actions.TryGetValue(actionName, out CustomMonsterAiActionInfo selected))
                return false;

            MapObject oldContextTarget = _ruleContextTarget;
            if (contextTarget != null)
                _ruleContextTarget = contextTarget;

            try
            {
                return ExecuteAiAction(selected, actions, depth + 1);
            }
            finally
            {
                _ruleContextTarget = oldContextTarget;
            }
        }

        private bool ExecuteMapEffect(CustomMonsterAiActionInfo action)
        {
            if (CurrentMap == null) return false;

            string effectName = GetActionOptionValue(action, "Effect");
            if (string.IsNullOrWhiteSpace(effectName) || !Enum.TryParse(effectName, true, out Effect effect))
                return false;

            Point location = GetActionLocation(action) ?? CurrentLocation;
            CurrentMap.Broadcast(location, new S.MapEffect { Location = location, Effect = effect, Direction = Direction });
            return true;
        }

        private bool ExecuteObjectEffect(CustomMonsterAiActionInfo action)
        {
            string effectName = GetActionOptionValue(action, "Effect");
            if (string.IsNullOrWhiteSpace(effectName) || !Enum.TryParse(effectName, true, out Effect effect))
                return false;

            Broadcast(new S.ObjectEffect { ObjectID = ObjectID, Effect = effect });
            return true;
        }

        private bool ExecuteStoreLocation(CustomMonsterAiActionInfo action)
        {
            if (CurrentMap == null) return false;

            _storedAiLocations[GetStoredLocationKey(action)] = new StoredAiLocation(CurrentMap, CurrentLocation, Direction);
            return true;
        }

        private bool ExecuteReturnToStoredLocation(CustomMonsterAiActionInfo action)
        {
            string key = GetStoredLocationKey(action);
            if (!_storedAiLocations.TryGetValue(key, out StoredAiLocation stored) || stored.Map == null)
                return false;

            bool restoreDirection = GetActionOptionBool(action, "RestoreDirection", true);
            bool leaveEffect = GetActionOptionBool(action, "LeaveEffect", true);
            bool enterEffect = GetActionOptionBool(action, "EnterEffect", true);
            bool clear = GetActionOptionBool(action, "Clear", true);

            if (restoreDirection)
                Direction = stored.Direction;

            bool teleported = Teleport(stored.Map, stored.Location, leaveEffect, enterEffect);
            if (teleported && clear)
                _storedAiLocations.Remove(key);

            return teleported;
        }

        private static string GetStoredLocationKey(CustomMonsterAiActionInfo action)
        {
            string key = GetActionOptionValue(action, "Key");
            if (string.IsNullOrWhiteSpace(key))
                key = GetActionOptionValue(action, "LocationKey");
            if (string.IsNullOrWhiteSpace(key))
                key = action?.CounterName;

            return string.IsNullOrWhiteSpace(key) ? "Default" : key.Trim();
        }

        private bool ExecuteReviveSelf(CustomMonsterAiActionInfo action)
        {
            if (!Dead) return false;

            if (!_reviveInitialized)
            {
                _revivesRemaining = Math.Max(0, action.FixedCount) + (action.RandomCount > 0 ? SEnvir.Random.Next(action.RandomCount + 1) : 0);
                _reviveInitialized = true;
            }

            if (_revivesRemaining <= 0) return false;

            _reviveHpPercent = Math.Max(1, action.PowerRate <= 0 ? 100 : action.PowerRate);
            _reviveHealthDecay = GetActionOptionBool(action, "HealthDecay", GetActionOptionBool(action, "ReviveHealthDecay", false));
            _suppressDropsWhileReviving = GetActionOptionBool(action, "SuppressDrops", true);
            _reviveTime = SEnvir.Now.AddMilliseconds(Math.Max(0, action.Delay));
            DeadTime = _reviveTime.Add(Config.DeadDuration);
            return true;
        }

        private void ReviveFromRule()
        {
            _reviveTime = DateTime.MinValue;
            _revivesRemaining = Math.Max(0, _revivesRemaining - 1);
            _reviveDeathCount++;

            Broadcast(new S.ObjectShow { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation });

            ActionTime = SEnvir.Now.AddMilliseconds(1500);
            Dead = false;

            int hpPercent = _reviveHpPercent;
            if (_reviveHealthDecay)
                hpPercent = Math.Max(1, hpPercent / (1 << Math.Min(_reviveDeathCount, 10)));

            SetHP(Math.Max(1, Stats[Stat.Health] * hpPercent / 100));
        }

        private bool ExecuteWakeNearby(CustomMonsterAiActionInfo action)
        {
            bool woke = ExecuteVisibility(true);
            int radius = action.Radius > 0 ? action.Radius : Config.MaxViewRange;

            foreach (MapObject ob in GetAllObjects(CurrentLocation, radius))
            {
                if (ob == this || ob is not CustomMonsterObject custom || custom.Dead || custom.Visible) continue;

                custom.Target = GetExecutionTarget();
                custom.ExecuteVisibility(true);
                woke = true;
            }

            return woke;
        }

        private bool ExecuteApplyPoison(CustomMonsterAiActionInfo action)
        {
            if (action.PoisonType == PoisonType.None) return false;

            List<MapObject> targets = GetActionTargets(action, requireAttackTarget: action.Target != CustomAiTargetSelectorType.Self);
            if (targets.Count == 0) return false;

            TimeSpan tickRate = TimeSpan.FromMilliseconds(Math.Max(100, action.TickRate));
            int duration = Math.Max((int)tickRate.TotalMilliseconds, action.Duration);
            int tickCount = Math.Max(1, duration / (int)tickRate.TotalMilliseconds);
            bool applied = false;

            foreach (MapObject target in targets)
            {
                if (target == null || target.Dead) continue;

                applied |= target.ApplyPoison(new Poison
                {
                    Owner = this,
                    Type = action.PoisonType,
                    Value = action.FixedPower > 0 ? action.FixedPower : GetActionPower(action, target),
                    TickFrequency = tickRate,
                    TickCount = tickCount,
                    TickTime = SEnvir.Now + tickRate,
                    CanKill = action.PoisonCanKill,
                    Stackable = GetActionOptionBool(action, "Stackable", false),
                });
            }

            return applied;
        }

        private bool ExecuteAddBuff(CustomMonsterAiActionInfo action)
        {
            if (action.BuffType == BuffType.None) return false;

            List<MapObject> targets = GetActionTargets(action, requireAttackTarget: false);
            if (targets.Count == 0) return false;

            TimeSpan duration = TimeSpan.FromMilliseconds(Math.Max(1000, action.Duration));
            TimeSpan tickRate = TimeSpan.FromMilliseconds(Math.Max(0, action.TickRate));

            foreach (MapObject target in targets)
                target.BuffAdd(
                    action.BuffType,
                    duration,
                    BuildActionBuffStats(action, target),
                    action.BuffVisible,
                    action.BuffPause,
                    tickRate,
                    action.BuffHidden,
                    action.BuffExtra != 0 ? action.BuffExtra : Math.Max(0, action.FixedPower));

            return true;
        }

        private bool ExecuteRemoveBuff(CustomMonsterAiActionInfo action)
        {
            if (action.BuffType == BuffType.None) return false;

            List<MapObject> targets = GetActionTargets(action, requireAttackTarget: false);
            if (targets.Count == 0) return false;

            foreach (MapObject target in targets)
                target.BuffRemove(action.BuffType);

            return true;
        }

        private bool ExecuteTeleportTarget(CustomMonsterAiActionInfo action)
        {
            MapObject target = GetActionTarget(action, requireAttackTarget: true);
            if (target == null || target == this || CurrentMap == null) return false;

            Point origin = action.Target == CustomAiTargetSelectorType.TargetRadius
                ? target.CurrentLocation
                : CurrentLocation;
            origin = ApplyActionOriginOffset(action, origin);

            return TeleportObjectNear(target, CurrentMap, origin, 0, Math.Max(1, action.Radius == 0 ? 5 : action.Radius));
        }

        private bool ExecuteTeleportTargetsRandom(CustomMonsterAiActionInfo action)
        {
            if (CurrentMap == null) return false;

            IEnumerable<MapObject> candidates = GetActionOptionBool(action, "AllMap", false)
                ? CurrentMap.Objects.ToArray().Where(CanAttackTarget)
                : GetActionTargets(action, requireAttackTarget: true);

            if (action.TargetChance < 100)
                candidates = candidates.Where(_ => SEnvir.Random.Next(100) < Math.Max(0, action.TargetChance));

            if (action.RandomTargetCount > 0)
                candidates = candidates.OrderBy(_ => SEnvir.Random.Next()).Take(action.RandomTargetCount);
            else if (action.MaxTargets > 0)
                candidates = candidates.Take(action.MaxTargets);

            int attempts = Math.Max(1, GetActionOptionInt(action, "Attempts", 25));
            bool teleported = false;

            foreach (MapObject target in candidates.ToList())
            {
                if (target == null || target == this || target.CurrentMap != CurrentMap) continue;

                if (action.Radius > 0)
                {
                    Point origin = GetActionLocation(action, target) ?? CurrentLocation;
                    teleported |= TeleportObjectNear(target, CurrentMap, origin, 0, action.Radius);
                }
                else
                {
                    teleported |= TeleportObjectRandom(target, CurrentMap, attempts);
                }
            }

            return teleported;
        }

        private bool ExecutePurifyTargets(CustomMonsterAiActionInfo action)
        {
            if (CurrentMap == null) return false;

            IEnumerable<MapObject> candidates = GetActionOptionBool(action, "AllMap", false)
                ? CurrentMap.Objects.ToArray().Where(x => x != null && x != this && !x.Dead)
                : GetActionTargets(action, requireAttackTarget: false);

            if (action.TargetChance < 100)
                candidates = candidates.Where(_ => SEnvir.Random.Next(100) < Math.Max(0, action.TargetChance));

            if (action.RandomTargetCount > 0)
                candidates = candidates.OrderBy(_ => SEnvir.Random.Next()).Take(action.RandomTargetCount);
            else if (action.MaxTargets > 0)
                candidates = candidates.Take(action.MaxTargets);

            List<MapObject> targets = candidates
                .Where(x => x != null && x != this && x.CurrentMap == CurrentMap && !x.Dead)
                .Distinct()
                .ToList();

            if (targets.Count == 0) return false;

            MapObject faceTarget = GetExecutionTarget() ?? targets.FirstOrDefault();
            if (faceTarget != null && faceTarget != this)
                Direction = Functions.DirectionFromPoint(CurrentLocation, faceTarget.CurrentLocation);

            Broadcast(new S.ObjectMagic
            {
                ObjectID = ObjectID,
                Direction = Direction,
                CurrentLocation = CurrentLocation,
                Cast = true,
                Type = MagicType.Purification,
                Targets = targets.Select(x => x.ObjectID).ToList(),
                AnimationVariant = action.AnimationVariant
            });

            UpdateAttackTime();

            int delay = Math.Max(0, action.Delay);
            foreach (MapObject target in targets)
            {
                ActionList.Add(new DelayedAction(
                    SEnvir.Now.AddMilliseconds(delay),
                    ActionType.DelayMagic,
                    MagicType.Purification,
                    target));
            }

            return true;
        }

        private bool ExecuteRecallMonstersByFlag(CustomMonsterAiActionInfo action)
        {
            if (CurrentMap == null) return false;

            MonsterFlag flag = GetActionMonsterFlag(action);
            Point origin = GetActionLocation(action) ?? CurrentLocation;
            origin = ApplyActionOriginOffset(action, origin);

            IEnumerable<MonsterObject> monsters = CurrentMap.Objects
                .OfType<MonsterObject>()
                .Where(x => x != null && x != this && !x.Dead && x.CurrentMap == CurrentMap);

            if (action.SpawnMonster != null)
                monsters = monsters.Where(x => x.MonsterInfo == action.SpawnMonster);
            else if (flag != MonsterFlag.None)
                monsters = monsters.Where(x => x.MonsterInfo?.Flag == flag);
            else
                return false;

            int leashRadius = action.Distance > 0 ? action.Distance : GetActionOptionInt(action, "RecallOutsideRadius", 0);
            if (leashRadius > 0)
                monsters = monsters.Where(x => !Functions.InRange(x.CurrentLocation, origin, leashRadius));

            if (action.TargetChance < 100)
                monsters = monsters.Where(_ => SEnvir.Random.Next(100) < Math.Max(0, action.TargetChance));

            if (action.RandomTargetCount > 0)
                monsters = monsters.OrderBy(_ => SEnvir.Random.Next()).Take(action.RandomTargetCount);
            else if (action.MaxTargets > 0)
                monsters = monsters.Take(action.MaxTargets);

            int radius = Math.Max(1, action.Radius == 0 ? 1 : action.Radius);
            bool recalled = false;

            foreach (MonsterObject monster in monsters.ToList())
                recalled |= TeleportObjectNear(monster, CurrentMap, origin, 0, radius);

            return recalled;
        }

        private bool ExecuteSwapRandomMinionLocations(CustomMonsterAiActionInfo action)
        {
            if (CurrentMap == null) return false;

            bool includeSelf = GetActionOptionBool(action, "IncludeSelf", true);
            bool leaveEffect = GetActionOptionBool(action, "LeaveEffect", true);
            bool enterEffect = GetActionOptionBool(action, "EnterEffect", leaveEffect);
            int attempts = Math.Max(1, GetActionOptionInt(action, "Attempts", 10));
            int repeat = Math.Max(1, GetActionOptionInt(action, "Repeat", 1));
            bool swapped = false;

            for (int i = 0; i < repeat; i++)
            {
                List<MapObject> candidates = GetTrackedMinions(includeDead: false)
                    .Where(x => x.CurrentMap == CurrentMap && x.Node != null && !x.Dead)
                    .Cast<MapObject>()
                    .ToList();

                if (includeSelf && Node != null && !Dead)
                    candidates.Add(this);

                candidates = candidates.Distinct().ToList();
                if (candidates.Count < 2) return swapped;

                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    MapObject first = candidates[SEnvir.Random.Next(candidates.Count)];
                    MapObject second;
                    do
                    {
                        second = candidates[SEnvir.Random.Next(candidates.Count)];
                    } while (ReferenceEquals(first, second));

                    if (!SwapObjectLocations(first, second, leaveEffect, enterEffect)) continue;

                    swapped = true;
                    break;
                }
            }

            return swapped;
        }

        private static bool SwapObjectLocations(MapObject first, MapObject second, bool leaveEffect, bool enterEffect)
        {
            if (first?.Node == null || second?.Node == null || first.Dead || second.Dead) return false;
            if (first.CurrentMap == null || first.CurrentMap != second.CurrentMap) return false;

            Map map = first.CurrentMap;
            Point firstPoint = first.CurrentLocation;
            Point secondPoint = second.CurrentLocation;

            if (!first.Teleport(map, secondPoint, leaveEffect, enterEffect)) return false;
            if (second.Teleport(map, firstPoint, leaveEffect, enterEffect)) return true;

            first.Teleport(map, firstPoint, false, false);
            return false;
        }

        private bool ExecuteKillMinions(CustomMonsterAiActionInfo action)
        {
            List<MonsterObject> minions = GetTrackedMinions(includeDead: false)
                .Where(x => x != null && x != this)
                .ToList();

            if (minions.Count == 0) return false;

            if (action.TargetChance < 100)
                minions = minions.Where(_ => SEnvir.Random.Next(100) < Math.Max(0, action.TargetChance)).ToList();

            if (action.RandomTargetCount > 0)
                minions = minions.OrderBy(_ => SEnvir.Random.Next()).Take(action.RandomTargetCount).ToList();
            else if (action.MaxTargets > 0)
                minions = minions.Take(action.MaxTargets).ToList();

            bool despawn = GetActionOptionBool(action, "Despawn", false);
            bool clear = GetActionOptionBool(action, "Clear", true);
            bool killed = false;

            foreach (MonsterObject minion in minions)
            {
                if (minion?.Node == null || minion.Dead) continue;

                minion.EXPOwner = null;
                _customMinionExpiry.Remove(minion);

                minion.Die();

                if (despawn)
                    minion.Despawn();

                if (clear)
                    MinionList.Remove(minion);

                killed = true;
            }

            return killed;
        }

        private bool ExecuteBroadcastMessage(CustomMonsterAiActionInfo action)
        {
            string message = GetActionOptionValue(action, "Message");
            if (string.IsNullOrWhiteSpace(message))
                message = GetActionOptionValue(action, "Text");
            if (string.IsNullOrWhiteSpace(message))
                message = GetActionOptionValue(action, "Format");
            if (string.IsNullOrWhiteSpace(message)) return false;

            message = FormatCustomMessage(message);
            MessageType messageType = GetActionOptionEnum(action, "MessageType", MessageType.System);
            string scope = GetActionOptionValue(action, "Scope");
            if (string.IsNullOrWhiteSpace(scope))
                scope = "All";

            switch (scope.Trim().ToUpperInvariant())
            {
                case "MAP":
                    if (CurrentMap == null) return false;
                    foreach (PlayerObject player in CurrentMap.Players.ToArray())
                        player.Connection?.ReceiveChat(message, messageType);
                    return true;
                case "RADIUS":
                case "NEARBY":
                    if (CurrentMap == null) return false;
                    int radius = action.Radius > 0 ? action.Radius : MonsterInfo?.ViewRange > 0 ? MonsterInfo.ViewRange : ViewRange;
                    foreach (PlayerObject player in CurrentMap.Players.ToArray())
                    {
                        if (player?.Node == null || !Functions.InRange(player.CurrentLocation, CurrentLocation, radius)) continue;
                        player.Connection?.ReceiveChat(message, messageType);
                    }
                    return true;
                default:
                    foreach (SConnection con in SEnvir.Connections)
                        con.ReceiveChat(message, messageType);
                    return true;
            }
        }

        private bool ExecuteTeleportTargetsToRegion(CustomMonsterAiActionInfo action)
        {
            if (CurrentMap == null || !TryGetActionMapRegion(action, out MapRegion region) || region == null) return false;

            Map map = SEnvir.GetMap(region.Map, CurrentMap.Instance, CurrentMap.InstanceSequence);
            if (map == null) return false;

            if (region.PointList == null)
                region.CreatePoints(map.Width);

            if (region.PointList == null || region.PointList.Count == 0) return false;

            int radius = action.Radius > 0 ? action.Radius : MonsterInfo?.ViewRange > 0 ? MonsterInfo.ViewRange : ViewRange;
            bool includePlayers = GetActionOptionBool(action, "Players", GetActionOptionBool(action, "IncludePlayers", true));
            bool includeMonsters = GetActionOptionBool(action, "Monsters", GetActionOptionBool(action, "IncludeMonsters", false));
            bool includePets = GetActionOptionBool(action, "Pets", GetActionOptionBool(action, "IncludePets", includeMonsters));
            bool includeWildMonsters = GetActionOptionBool(action, "WildMonsters", GetActionOptionBool(action, "IncludeWildMonsters", includeMonsters));
            bool ignoreSafeZone = GetActionOptionBool(action, "IgnoreSafeZone", false);
            bool teleported = false;

            if (includePlayers)
            {
                foreach (PlayerObject player in CurrentMap.Players.ToArray())
                {
                    if (player?.Node == null || player.Dead) continue;
                    if (!ignoreSafeZone && player.InSafeZone) continue;
                    if (!Functions.InRange(player.CurrentLocation, CurrentLocation, radius)) continue;

                    teleported |= TeleportToRegionPoint(player, map, region);
                }
            }

            if (includeMonsters || includePets || includeWildMonsters)
            {
                foreach (MapObject ob in CurrentMap.Objects.ToArray())
                {
                    if (ob == this || ob?.Race != ObjectType.Monster || ob.Node == null || ob.Dead) continue;
                    if (ob is Guard or CastleLord) continue;
                    if (!ignoreSafeZone && ob.InSafeZone) continue;
                    if (!Functions.InRange(ob.CurrentLocation, CurrentLocation, radius)) continue;

                    MonsterObject monster = (MonsterObject)ob;
                    bool isPet = monster.PetOwner != null;
                    if (isPet && !includePets) continue;
                    if (!isPet && !includeWildMonsters) continue;

                    teleported |= TeleportToRegionPoint(monster, map, region);
                }
            }

            return teleported;
        }

        private bool ExecuteDespawnSelf(CustomMonsterAiActionInfo action)
        {
            if (SpawnInfo != null && GetActionOptionBool(action, "DecreaseAliveCount", true))
                SpawnInfo.AliveCount = Math.Max(0, SpawnInfo.AliveCount - 1);

            SpawnInfo = null;
            Despawn();
            return true;
        }

        private bool ExecuteSpawnSpellObject(CustomMonsterAiActionInfo action)
        {
            if (CurrentMap == null) return false;

            string effectName = GetActionOptionValue(action, "SpellEffect");
            if (string.IsNullOrWhiteSpace(effectName))
                effectName = GetActionOptionValue(action, "Effect");
            if (string.IsNullOrWhiteSpace(effectName) || !Enum.TryParse(effectName, true, out SpellEffect effect) || effect == SpellEffect.None)
                return false;

            MapObject target = GetExecutionTarget();
            if (action.Distance > 0 && target != null && !Functions.InRange(CurrentLocation, target.CurrentLocation, action.Distance))
                return false;

            bool perTarget = GetActionOptionBool(action, "PerTarget", false);
            List<MapObject> selectedTargets = perTarget
                ? GetActionTargets(action, requireAttackTarget: true).Where(x => x?.CurrentMap == CurrentMap).Distinct().ToList()
                : [];
            List<Point> origins = selectedTargets.Count > 0
                ? selectedTargets.Select(x => x.CurrentLocation).Distinct().ToList()
                : [GetActionLocation(action) ?? CurrentLocation];

            int radius = Math.Max(0, action.Radius);
            List<Cell> allCells = [];

            bool visibleOriginOnly = GetActionOptionBool(action, "VisibleOriginOnly", false);
            int duration = Math.Max(0, action.Duration);
            int durationRandom = Math.Max(0, GetActionOptionInt(action, "DurationRandom", 0));
            if (durationRandom > 0)
                duration += SEnvir.Random.Next(durationRandom);

            TimeSpan tickFrequency = TimeSpan.FromMilliseconds(Math.Max(0, action.TickRate));
            int tickCount = GetSpawnSpellObjectTickCount(action, effect);

            foreach (Point origin in origins)
            {
                List<Cell> cells = CurrentMap.GetCells(origin, 0, radius).Where(x => x != null).ToList();
                foreach (Cell cell in cells)
                {
                    SpellObject spell = new()
                    {
                        DisplayLocation = origin,
                        TickCount = tickCount,
                        TickFrequency = tickFrequency,
                        Owner = this,
                        Effect = effect,
                        Power = Math.Max(0, action.FixedPower),
                        Visible = !visibleOriginOnly || cell.Location == origin,
                    };

                    if (duration > 0)
                        spell.TickTime = SEnvir.Now.AddMilliseconds(duration);

                    spell.Spawn(cell.Map, cell.Location);
                    allCells.Add(cell);
                }
            }

            if (allCells.Count == 0) return false;

            MagicType magic = GetActionMagicType(action);
            if (magic != MagicType.None || GetActionOptionBool(action, "BroadcastMagic", false))
            {
                if (target != null)
                    Direction = Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation);

                Broadcast(new S.ObjectMagic
                {
                    ObjectID = ObjectID,
                    Direction = Direction,
                    CurrentLocation = CurrentLocation,
                    Cast = true,
                    Type = magic,
                    MagicInfoIndex = action.MagicInfo?.Index ?? 0,
                    Targets = selectedTargets.Count > 0 ? selectedTargets.Select(x => x.ObjectID).ToList() : target == null ? [] : [target.ObjectID],
                    Locations = allCells.Select(x => x.Location).Distinct().ToList(),
                    AttackElement = GetActionElement(action),
                    AnimationVariant = action.AnimationVariant
                });
            }

            UpdateAttackTime();
            return true;
        }

        private static int GetSpawnSpellObjectTickCount(CustomMonsterAiActionInfo action, SpellEffect effect)
        {
            int tickCount = action.FixedCount > 0 ? action.FixedCount : 1;

            return effect == SpellEffect.MonsterDeathCloud ? 0 : tickCount;
        }

        private static MonsterFlag GetActionMonsterFlag(CustomMonsterAiActionInfo action)
        {
            string flagName = GetActionOptionValue(action, "MonsterFlag");
            if (string.IsNullOrWhiteSpace(flagName))
                flagName = GetActionOptionValue(action, "Flag");

            return !string.IsNullOrWhiteSpace(flagName) && Enum.TryParse(flagName, true, out MonsterFlag flag)
                ? flag
                : MonsterFlag.None;
        }

        private Point ApplyActionOriginOffset(CustomMonsterAiActionInfo action, Point origin)
        {
            string directionName = GetActionOptionValue(action, "OriginDirection");
            if (string.IsNullOrWhiteSpace(directionName))
                directionName = GetActionOptionValue(action, "OffsetDirection");

            if (string.IsNullOrWhiteSpace(directionName) || !TryGetActionDirection(directionName, GetExecutionTarget(), out MirDirection direction))
                return origin;

            int distance = GetActionOptionInt(action, "OriginDistance", GetActionOptionInt(action, "OffsetDistance", 1));
            distance = Math.Max(0, distance);
            return distance == 0 ? origin : Functions.Move(origin, direction, distance);
        }

        private bool TryGetActionDirection(string directionName, MapObject selectedTarget, out MirDirection direction)
        {
            direction = Direction;
            if (string.IsNullOrWhiteSpace(directionName)) return false;

            MapObject target = selectedTarget ?? GetExecutionTarget();

            if (directionName.Equals("Target", StringComparison.OrdinalIgnoreCase)
                || directionName.Equals("CurrentTarget", StringComparison.OrdinalIgnoreCase))
            {
                if (target?.CurrentMap != CurrentMap || target.CurrentLocation == CurrentLocation) return false;

                direction = Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation);
                return true;
            }

            if (directionName.Equals("Away", StringComparison.OrdinalIgnoreCase)
                || directionName.Equals("AwayFromTarget", StringComparison.OrdinalIgnoreCase)
                || directionName.Equals("FromTarget", StringComparison.OrdinalIgnoreCase))
            {
                if (target?.CurrentMap != CurrentMap || target.CurrentLocation == CurrentLocation) return false;

                direction = Functions.DirectionFromPoint(target.CurrentLocation, CurrentLocation);
                return true;
            }

            return Enum.TryParse(directionName, true, out direction);
        }

        private bool ExecutePullTarget(CustomMonsterAiActionInfo action)
        {
            if (CurrentMap == null) return false;

            List<MapObject> targets = GetActionTargets(action, requireAttackTarget: true);
            if (targets.Count == 0) return false;

            bool pulled = false;
            int radius = Math.Max(1, action.Radius == 0 ? 1 : action.Radius);

            foreach (MapObject target in targets)
            {
                if (target == null || target == this) continue;

                pulled |= TeleportObjectNear(target, CurrentMap, CurrentLocation, 0, radius);
            }

            return pulled;
        }

        private bool ExecutePushTarget(CustomMonsterAiActionInfo action)
        {
            List<MapObject> targets = GetActionTargets(action, requireAttackTarget: true);
            if (targets.Count == 0) return false;

            bool pushed = false;
            int distance = Math.Max(1, action.Distance == 0 ? 1 : action.Distance);

            foreach (MapObject target in targets)
            {
                if (target == null || target == this) continue;

                string directionName = GetActionOptionValue(action, "PushDirection");
                MirDirection direction = string.IsNullOrWhiteSpace(directionName) || !TryGetActionDirection(directionName, target, out MirDirection configured)
                    ? Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation)
                    : configured;
                pushed |= target.Pushed(direction, distance) > 0;
            }

            return pushed;
        }

        private bool ExecuteCastSkill(CustomMonsterAiActionInfo action)
        {
            if (!CanAttack || action.MagicInfo == null || !SEnvir.CustomSystem.TryGetSkill(action.MagicInfo, out CustomSkillInfo skill) || skill == null || !skill.Enabled)
                return false;

            if (UsesLocationPayload(skill) && GetActionOptionBool(action, "PerTarget", false))
                return ExecuteCastSkillPerTarget(action, skill);

            MapObject target = GetSkillActionTarget(action, skill);
            Point location = GetActionLocation(action, target) ?? CurrentLocation;
            location = ApplyActionOriginOffset(action, location);
            int range = action.Distance > 0 ? action.Distance : skill.Range > 0 ? skill.Range : Globals.MagicRange;
            MirDirection castDirection = GetSkillPreviewDirection(target, location);

            if (!SkillCastInRange(skill, target, location, range, castDirection))
                return false;

            Direction = castDirection;

            object payload;
            List<uint> targets = new();
            List<Point> locations = new();

            switch (skill.Template)
            {
                case CustomSkillTemplate.ChainDamage:
                    if (target == null) return false;
                    SkillChainPayload chainPayload = BuildChainPayload(skill, target);
                    if (chainPayload.Targets.Count == 0) return false;
                    payload = chainPayload;
                    targets.AddRange(chainPayload.Targets.Select(x => x.ObjectID));
                    location = target.CurrentLocation;
                    break;
                case CustomSkillTemplate.AreaDamage:
                case CustomSkillTemplate.PersistentAreaDamage:
                    payload = CreateSkillAreaPayload(skill, target, location, castDirection, range);
                    locations.Add(((SkillAreaPayload)payload).Location);
                    break;
                case CustomSkillTemplate.Heal:
                case CustomSkillTemplate.Buff:
                    if (target == null) return false;
                    payload = CreateSkillTargetPayload(target, range);
                    targets.Add(target.ObjectID);
                    break;
                case CustomSkillTemplate.Summon:
                    Point summonLocation = Functions.Move(CurrentLocation, Direction, -1);
                    payload = new SkillLocationPayload { Map = CurrentMap, Location = summonLocation, Origin = CurrentLocation };
                    locations.Add(summonLocation);
                    break;
                case CustomSkillTemplate.DamageProxy:
                    if (target == null) return false;
                    Direction = Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation);
                    Point proxyLocation = Functions.Move(CurrentLocation, Direction, 1);
                    payload = new SkillDamageProxyPayload { Map = CurrentMap, Target = target, Location = proxyLocation, Origin = CurrentLocation };
                    targets.Add(target.ObjectID);
                    locations.Add(proxyLocation);
                    break;
                case CustomSkillTemplate.Move:
                    payload = new SkillLocationPayload { Map = CurrentMap, Location = location, Origin = CurrentLocation, Range = range };
                    locations.Add(location);
                    break;
                default:
                    if (target == null) return false;
                    payload = ShouldCaptureOffensiveSelfAreaPayload(skill, target)
                        ? CreateSkillAreaPayload(skill, target, location, castDirection, range)
                        : CreateSkillTargetPayload(target, range);
                    targets.Add(target.ObjectID);
                    if (payload is SkillAreaPayload areaPayload)
                        locations.Add(areaPayload.Location);
                    location = target.CurrentLocation;
                    break;
            }

            if (target != null && target != this)
                Direction = Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation);
            else if (target == this && Target?.Node != null && Target.CurrentMap == CurrentMap && Target.CurrentLocation != CurrentLocation)
                Direction = Functions.DirectionFromPoint(CurrentLocation, Target.CurrentLocation);

            Broadcast(new S.ObjectMagic
            {
                ObjectID = ObjectID,
                Direction = Direction,
                CurrentLocation = CurrentLocation,
                Type = action.MagicInfo.Magic,
                MagicInfoIndex = action.MagicInfo.Index,
                Targets = targets,
                Locations = locations,
                Cast = true,
                AttackElement = skill.Element,
                AnimationVariant = action.AnimationVariant
            });

            UpdateAttackTime();
            ActionList.Add(new DelayedAction(GetSkillDelay(skill, payload, action.Delay), ActionType.DelayMagic, action.MagicInfo.Magic, action.MagicInfo.Index, skill, payload));
            return true;
        }

        private bool ExecuteCastSkillPerTarget(CustomMonsterAiActionInfo action, CustomSkillInfo skill)
        {
            bool requireAttackTarget = RequiresAttackTarget(skill, action.Target);
            List<MapObject> selectedTargets = GetActionTargets(action, requireAttackTarget);
            if (selectedTargets.Count == 0) return false;

            int range = action.Distance > 0 ? action.Distance : skill.Range > 0 ? skill.Range : Globals.MagicRange;
            List<uint> targets = new();
            List<Point> locations = new();
            List<object> payloads = new();

            foreach (MapObject target in selectedTargets)
            {
                if (action.TargetChance < 100 && SEnvir.Random.Next(100) >= Math.Max(0, action.TargetChance)) continue;

                Point location = GetActionLocation(action, target) ?? target.CurrentLocation;
                location = ApplyActionOriginOffset(action, location);
                MirDirection castDirection = GetSkillPreviewDirection(target, location);
                if (!SkillCastInRange(skill, target, location, range, castDirection)) continue;

                object payload;

                if (skill.Template == CustomSkillTemplate.Move)
                {
                    payload = new SkillLocationPayload { Map = CurrentMap, Location = location, Origin = CurrentLocation, Range = range };
                    locations.Add(location);
                }
                else
                {
                    SkillAreaPayload areaPayload = CreateSkillAreaPayload(skill, target, location, castDirection, range);
                    payload = areaPayload;
                    locations.Add(areaPayload.Location);
                    targets.Add(target.ObjectID);
                }

                payloads.Add(payload);
            }

            if (payloads.Count == 0) return false;

            MapObject faceTarget = selectedTargets.FirstOrDefault(x => x?.Node != null && x.CurrentMap == CurrentMap);
            if (faceTarget != null && faceTarget != this)
                Direction = Functions.DirectionFromPoint(CurrentLocation, faceTarget.CurrentLocation);

            Broadcast(new S.ObjectMagic
            {
                ObjectID = ObjectID,
                Direction = Direction,
                CurrentLocation = CurrentLocation,
                Type = action.MagicInfo.Magic,
                MagicInfoIndex = action.MagicInfo.Index,
                Targets = targets,
                Locations = locations,
                Cast = true,
                AttackElement = skill.Element,
                AnimationVariant = action.AnimationVariant
            });

            UpdateAttackTime();

            foreach (object payload in payloads)
                ActionList.Add(new DelayedAction(GetSkillDelay(skill, payload, action.Delay), ActionType.DelayMagic, action.MagicInfo.Magic, action.MagicInfo.Index, skill, payload));

            return true;
        }

        private MapObject GetExecutionTarget()
        {
            if (_ruleContextTarget?.Node != null && !_ruleContextTarget.Dead && _ruleContextTarget.CurrentMap == CurrentMap)
                return _ruleContextTarget;

            if (Target?.Node != null && !Target.Dead && Target.CurrentMap == CurrentMap)
                return Target;

            return null;
        }

        private static CustomMonsterAiActionInfo CreateSkillAction(CustomMonsterSkillInfo entry, int range)
        {
            return new CustomMonsterAiActionInfo
            {
                Target = entry?.Target ?? CustomAiTargetSelectorType.CurrentTarget,
                AnimationVariant = entry?.AnimationVariant ?? 0,
                Radius = Math.Max(0, entry?.Range > 0 ? entry.Range : range),
                Distance = Math.Max(0, range),
                Delay = 500,
            };
        }

        private MapObject GetSkillActionTarget(CustomMonsterAiActionInfo action, CustomSkillInfo skill)
        {
            if (action == null || skill == null) return null;
            if (skill.TargetRule == CustomSkillTargetRule.Self || action.Target == CustomAiTargetSelectorType.Self)
                return this;

            bool helpfulSkill = IsHelpfulSkill(skill);
            MapObject target = GetActionTarget(action, RequiresAttackTarget(skill, action.Target));

            if (helpfulSkill)
            {
                if (target != null && CanHelpTarget(target))
                    return target;

                return skill.TargetRule == CustomSkillTargetRule.AllyOrSelf ? this : null;
            }

            if (skill.TargetRule == CustomSkillTargetRule.Ground)
                return target;

            return target;
        }

        private bool SkillCastInRange(CustomSkillInfo skill, MapObject target, Point location, int range, MirDirection direction)
        {
            if (skill == null) return false;

            range = Math.Max(1, range);

            if (skill.Template == CustomSkillTemplate.Summon)
                return true;

            if (UsesLocationPayload(skill))
            {
                if (!Functions.InRange(CurrentLocation, location, range)) return false;
                return !RequiresOffensiveSelfAreaTarget(skill, target) ||
                       HasOffensiveSkillAreaTarget(skill, location, direction);
            }

            if (target == null) return false;
            if (skill.Template == CustomSkillTemplate.Heal)
            {
                int maxHealth = target.Stats[Stat.Health];
                if (maxHealth <= 0 || target.CurrentHP >= maxHealth) return false;
            }

            if (RequiresOffensiveSelfAreaTarget(skill, target))
                return HasOffensiveSkillAreaTarget(skill, target.CurrentLocation, direction);

            return target == this || Functions.InRange(CurrentLocation, target.CurrentLocation, range);
        }

        private bool RequiresOffensiveSelfAreaTarget(CustomSkillInfo skill, MapObject target)
        {
            return IsOffensiveSkill(skill) &&
                   (skill.TargetRule == CustomSkillTargetRule.Self || ReferenceEquals(target, this));
        }

        private bool ShouldCaptureOffensiveSelfAreaPayload(CustomSkillInfo skill, MapObject target)
        {
            return RequiresOffensiveSelfAreaTarget(skill, target) && GetSkillSearchRadius(skill) > 0;
        }

        private bool HasOffensiveSkillAreaTarget(CustomSkillInfo skill, Point center, MirDirection direction)
        {
            if (CurrentMap == null || GetSkillSearchRadius(skill) <= 0) return false;

            return GetSkillAreaTargets(skill, center, direction).Count > 0;
        }

        private static bool UsesLocationPayload(CustomSkillInfo skill)
        {
            return skill?.Template is CustomSkillTemplate.AreaDamage
                or CustomSkillTemplate.PersistentAreaDamage
                or CustomSkillTemplate.Move;
        }

        private MirDirection GetSkillPreviewDirection(MapObject target, Point location)
        {
            if (target?.Node != null && target != this && target.CurrentLocation != CurrentLocation)
                return Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation);

            MapObject executionTarget = GetExecutionTarget();
            if ((target == this || location == CurrentLocation) &&
                executionTarget?.Node != null &&
                executionTarget != this &&
                executionTarget.CurrentMap == CurrentMap &&
                executionTarget.CurrentLocation != CurrentLocation)
                return Functions.DirectionFromPoint(CurrentLocation, executionTarget.CurrentLocation);

            return location == CurrentLocation ? Direction : Functions.DirectionFromPoint(CurrentLocation, location);
        }

        private static bool IsHelpfulSkill(CustomSkillInfo skill)
        {
            return skill?.Template is CustomSkillTemplate.Heal or CustomSkillTemplate.Buff
                || skill?.TargetRule is CustomSkillTargetRule.Ally
                    or CustomSkillTargetRule.AllyOrSelf;
        }

        private static bool RequiresAttackTarget(CustomSkillInfo skill, CustomAiTargetSelectorType selector)
        {
            if (skill == null || IsHelpfulSkill(skill) || IsAllySelector(selector))
                return false;

            if (IsOffensiveSkill(skill))
                return true;

            if (selector == CustomAiTargetSelectorType.Self)
                return false;

            return skill.TargetRule is CustomSkillTargetRule.Enemy or CustomSkillTargetRule.EnemyOrGround;
        }

        private static bool IsOffensiveSkill(CustomSkillInfo skill)
        {
            return skill?.Template is CustomSkillTemplate.DirectDamage
                or CustomSkillTemplate.ProjectileDamage
                or CustomSkillTemplate.ChainDamage
                or CustomSkillTemplate.AreaDamage
                or CustomSkillTemplate.PersistentAreaDamage
                or CustomSkillTemplate.Debuff
                or CustomSkillTemplate.Control
                or CustomSkillTemplate.DamageProxy;
        }

        private static bool IsAllySelector(CustomAiTargetSelectorType selector)
        {
            return selector is CustomAiTargetSelectorType.AllyRadius
                or CustomAiTargetSelectorType.RandomAllyInView
                or CustomAiTargetSelectorType.NearestAllyInView
                or CustomAiTargetSelectorType.LowestHealthAllyInView
                or CustomAiTargetSelectorType.Summoner
                or CustomAiTargetSelectorType.SummonerGroup;
        }

        private MapObject GetActionTarget(CustomMonsterAiActionInfo action, bool requireAttackTarget)
        {
            if (action?.Target == CustomAiTargetSelectorType.Self)
                return this;

            MapObject target = action?.Target switch
            {
                CustomAiTargetSelectorType.SelfRadius or CustomAiTargetSelectorType.AllyRadius or CustomAiTargetSelectorType.TargetRadius or CustomAiTargetSelectorType.AllInView
                    or CustomAiTargetSelectorType.RandomInView or CustomAiTargetSelectorType.NearestInView or CustomAiTargetSelectorType.FarthestInView
                    or CustomAiTargetSelectorType.LowestHealthInView or CustomAiTargetSelectorType.HighestHealthInView
                    or CustomAiTargetSelectorType.RandomAllyInView or CustomAiTargetSelectorType.NearestAllyInView or CustomAiTargetSelectorType.LowestHealthAllyInView
                    or CustomAiTargetSelectorType.Line
                    or CustomAiTargetSelectorType.ExperienceOwnerGroup
                    or CustomAiTargetSelectorType.SummonerGroup
                    => GetActionTargets(action, requireAttackTarget).FirstOrDefault(),
                CustomAiTargetSelectorType.ExperienceOwner
                    => EXPOwner,
                CustomAiTargetSelectorType.Summoner
                    => GetSummonerTarget(),
                _ => GetExecutionTarget(),
            };

            if (target == null) return null;
            if (target != this && target.CurrentMap != CurrentMap) return null;
            if (requireAttackTarget && !CanAttackTarget(target)) return null;

            return target;
        }

        private List<MapObject> GetActionTargets(CustomMonsterAiActionInfo action, bool requireAttackTarget)
        {
            List<MapObject> targets = new();
            if (action == null || CurrentMap == null) return targets;

            int radius = action.Radius > 0 ? action.Radius : 0;
            MapObject target = GetExecutionTarget();

            switch (action.Target)
            {
                case CustomAiTargetSelectorType.CurrentTarget:
                    if (target != null)
                    {
                        if (radius > 0)
                            targets.AddRange(GetObjects(target.CurrentLocation, radius, requireAttackTarget));
                        else
                            targets.Add(target);
                    }
                    break;
                case CustomAiTargetSelectorType.Self:
                    targets.Add(this);
                    break;
                case CustomAiTargetSelectorType.SelfRadius:
                    targets.AddRange(GetObjects(CurrentLocation, radius, requireAttackTarget));
                    break;
                case CustomAiTargetSelectorType.AllyRadius:
                    targets.AddRange(GetAllObjects(CurrentLocation, radius).Where(x => x != null && CanHelpTarget(x)));
                    break;
                case CustomAiTargetSelectorType.TargetRadius:
                    if (target != null)
                        targets.AddRange(GetObjects(target.CurrentLocation, radius, requireAttackTarget));
                    break;
                case CustomAiTargetSelectorType.Line:
                    targets.AddRange(GetLineTargets(action, requireAttackTarget));
                    break;
                case CustomAiTargetSelectorType.AllInView:
                    targets.AddRange(GetObjects(CurrentLocation, radius == 0 ? Config.MaxViewRange : radius, requireAttackTarget));
                    break;
                case CustomAiTargetSelectorType.RandomInView:
                case CustomAiTargetSelectorType.NearestInView:
                case CustomAiTargetSelectorType.FarthestInView:
                case CustomAiTargetSelectorType.LowestHealthInView:
                case CustomAiTargetSelectorType.HighestHealthInView:
                    targets.AddRange(GetViewObjects(action, requireAttackTarget, alliesOnly: false));
                    break;
                case CustomAiTargetSelectorType.RandomAllyInView:
                case CustomAiTargetSelectorType.NearestAllyInView:
                case CustomAiTargetSelectorType.LowestHealthAllyInView:
                    targets.AddRange(GetViewObjects(action, requireAttackTarget: false, alliesOnly: true));
                    break;
                case CustomAiTargetSelectorType.ExperienceOwner:
                    if (EXPOwner != null)
                        targets.Add(EXPOwner);
                    break;
                case CustomAiTargetSelectorType.ExperienceOwnerGroup:
                    targets.AddRange(GetExperienceOwnerGroupTargets());
                    break;
                case CustomAiTargetSelectorType.Summoner:
                    MapObject summoner = GetSummonerTarget();
                    if (summoner != null)
                        targets.Add(summoner);
                    break;
                case CustomAiTargetSelectorType.SummonerGroup:
                    targets.AddRange(GetSummonerGroupTargets());
                    break;
                case CustomAiTargetSelectorType.CurrentTargetLocation:
                    if (target != null)
                    {
                        if (radius > 0)
                            targets.AddRange(GetObjects(target.CurrentLocation, radius, requireAttackTarget));
                        else
                            targets.Add(target);
                    }
                    break;
                default:
                    if (target != null)
                        targets.Add(target);
                    break;
            }

            IEnumerable<MapObject> filteredTargets = targets
                .Where(x => x?.Node != null && !x.Dead && x.CurrentMap == CurrentMap)
                .Where(x => !requireAttackTarget || CanAttackTarget(x))
                .Distinct();

            filteredTargets = ApplyActionTargetFilters(action, filteredTargets);

            IEnumerable<MapObject> orderedTargets = OrderTargetsBySelector(filteredTargets, action.Target);

            if (action.RandomTargetCount > 0)
                orderedTargets = orderedTargets.OrderBy(_ => SEnvir.Random.Next()).Take(action.RandomTargetCount);
            else if (action.MaxTargets > 0)
                orderedTargets = orderedTargets.Take(action.MaxTargets);

            return orderedTargets.ToList();
        }

        private static IEnumerable<MapObject> ApplyActionTargetFilters(CustomMonsterAiActionInfo action, IEnumerable<MapObject> targets)
        {
            if (action == null || targets == null) return Enumerable.Empty<MapObject>();

            if (GetActionOptionBool(action, "RequireInjured", false))
                targets = targets.Where(x => x?.Stats[Stat.Health] > 0 && x.CurrentHP < x.Stats[Stat.Health]);

            BuffType requiredBuff = GetActionOptionBuffType(action, "RequireHasBuff");
            if (requiredBuff != BuffType.None)
                targets = targets.Where(x => HasRuleBuff(x, requiredBuff));

            BuffType missingBuff = GetActionOptionBuffType(action, "RequireMissingBuff");
            if (missingBuff != BuffType.None)
                targets = targets.Where(x => !HasRuleBuff(x, missingBuff));

            return targets;
        }

        private static bool UsesTargetCollection(CustomAiTargetSelectorType selector)
        {
            return selector is CustomAiTargetSelectorType.SelfRadius
                or CustomAiTargetSelectorType.AllyRadius
                or CustomAiTargetSelectorType.TargetRadius
                or CustomAiTargetSelectorType.Line
                or CustomAiTargetSelectorType.AllInView
                or CustomAiTargetSelectorType.RandomInView
                or CustomAiTargetSelectorType.NearestInView
                or CustomAiTargetSelectorType.FarthestInView
                or CustomAiTargetSelectorType.LowestHealthInView
                or CustomAiTargetSelectorType.HighestHealthInView
                or CustomAiTargetSelectorType.RandomAllyInView
                or CustomAiTargetSelectorType.NearestAllyInView
                or CustomAiTargetSelectorType.LowestHealthAllyInView
                or CustomAiTargetSelectorType.ExperienceOwnerGroup
                or CustomAiTargetSelectorType.SummonerGroup;
        }

        private IEnumerable<MapObject> GetLineTargets(CustomMonsterAiActionInfo action, bool requireAttackTarget)
        {
            if (CurrentMap == null) yield break;

            MapObject target = GetExecutionTarget();
            bool allDirections = GetActionOptionBool(action, "AllDirections", false);
            int distance = Math.Max(1, action.Distance > 0 ? action.Distance : Behavior?.AttackRange ?? 1);
            int minOffset = allDirections ? 0 : action.MinOffset;
            int maxOffset = allDirections ? 7 : action.MaxOffset;

            if (maxOffset < minOffset)
                (minOffset, maxOffset) = (maxOffset, minOffset);

            MirDirection baseDirection = Direction;
            if (target?.CurrentMap == CurrentMap && target != this && target.CurrentLocation != CurrentLocation)
                baseDirection = Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation);

            HashSet<MapObject> selected = new();

            for (int offset = minOffset; offset <= maxOffset; offset++)
            {
                MirDirection direction = allDirections
                    ? Functions.ShiftDirection(MirDirection.Up, offset)
                    : Functions.ShiftDirection(baseDirection, offset);

                for (int i = 1; i <= distance; i++)
                {
                    Cell cell = CurrentMap.GetCell(Functions.Move(CurrentLocation, direction, i));
                    if (cell?.Objects == null) continue;

                    foreach (MapObject ob in cell.Objects.ToArray())
                    {
                        if (ob == this || ob?.Node == null || ob.Dead || !ob.Visible || ob.CurrentMap != CurrentMap) continue;
                        if (requireAttackTarget && !CanAttackTarget(ob)) continue;
                        if (!selected.Add(ob)) continue;

                        yield return ob;
                    }
                }
            }
        }

        private IEnumerable<MapObject> GetViewObjects(CustomMonsterAiActionInfo action, bool requireAttackTarget, bool alliesOnly)
        {
            int radius = action.Radius > 0 ? action.Radius : ViewRange > 0 ? ViewRange : Config.MaxViewRange;

            IEnumerable<MapObject> objects = alliesOnly
                ? GetAllObjects(CurrentLocation, radius).Where(x => x != null && x != this && CanHelpTarget(x))
                : requireAttackTarget
                    ? GetTargets(CurrentMap, CurrentLocation, radius)
                    : GetAllObjects(CurrentLocation, radius);

            return objects.Where(x => x?.Node != null && !x.Dead && x.Visible && x.CurrentMap == CurrentMap);
        }

        private IEnumerable<MapObject> GetExperienceOwnerGroupTargets()
        {
            if (EXPOwner == null) yield break;

            HashSet<PlayerObject> players = new() { EXPOwner };

            if (EXPOwner.GroupMembers != null)
            {
                foreach (PlayerObject player in EXPOwner.GroupMembers)
                {
                    if (player != null)
                        players.Add(player);
                }
            }

            foreach (PlayerObject player in players)
            {
                if (player?.Node == null || player.Dead || !player.Visible || player.CurrentMap != CurrentMap) continue;
                if (!Functions.InRange(player.CurrentLocation, CurrentLocation, Config.MaxViewRange)) continue;

                yield return player;
            }
        }

        private MapObject GetSummonerTarget()
        {
            if (PetOwner?.Node != null && !PetOwner.Dead && PetOwner.CurrentMap == CurrentMap)
                return PetOwner;

            if (Master?.Node != null && !Master.Dead && Master.CurrentMap == CurrentMap)
                return Master;

            return null;
        }

        private IEnumerable<MapObject> GetSummonerGroupTargets()
        {
            if (PetOwner?.Node != null && !PetOwner.Dead && PetOwner.CurrentMap == CurrentMap)
            {
                HashSet<PlayerObject> players = new() { PetOwner };

                if (PetOwner.GroupMembers != null)
                {
                    foreach (PlayerObject player in PetOwner.GroupMembers)
                    {
                        if (player != null)
                            players.Add(player);
                    }
                }

                foreach (PlayerObject player in players)
                {
                    if (player?.Node == null || player.Dead || !player.Visible || player.CurrentMap != CurrentMap) continue;
                    if (!Functions.InRange(player.CurrentLocation, CurrentLocation, Config.MaxViewRange)) continue;

                    yield return player;
                }

                yield break;
            }

            if (Master?.Node != null && !Master.Dead && Master.Visible && Master.CurrentMap == CurrentMap)
                yield return Master;
        }

        private IEnumerable<MapObject> OrderTargetsBySelector(IEnumerable<MapObject> targets, CustomAiTargetSelectorType selector)
        {
            return selector switch
            {
                CustomAiTargetSelectorType.RandomInView or CustomAiTargetSelectorType.RandomAllyInView
                    => targets.OrderBy(_ => SEnvir.Random.Next()),
                CustomAiTargetSelectorType.NearestInView or CustomAiTargetSelectorType.NearestAllyInView
                    => targets.OrderBy(x => Functions.Distance(CurrentLocation, x.CurrentLocation)).ThenBy(_ => SEnvir.Random.Next()),
                CustomAiTargetSelectorType.FarthestInView
                    => targets.OrderByDescending(x => Functions.Distance(CurrentLocation, x.CurrentLocation)).ThenBy(_ => SEnvir.Random.Next()),
                CustomAiTargetSelectorType.LowestHealthInView or CustomAiTargetSelectorType.LowestHealthAllyInView
                    => targets.OrderBy(GetHealthPercent).ThenBy(x => Functions.Distance(CurrentLocation, x.CurrentLocation)).ThenBy(_ => SEnvir.Random.Next()),
                CustomAiTargetSelectorType.HighestHealthInView
                    => targets.OrderByDescending(GetHealthPercent).ThenBy(x => Functions.Distance(CurrentLocation, x.CurrentLocation)).ThenBy(_ => SEnvir.Random.Next()),
                _ => targets,
            };
        }

        private static int GetHealthPercent(MapObject ob)
        {
            int max = ob?.Stats[Stat.Health] ?? 0;
            return max <= 0 ? 100 : (int)(Math.Max(0, (long)ob.CurrentHP) * 100 / max);
        }

        private List<MapObject> GetObjects(Point location, int radius, bool requireAttackTarget)
        {
            radius = Math.Max(0, radius);

            return requireAttackTarget
                ? GetTargets(CurrentMap, location, radius)
                : GetAllObjects(location, radius)
                    .Where(x => x?.Node != null && !x.Dead && x.Visible)
                    .ToList();
        }

        private Point? GetActionLocation(CustomMonsterAiActionInfo action, MapObject selectedTarget = null)
        {
            if (action == null) return null;

            MapObject target = selectedTarget ?? GetExecutionTarget();

            return action.Target switch
            {
                CustomAiTargetSelectorType.Self or CustomAiTargetSelectorType.SelfRadius or CustomAiTargetSelectorType.AllyRadius or CustomAiTargetSelectorType.AllInView => CurrentLocation,
                CustomAiTargetSelectorType.Line => selectedTarget?.CurrentMap == CurrentMap ? selectedTarget.CurrentLocation : CurrentLocation,
                CustomAiTargetSelectorType.ExperienceOwner => EXPOwner?.CurrentMap == CurrentMap ? EXPOwner.CurrentLocation : CurrentLocation,
                CustomAiTargetSelectorType.Summoner => GetSummonerTarget()?.CurrentLocation ?? CurrentLocation,
                CustomAiTargetSelectorType.SummonerGroup => selectedTarget?.CurrentMap == CurrentMap ? selectedTarget.CurrentLocation : GetSummonerTarget()?.CurrentLocation ?? CurrentLocation,
                CustomAiTargetSelectorType.CurrentTarget or CustomAiTargetSelectorType.CurrentTargetLocation or CustomAiTargetSelectorType.TargetRadius => target?.CurrentLocation,
                _ => target?.CurrentLocation ?? CurrentLocation,
            };
        }

        private bool InActionRange(MapObject target, CustomMonsterAiActionInfo action)
        {
            if (target?.CurrentMap != CurrentMap || target == this || target.CurrentLocation == CurrentLocation) return false;

            int range = Math.Max(1, action?.Distance > 0 ? action.Distance : Behavior?.AttackRange ?? 1);
            return Functions.InRange(CurrentLocation, target.CurrentLocation, range);
        }

        private bool ActionTargetWithinDistance(CustomMonsterAiActionInfo action, MapObject target)
        {
            if (action?.Distance <= 0) return true;
            return target?.CurrentMap == CurrentMap && Functions.InRange(CurrentLocation, target.CurrentLocation, action.Distance);
        }

        private bool IsTargetInLine(MapObject target, int distance)
        {
            if (target?.CurrentMap != CurrentMap || target == this || target.CurrentLocation == CurrentLocation) return false;

            int maxDistance = distance > 0 ? distance : Math.Max(1, Behavior?.AttackRange ?? 1);
            int x = Math.Abs(target.CurrentLocation.X - CurrentLocation.X);
            int y = Math.Abs(target.CurrentLocation.Y - CurrentLocation.Y);

            if (x > maxDistance || y > maxDistance) return false;

            return x == 0 || y == 0 || x == y;
        }

        private int GetActionPower(CustomMonsterAiActionInfo action, MapObject target = null)
        {
            int source = action.PowerSource switch
            {
                CustomAiPowerSource.MC => GetMC(),
                CustomAiPowerSource.SC => GetSC(),
                CustomAiPowerSource.Fixed => 0,
                _ => GetDC(),
            };

            int rate = Math.Max(0, action.PowerRate);
            if (TryGetClassPowerRate(action, target, out int classRate))
                rate = classRate;

            long power = action.FixedPower + source * rate / 100L;

            int targetHealth = target?.Stats[Stat.Health] ?? 0;
            if (targetHealth > 0)
            {
                int targetHealthRate = Math.Max(0, GetActionOptionInt(action, "TargetHealthRate", 0));
                int targetHealthSourceRate = Math.Max(0, GetActionOptionInt(action, "TargetHealthSourceRate", 0));
                int targetHealthPowerSource = GetActionPowerSource(action, "TargetHealthPower", source);

                power += targetHealth * targetHealthRate / 100L;
                power += (long)targetHealthPowerSource * targetHealth * targetHealthSourceRate / 10000L;
            }

            if (power > int.MaxValue)
                return int.MaxValue;

            return Math.Max(1, (int)power);
        }

        private static bool TryGetClassPowerRate(CustomMonsterAiActionInfo action, MapObject target, out int rate)
        {
            rate = 0;
            if (action == null || target == null) return false;

            string value = GetActionOptionValue(action, "ClassPowerRates");
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (!TryGetTargetClass(target, out MirClass targetClass)) return false;

            foreach (string token in value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = token.Split(':', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2) continue;
                if (!Enum.TryParse(parts[0], true, out MirClass configuredClass)) continue;
                if (configuredClass != targetClass) continue;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) continue;

                rate = Math.Max(0, parsed);
                return true;
            }

            return false;
        }

        private static bool TryGetTargetClass(MapObject target, out MirClass targetClass)
        {
            switch (target)
            {
                case PlayerObject player:
                    targetClass = player.Class;
                    return true;
                case HeroObject hero:
                    targetClass = hero.Class;
                    return true;
                default:
                    targetClass = MirClass.Warrior;
                    return false;
            }
        }

        private static bool TargetClassIs(MapObject target, MirClass expected)
        {
            return TryGetTargetClass(target, out MirClass targetClass) && targetClass == expected;
        }

        private Element GetActionElement(CustomMonsterAiActionInfo action)
        {
            return action.Element == Element.None ? AttackElement : action.Element;
        }

        private static MagicType GetActionMagicType(CustomMonsterAiActionInfo action)
        {
            return action.MagicInfo?.Magic ?? action.Magic;
        }

        private static string GetRuleCooldownKey(CustomMonsterAiRuleInfo rule)
        {
            return rule.Index > 0 ? rule.Index.ToString() : $"{rule.Trigger}:{rule.Priority}:{rule.Name}:{rule.ActionName}";
        }

        private static List<(string Name, int Weight)> ParseWeightedOptions(string value)
        {
            List<(string Name, int Weight)> result = new();
            if (string.IsNullOrWhiteSpace(value)) return result;

            foreach (string token in SplitActionOptions(value, splitBareItems: true))
            {
                if (token.Contains('=', StringComparison.Ordinal))
                    continue;

                string[] parts = token.Split(':', 2, StringSplitOptions.TrimEntries);
                string name = parts[0];
                int weight = parts.Length == 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 1;

                if (!string.IsNullOrWhiteSpace(name) && weight > 0)
                    result.Add((name, weight));
            }

            return result;
        }

        private static bool GetActionOptionBool(CustomMonsterAiActionInfo action, string key, bool defaultValue)
        {
            string value = GetActionOptionValue(action, key);
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;

            if (bool.TryParse(value, out bool result)) return result;
            if (value.Equals("1", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
            if (value.Equals("0", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
            return defaultValue;
        }

        private static int GetActionOptionInt(CustomMonsterAiActionInfo action, string key, int defaultValue)
        {
            string value = GetActionOptionValue(action, key);
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : defaultValue;
        }

        private int GetActionPowerSource(CustomMonsterAiActionInfo action, string key, int defaultValue)
        {
            string value = GetActionOptionValue(action, key);
            if (!Enum.TryParse(value, true, out CustomAiPowerSource source))
                return defaultValue;

            return source switch
            {
                CustomAiPowerSource.MC => GetMC(),
                CustomAiPowerSource.SC => GetSC(),
                CustomAiPowerSource.Fixed => 0,
                _ => GetDC(),
            };
        }

        private static BuffType GetActionOptionBuffType(CustomMonsterAiActionInfo action, string key)
        {
            string value = GetActionOptionValue(action, key);
            return Enum.TryParse(value, true, out BuffType result) ? result : BuffType.None;
        }

        private static TEnum GetActionOptionEnum<TEnum>(CustomMonsterAiActionInfo action, string key, TEnum defaultValue) where TEnum : struct, Enum
        {
            string value = GetActionOptionValue(action, key);
            return Enum.TryParse(value, true, out TEnum result) ? result : defaultValue;
        }

        private static string GetActionOptionValue(CustomMonsterAiActionInfo action, string key)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.Options)) return null;

            foreach (string token in SplitActionOptions(action.Options, splitBareItems: false))
            {
                string[] parts = token.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && parts[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return TrimOptionQuotes(parts[1]);
            }

            return null;
        }

        private static IEnumerable<string> SplitActionOptions(string options, bool splitBareItems = false)
        {
            if (string.IsNullOrWhiteSpace(options)) yield break;

            int start = 0;
            bool inQuote = false;
            bool escaped = false;

            for (int i = 0; i < options.Length; i++)
            {
                char ch = options[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (inQuote && ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                {
                    inQuote = !inQuote;
                    continue;
                }

                if (ch != ',' || inQuote || !ShouldSplitActionOptionComma(options, i, splitBareItems)) continue;

                string token = options[start..i].Trim();
                if (token.Length > 0) yield return token;
                start = i + 1;
            }

            string last = options[start..].Trim();
            if (last.Length > 0) yield return last;
        }

        private static bool ShouldSplitActionOptionComma(string options, int commaIndex, bool splitBareItems)
        {
            if (splitBareItems)
                return true;

            int index = commaIndex + 1;
            while (index < options.Length && char.IsWhiteSpace(options[index]))
                index++;

            if (index >= options.Length || options[index] == '"')
                return false;

            bool hasName = false;
            for (; index < options.Length; index++)
            {
                char ch = options[index];

                if (ch == '=')
                    return hasName;

                if (ch == ',')
                    return false;

                if (char.IsWhiteSpace(ch))
                    continue;

                if (char.IsLetterOrDigit(ch) || ch == '_')
                {
                    hasName = true;
                    continue;
                }

                return false;
            }

            return false;
        }

        private static string TrimOptionQuotes(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;

            value = value.Trim();
            return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
                ? value[1..^1].Replace("\\\"", "\"")
                : value;
        }

        private string FormatCustomMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return message;

            string mapName = CurrentMap?.Info?.Description ?? string.Empty;
            string location = $"{CurrentLocation.X}:{CurrentLocation.Y}";
            string monsterName = MonsterInfo?.MonsterName ?? Name ?? string.Empty;

            return message
                .Replace("{MapName}", mapName, StringComparison.OrdinalIgnoreCase)
                .Replace("{Map}", mapName, StringComparison.OrdinalIgnoreCase)
                .Replace("{Location}", location, StringComparison.OrdinalIgnoreCase)
                .Replace("{X}", CurrentLocation.X.ToString(), StringComparison.OrdinalIgnoreCase)
                .Replace("{Y}", CurrentLocation.Y.ToString(), StringComparison.OrdinalIgnoreCase)
                .Replace("{Monster}", monsterName, StringComparison.OrdinalIgnoreCase)
                .Replace("{Name}", monsterName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetActionMapRegion(CustomMonsterAiActionInfo action, out MapRegion region)
        {
            region = null;
            if (action == null) return false;

            string specialRegion = GetActionOptionValue(action, "SpecialRegion");
            if (!string.IsNullOrWhiteSpace(specialRegion))
            {
                switch (specialRegion.Trim().ToUpperInvariant())
                {
                    case "MYSTERYSHIP":
                    case "MYSTERY_SHIP":
                    case "NETHERWORLD":
                    case "NETHERWORLDGATE":
                        region = SEnvir.MysteryShipMapRegion;
                        return region != null;
                    case "LAIR":
                    case "JINAM":
                    case "JINAMSTONE":
                    case "JINAMSTONEGATE":
                        region = SEnvir.LairMapRegion;
                        return region != null;
                }
            }

            if (TryGetRegionByIndex(GetActionOptionValue(action, "RegionIndex"), out region) ||
                TryGetRegionByIndex(GetActionOptionValue(action, "Region"), out region))
                return true;

            string regionName = GetActionOptionValue(action, "RegionDescription");
            if (string.IsNullOrWhiteSpace(regionName))
                regionName = GetActionOptionValue(action, "RegionName");
            if (string.IsNullOrWhiteSpace(regionName))
                regionName = GetActionOptionValue(action, "Region");

            if (string.IsNullOrWhiteSpace(regionName)) return false;

            region = SEnvir.MapRegionList.Binding.FirstOrDefault(x =>
                x?.Description.Equals(regionName, StringComparison.OrdinalIgnoreCase) == true ||
                x?.ServerDescription.Equals(regionName, StringComparison.OrdinalIgnoreCase) == true);

            return region != null;
        }

        private static bool TryGetRegionByIndex(string value, out MapRegion region)
        {
            region = null;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)) return false;

            region = SEnvir.MapRegionList.Binding.FirstOrDefault(x => x.Index == index);
            return region != null;
        }

        private static bool TeleportToRegionPoint(MapObject ob, Map map, MapRegion region)
        {
            if (ob == null || map == null || region == null) return false;

            if (region.PointList == null)
                region.CreatePoints(map.Width);

            if (region.PointList == null || region.PointList.Count == 0) return false;

            for (int i = 0; i < 20; i++)
            {
                Point point = region.PointList[SEnvir.Random.Next(region.PointList.Count)];
                if (ob.Teleport(map, point)) return true;
            }

            return false;
        }

        private IEnumerable<MonsterObject> GetTrackedMinions(bool includeDead)
        {
            HashSet<MonsterObject> seen = new();

            foreach (MonsterObject minion in MinionList)
            {
                if (minion == null || !includeDead && minion.Dead) continue;
                if (seen.Add(minion)) yield return minion;
            }

            if (_deathContextMinions == null) yield break;

            foreach (MonsterObject minion in _deathContextMinions)
            {
                if (minion == null || !includeDead && minion.Dead) continue;
                if (seen.Add(minion)) yield return minion;
            }
        }

        private bool CastSkill(CustomMonsterSkillInfo entry, CustomSkillInfo skill, MapObject target, Point location, int range)
        {
            if (entry?.Magic == null || skill == null) return false;

            MirDirection castDirection = GetSkillPreviewDirection(target, location);
            Direction = castDirection;

            List<uint> targets = new();
            List<Point> locations = new();
            object payload = target == null ? null : CreateSkillTargetPayload(target, range);

            switch (skill.Template)
            {
                case CustomSkillTemplate.ChainDamage:
                    if (target == null) return false;
                    SkillChainPayload chainPayload = BuildChainPayload(skill, target);
                    if (chainPayload.Targets.Count == 0) return false;
                    payload = chainPayload;
                    targets.AddRange(chainPayload.Targets.Select(x => x.ObjectID));
                    break;
                case CustomSkillTemplate.AreaDamage:
                case CustomSkillTemplate.PersistentAreaDamage:
                    payload = CreateSkillAreaPayload(skill, target, location, castDirection, range);
                    locations.Add(((SkillAreaPayload)payload).Location);
                    break;
                case CustomSkillTemplate.Heal:
                case CustomSkillTemplate.Buff:
                    if (target == null) return false;
                    payload = CreateSkillTargetPayload(target, range);
                    targets.Add(target.ObjectID);
                    break;
                case CustomSkillTemplate.Summon:
                    Point summonLocation = Functions.Move(CurrentLocation, Direction, -1);
                    payload = new SkillLocationPayload { Map = CurrentMap, Location = summonLocation, Origin = CurrentLocation };
                    locations.Add(summonLocation);
                    break;
                case CustomSkillTemplate.DamageProxy:
                    if (target == null) return false;
                    Point proxyLocation = Functions.Move(CurrentLocation, Direction, 1);
                    payload = new SkillDamageProxyPayload { Map = CurrentMap, Target = target, Location = proxyLocation, Origin = CurrentLocation };
                    targets.Add(target.ObjectID);
                    locations.Add(proxyLocation);
                    break;
                case CustomSkillTemplate.Move:
                    payload = new SkillLocationPayload { Map = CurrentMap, Location = location, Origin = CurrentLocation, Range = range };
                    locations.Add(location);
                    break;
                default:
                    if (target == null) return false;
                    payload = ShouldCaptureOffensiveSelfAreaPayload(skill, target)
                        ? CreateSkillAreaPayload(skill, target, location, castDirection, range)
                        : CreateSkillTargetPayload(target, range);
                    targets.Add(target.ObjectID);
                    if (payload is SkillAreaPayload areaPayload)
                        locations.Add(areaPayload.Location);
                    break;
            }

            Broadcast(new S.ObjectMagic
            {
                ObjectID = ObjectID,
                Direction = Direction,
                CurrentLocation = CurrentLocation,
                Type = entry.Magic.Magic,
                MagicInfoIndex = entry.Magic.Index,
                Targets = targets,
                Locations = locations,
                Cast = true,
                AttackElement = skill.Element,
                AnimationVariant = entry.AnimationVariant
            });

            UpdateAttackTime();
            ActionList.Add(new DelayedAction(GetSkillDelay(skill, payload), ActionType.DelayMagic, entry.Magic.Magic, entry.Magic.Index, skill, payload));
            return true;
        }

        private void CompleteSkill(CustomSkillInfo skill, object payload, int remainingTicks = 0)
        {
            bool applied = false;

            switch (skill.Template)
            {
                case CustomSkillTemplate.ChainDamage:
                    if (payload is SkillChainPayload chainPayload)
                        ApplyChainDamage(skill, chainPayload);
                    else
                        ApplyDamage(skill, GetSkillPayloadTarget(payload));
                    applied = true;
                    break;
                case CustomSkillTemplate.DirectDamage:
                case CustomSkillTemplate.ProjectileDamage:
                    if (!TryApplyTargetAreaSkill(skill, payload))
                        ApplyDamage(skill, GetSkillPayloadTarget(payload));
                    applied = true;
                    break;
                case CustomSkillTemplate.Debuff:
                case CustomSkillTemplate.Control:
                    if (!TryApplyTargetAreaSkill(skill, payload))
                    {
                        MapObject target = GetSkillPayloadTarget(payload, out Point? origin);
                        ApplySkillStatus(skill, target, helpful: false);
                        ApplySkillMovement(skill, target, origin);
                    }
                    applied = true;
                    break;
                case CustomSkillTemplate.AreaDamage:
                    if (payload is SkillAreaPayload area)
                        ApplyAreaDamage(skill, area);
                    else if (payload is Point point)
                        ApplyAreaDamage(skill, CreateSkillAreaPayload(skill, null, point));
                    applied = true;
                    break;
                case CustomSkillTemplate.PersistentAreaDamage:
                    if (payload is SkillAreaPayload persistentArea)
                        ApplyPersistentAreaDamage(skill, persistentArea, remainingTicks <= 0 ? GetPersistentAreaTickCount(skill) : remainingTicks);
                    else if (payload is Point persistentPoint)
                        ApplyPersistentAreaDamage(skill, CreateSkillAreaPayload(skill, null, persistentPoint), remainingTicks <= 0 ? GetPersistentAreaTickCount(skill) : remainingTicks);
                    break;
                case CustomSkillTemplate.Heal:
                    if (!TryApplyTargetAreaSkill(skill, payload))
                        ApplySkillHeal(skill, GetSingleTargetOrSelf(payload));
                    applied = true;
                    break;
                case CustomSkillTemplate.Buff:
                    if (!TryApplyTargetAreaSkill(skill, payload))
                        ApplySkillStatus(skill, GetSingleTargetOrSelf(payload), helpful: true);
                    applied = true;
                    break;
                case CustomSkillTemplate.Summon:
                    if (payload is SkillLocationPayload summonPayload)
                        SpawnCustomMinion(skill, summonPayload);
                    else if (payload is Point location)
                        SpawnCustomMinion(skill, new SkillLocationPayload { Map = CurrentMap, Location = location, Origin = CurrentLocation });
                    break;
                case CustomSkillTemplate.DamageProxy:
                    if (payload is SkillDamageProxyPayload proxyPayload)
                        SpawnDamageProxyMinion(skill, proxyPayload);
                    break;
                case CustomSkillTemplate.Move:
                    if (payload is SkillLocationPayload movePayload)
                    {
                        ApplySkillMove(skill, movePayload);
                        applied = true;
                    }
                    else if (payload is Point moveLocation)
                    {
                        ApplySkillMove(skill, new SkillLocationPayload { Map = CurrentMap, Location = moveLocation, Origin = CurrentLocation });
                        applied = true;
                    }
                    break;
            }

            if (applied)
                QueueNextSkillWave(skill, payload, remainingTicks <= 0 ? GetSkillWaveCount(skill) : remainingTicks);
        }

        private SkillChainPayload BuildChainPayload(CustomSkillInfo skill, MapObject primaryTarget)
        {
            List<MapObject> targets = new();
            if (skill == null || primaryTarget?.Node == null || !CanAttackTarget(primaryTarget))
                return new SkillChainPayload { Map = CurrentMap, Origin = CurrentLocation, Targets = targets };

            targets.Add(primaryTarget);

            int maxTargets = GetMaxChainTargets(skill);
            int jumpRadius = GetChainRadius(skill);
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
                .Where(x => !usedTargets.Contains(x) && CanAttackTarget(x))
                .OrderBy(x => Functions.Distance(previous.CurrentLocation, x.CurrentLocation))
                .ThenBy(x => Functions.Distance(CurrentLocation, x.CurrentLocation))
                .FirstOrDefault();
        }

        private void ApplyChainDamage(CustomSkillInfo skill, SkillChainPayload payload)
        {
            if (payload?.Targets == null) return;
            Map map = payload.Map ?? CurrentMap;
            if (map == null || CurrentMap != map) return;

            foreach (MapObject target in payload.Targets.ToArray())
            {
                if (target?.CurrentMap != map) continue;
                ApplyDamage(skill, target);
            }
        }

        private bool TryApplyTargetAreaSkill(CustomSkillInfo skill, object payload)
        {
            if (skill == null || GetSkillSearchRadius(skill) <= 0 || CurrentMap == null) return false;

            Point? center;
            MirDirection direction;
            SkillTargetPayload targetPayload = payload as SkillTargetPayload;
            MapObject payloadTarget = null;
            Point? payloadOrigin = null;

            if (targetPayload != null)
            {
                payloadTarget = GetSkillPayloadTarget(targetPayload, out payloadOrigin);
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
                    _ when IsHelpfulSkill(skill) => CurrentLocation,
                    _ => null,
                };

                if (center == null) return false;

                direction = payloadTarget?.Node != null && !ReferenceEquals(payloadTarget, this)
                    ? Functions.DirectionFromPoint(payloadOrigin ?? CurrentLocation, payloadTarget.CurrentLocation)
                    : GetSkillDirection(center.Value);
            }

            SkillAreaPayload payloadArea = payload as SkillAreaPayload;
            Map map = payloadArea?.Map ?? targetPayload?.Map ?? CurrentMap;
            Point origin = payloadArea?.Origin ?? payloadOrigin ?? CurrentLocation;
            if (map == null || CurrentMap != map) return false;
            int payloadRange = payloadArea?.Range > 0 ? payloadArea.Range : targetPayload?.Range > 0 ? targetPayload.Range : GetSkillRange(skill);
            if (!Functions.InRange(origin, center.Value, payloadRange)) return false;

            List<MapObject> targets = GetSkillAreaTargets(skill, center.Value, direction, origin, map);
            if (targets.Count == 0) return false;

            foreach (MapObject target in targets)
                ApplySkillTemplateToTarget(skill, target, origin);

            return true;
        }

        private SkillAreaPayload CreateSkillAreaPayload(CustomSkillInfo skill, MapObject target, Point location, MirDirection? direction = null, int? range = null)
        {
            return new SkillAreaPayload
            {
                Map = CurrentMap,
                Location = location,
                Origin = CurrentLocation,
                Range = Math.Max(1, range ?? GetSkillRange(skill)),
                Direction = direction ??
                            (target?.Node != null && !ReferenceEquals(target, this)
                                ? Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation)
                                : GetSkillDirection(location)),
            };
        }

        private SkillTargetPayload CreateSkillTargetPayload(MapObject target, int range = 0)
        {
            return new SkillTargetPayload
            {
                Map = CurrentMap,
                Target = target,
                Origin = CurrentLocation,
                Range = range,
            };
        }

        private MapObject GetSkillPayloadTarget(object payload)
        {
            return GetSkillPayloadTarget(payload, out _);
        }

        private MapObject GetSkillPayloadTarget(object payload, out Point? origin)
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

        private MapObject GetSingleTargetOrSelf(object payload)
        {
            if (payload is SkillTargetPayload)
                return GetSkillPayloadTarget(payload);

            return GetSkillPayloadTarget(payload) ?? this;
        }

        private List<MapObject> GetSkillAreaTargets(CustomSkillInfo skill, Point center, MirDirection direction, Point? origin = null, Map map = null)
        {
            List<MapObject> targets = new();
            int radius = GetSkillSearchRadius(skill);
            bool helpful = IsHelpfulSkill(skill);
            map ??= CurrentMap;
            if (map == null) return targets;

            foreach (Cell cell in map.GetCells(center, 0, radius))
            {
                if (cell?.Objects == null) continue;

                foreach (MapObject ob in cell.Objects.ToArray())
                {
                    if (ob?.Node == null || ob.Dead || ob.CurrentMap != map) continue;
                    if (!IsLocationInSkillShape(skill, center, ob.CurrentLocation, direction, origin ?? CurrentLocation)) continue;

                    if (helpful)
                    {
                        if (!CanHelpTarget(ob)) continue;
                    }
                    else if (!CanAttackTarget(ob)) continue;

                    targets.Add(ob);
                }
            }

            return OrderTargetsBySelector(targets.Distinct(), helpful ? CustomAiTargetSelectorType.LowestHealthAllyInView : CustomAiTargetSelectorType.NearestInView)
                .Take(GetMaxSkillTargetsOrAll(skill))
                .ToList();
        }

        private void ApplySkillTemplateToTarget(CustomSkillInfo skill, MapObject target, Point? origin = null)
        {
            switch (skill.Template)
            {
                case CustomSkillTemplate.DirectDamage:
                case CustomSkillTemplate.ProjectileDamage:
                    ApplyDamage(skill, target);
                    break;
                case CustomSkillTemplate.Debuff:
                case CustomSkillTemplate.Control:
                    ApplySkillStatus(skill, target, helpful: false);
                    ApplySkillMovement(skill, target, origin);
                    break;
                case CustomSkillTemplate.Heal:
                    ApplySkillHeal(skill, target);
                    break;
                case CustomSkillTemplate.Buff:
                    ApplySkillStatus(skill, target, helpful: true);
                    break;
            }
        }

        private void ApplyAreaDamage(CustomSkillInfo skill, SkillAreaPayload payload)
        {
            if (payload == null) return;
            Map map = payload.Map ?? CurrentMap;
            if (map == null || CurrentMap != map) return;
            if (!Functions.InRange(payload.Origin, payload.Location, GetSkillPayloadRange(skill, payload))) return;

            int remainingTargets = GetMaxSkillTargetsOrAll(skill);

            foreach (Cell cell in map.GetCells(payload.Location, 0, GetSkillSearchRadius(skill)))
            {
                if (cell?.Objects == null) continue;

                for (int i = cell.Objects.Count - 1; i >= 0; i--)
                {
                    if (i >= cell.Objects.Count) continue;
                    MapObject target = cell.Objects[i];
                    if (target?.Node == null || target.Dead || target.CurrentMap != map) continue;
                    if (!IsLocationInSkillShape(skill, payload.Location, target.CurrentLocation, payload.Direction, payload.Origin)) continue;
                    if (!CanAttackTarget(target)) continue;

                    ApplyDamage(skill, target);
                    if (remainingTargets != int.MaxValue && --remainingTargets <= 0)
                        return;
                }
            }
        }

        private void ApplyPersistentAreaDamage(CustomSkillInfo skill, SkillAreaPayload payload, int remainingTicks)
        {
            if (remainingTicks <= 0) return;

            ApplyAreaDamage(skill, payload);

            if (remainingTicks <= 1 || skill.Magic == null) return;

            ActionList.Add(new DelayedAction(
                SEnvir.Now.AddMilliseconds(GetPersistentAreaTickRate(skill)),
                ActionType.DelayMagic,
                skill.Magic.Magic,
                skill.Magic.Index,
                skill,
                payload,
                remainingTicks - 1));
        }

        private void ApplyDamage(CustomSkillInfo skill, MapObject target)
        {
            if (target?.Node == null || target.Dead || target.CurrentMap != CurrentMap || !CanAttackTarget(target)) return;

            int power = GetSkillPower(skill);
            Element element = skill.Element;

            if (!IgnoreArmour && !skill.IgnoreArmour)
                power -= element == Element.None ? target.GetAC() : target.GetMR();

            int res = target.Stats.GetResistanceValue(element);
            if (res > 0)
                power -= power * res / 10;
            else if (res < 0)
                power -= power * res / 5;

            if (power <= 0)
            {
                target.Blocked();
                return;
            }

            target.Attacked(this, power, element, true, IgnoreShield || skill.IgnoreShield);
        }

        private void ApplySkillHeal(CustomSkillInfo skill, MapObject target)
        {
            if (target?.Node == null || target.Dead || target.CurrentMap != CurrentMap || !CanHelpTarget(target)) return;

            int maxHealth = target.Stats[Stat.Health];
            if (maxHealth <= 0 || target.CurrentHP >= maxHealth) return;

            target.ChangeHP(Math.Min(GetSkillPower(skill), maxHealth - target.CurrentHP));
        }

        private void ApplySkillStatus(CustomSkillInfo skill, MapObject target, bool helpful)
        {
            if (target?.Node == null || target.Dead) return;
            if (target.CurrentMap != CurrentMap) return;
            if (helpful && target != this && !CanHelpTarget(target)) return;
            if (!helpful && !CanAttackTarget(target)) return;

            TimeSpan duration = GetSkillDuration(skill);
            TimeSpan tickRate = TimeSpan.FromMilliseconds(Math.Max(0, skill.TickRate));

            if (skill.BuffType != BuffType.None)
            {
                target.BuffAdd(skill.BuffType, duration, CloneStats(skill.BuffStats), skill.BuffVisible, skill.BuffPause, tickRate, skill.BuffHidden, skill.BuffExtra);
                return;
            }

            if (!helpful)
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
            }
        }

        private void ApplySkillMovement(CustomSkillInfo skill, MapObject target, Point? origin = null)
        {
            if (skill == null || target?.Node == null || target == this || target.CurrentMap != CurrentMap || !CanAttackTarget(target)) return;

            Point source = origin ?? CurrentLocation;

            if (skill.KnockbackDistance > 0)
            {
                MirDirection direction = Functions.DirectionFromPoint(source, target.CurrentLocation);
                target.Pushed(direction, skill.KnockbackDistance);
            }

            if (skill.PullDistance > 0)
            {
                MirDirection direction = Functions.DirectionFromPoint(target.CurrentLocation, source);
                target.Pushed(direction, skill.PullDistance);
            }
        }

        private void ApplySkillMove(CustomSkillInfo skill, SkillLocationPayload payload)
        {
            if (skill == null || payload == null || !CanMove) return;
            Map map = payload.Map ?? CurrentMap;
            if (map == null || CurrentMap != map) return;
            if (!Functions.InRange(payload.Origin, payload.Location, payload.Range > 0 ? payload.Range : GetSkillRange(skill))) return;

            Cell cell = map.GetCell(payload.Location);
            if (cell == null || cell.Movements != null || cell.IsBlocking(this, false)) return;

            Teleport(map, payload.Location, true);
        }

        private int GetSkillPower(CustomSkillInfo skill)
        {
            int attack = skill.PowerSource switch
            {
                CustomSkillPowerSource.DC => GetDC(),
                CustomSkillPowerSource.MC => GetMC(),
                CustomSkillPowerSource.SC => GetSC(),
                CustomSkillPowerSource.Fixed => 0,
                _ => skill.Element switch
                {
                    Element.Holy or Element.Dark or Element.Phantom => GetSC(),
                    Element.None => GetDC(),
                    _ => GetMC(),
                },
            };

            return Math.Max(1, skill.FixedPower + attack * Math.Max(0, skill.PowerRate) / 100);
        }

        private TimeSpan GetSkillDuration(CustomSkillInfo skill)
        {
            int duration = Math.Max(0, skill.Duration);

            if (skill.DurationPowerRate > 0)
                duration += GetSkillPower(skill) * skill.DurationPowerRate;

            return TimeSpan.FromMilliseconds(Math.Max(1000, duration));
        }

        private static int GetMaxSkillTargetsOrAll(CustomSkillInfo skill)
        {
            return skill?.MaxTargets > 0 ? skill.MaxTargets : int.MaxValue;
        }

        private static int GetSkillRange(CustomSkillInfo skill)
        {
            return Math.Max(1, skill?.Range > 0 ? skill.Range : Globals.MagicRange);
        }

        private static int GetSkillPayloadRange(CustomSkillInfo skill, SkillAreaPayload payload)
        {
            return payload?.Range > 0 ? payload.Range : GetSkillRange(skill);
        }

        private int GetSkillSearchRadius(CustomSkillInfo skill)
        {
            if (skill == null) return 0;

            return skill.Shape switch
            {
                CustomSkillShape.Line or CustomSkillShape.Cone or CustomSkillShape.Rectangle => Math.Max(Math.Max(skill.Range, skill.Length), skill.Radius),
                _ => Math.Max(0, skill.Radius),
            };
        }

        private bool IsLocationInSkillShape(CustomSkillInfo skill, Point center, Point location, MirDirection direction, Point origin)
        {
            if (skill == null) return false;
            if (skill.Shape == CustomSkillShape.Circle)
                return Functions.Distance(center, location) <= Math.Max(0, skill.Radius);

            (double fx, double fy) = GetDirectionVector(direction);
            double vx = location.X - origin.X;
            double vy = location.Y - origin.Y;
            double forward = vx * fx + vy * fy;
            double side = Math.Abs(vx * -fy + vy * fx);
            double length = Math.Max(1, skill.Length > 0 ? skill.Length : Math.Max(skill.Range, skill.Radius));

            return skill.Shape switch
            {
                CustomSkillShape.Line => forward >= 0 && forward <= length && side <= Math.Max(0.5D, (skill.Width <= 0 ? 1 : skill.Width) / 2D),
                CustomSkillShape.Rectangle => forward >= 0 && forward <= length && side <= Math.Max(0.5D, (skill.Width <= 0 ? Math.Max(1, skill.Radius) : skill.Width) / 2D),
                CustomSkillShape.Cone => IsInsideSkillCone(skill, vx, vy, fx, fy, length),
                _ => true,
            };
        }

        private static bool IsInsideSkillCone(CustomSkillInfo skill, double vx, double vy, double fx, double fy, double length)
        {
            double distance = Math.Sqrt(vx * vx + vy * vy);
            if (distance <= 0 || distance > length) return false;

            double dot = (vx * fx + vy * fy) / distance;
            double minDot = Math.Cos((Math.Max(1, skill.Angle <= 0 ? 90 : skill.Angle) / 2D) * Math.PI / 180D);
            return dot >= minDot;
        }

        private MirDirection GetSkillDirection(Point center)
        {
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

        private static int GetSkillWaveCount(CustomSkillInfo skill)
        {
            return Math.Max(1, skill?.WaveCount > 0 ? skill.WaveCount : 1);
        }

        private void QueueNextSkillWave(CustomSkillInfo skill, object payload, int remainingWaves)
        {
            if (!SkillSupportsWaves(skill) || skill?.Magic == null || remainingWaves <= 1) return;

            ActionList.Add(new DelayedAction(
                SEnvir.Now.AddMilliseconds(Math.Max(100, skill.WaveInterval <= 0 ? skill.TickRate : skill.WaveInterval)),
                ActionType.DelayMagic,
                skill.Magic.Magic,
                skill.Magic.Index,
                skill,
                payload,
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
                or CustomSkillTemplate.Control
                or CustomSkillTemplate.Move;
        }

        private static int GetMaxChainTargets(CustomSkillInfo skill)
        {
            return Math.Max(1, skill?.MaxTargets > 0 ? skill.MaxTargets : 3);
        }

        private static int GetChainRadius(CustomSkillInfo skill)
        {
            return Math.Max(1, skill?.Radius > 0 ? skill.Radius : 4);
        }

        private DateTime GetSkillDelay(CustomSkillInfo skill, object payload, int fallbackDelay = 500)
        {
            int delay = Math.Max(0, skill?.CastDelay > 0 ? skill.CastDelay : fallbackDelay);

            if (skill != null && SkillUsesProjectileTravel(skill))
            {
                Point? targetLocation = payload switch
                {
                    SkillAreaPayload area => area.Location,
                    SkillTargetPayload targetPayload when targetPayload.Target?.Node != null => targetPayload.Target.CurrentLocation,
                    SkillLocationPayload locationPayload => locationPayload.Location,
                    MapObject target when target?.Node != null => target.CurrentLocation,
                    Point point => point,
                    _ => null,
                };

                if (targetLocation != null)
                    delay += Functions.Distance(CurrentLocation, targetLocation.Value) * Math.Max(0, skill.ProjectileSpeed > 0 ? skill.ProjectileSpeed : Globals.ProjectileSpeed);
            }

            return SEnvir.Now.AddMilliseconds(delay);
        }

        private static bool SkillUsesProjectileTravel(CustomSkillInfo skill)
        {
            return skill?.Template == CustomSkillTemplate.ProjectileDamage
                || skill?.Effects?.Any(x => x?.Stage == CustomSkillEffectStage.Projectile) == true;
        }

        private Stats BuildActionBuffStats(CustomMonsterAiActionInfo action, MapObject target)
        {
            Stats stats = CloneStats(action?.BuffStats) ?? new Stats();
            AddScaledBuffStats(stats, target, GetActionOptionValue(action, "BuffStatsFromTarget"));
            AddScaledBuffStats(stats, this, GetActionOptionValue(action, "BuffStatsFromSelf"));

            return stats.Count == 0 ? null : stats;
        }

        private static void AddScaledBuffStats(Stats stats, MapObject sourceTarget, string value)
        {
            if (stats == null || sourceTarget == null || string.IsNullOrWhiteSpace(value)) return;

            foreach (string token in value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!TryParseBuffStatScale(token, out Stat source, out Stat destination, out int rate)) continue;

                long amount = (long)sourceTarget.Stats[source] * Math.Max(0, rate) / 100L;
                if (amount == 0) continue;

                stats[destination] += amount > int.MaxValue ? int.MaxValue : (int)amount;
            }
        }

        private static bool TryParseBuffStatScale(string token, out Stat source, out Stat destination, out int rate)
        {
            source = Stat.None;
            destination = Stat.None;
            rate = 0;

            if (string.IsNullOrWhiteSpace(token)) return false;

            string[] rateParts = token.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (rateParts.Length != 2 && rateParts.Length != 3) return false;

            string sourceName;
            string destinationName;

            if (rateParts.Length == 3)
            {
                sourceName = rateParts[0];
                destinationName = rateParts[1];
            }
            else
            {
                string[] statParts = rateParts[0].Split('>', 2, StringSplitOptions.TrimEntries);
                if (statParts.Length != 2) return false;

                sourceName = statParts[0];
                destinationName = statParts[1];
            }

            return Enum.TryParse(sourceName, true, out source) &&
                   Enum.TryParse(destinationName, true, out destination) &&
                   source != Stat.None &&
                   destination != Stat.None &&
                   int.TryParse(rateParts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out rate) &&
                   rate >= 0;
        }

        private static Stats CloneStats(Stats stats)
        {
            return stats == null || stats.Count == 0 ? null : new Stats(stats);
        }

        private static int GetPersistentAreaTickRate(CustomSkillInfo skill)
        {
            return Math.Max(100, skill?.TickRate ?? 0);
        }

        private static int GetPersistentAreaTickCount(CustomSkillInfo skill)
        {
            int tickRate = GetPersistentAreaTickRate(skill);
            int duration = skill == null || skill.Duration <= 0 ? tickRate : skill.Duration;
            return Math.Max(1, (int)Math.Ceiling(duration / (double)tickRate));
        }

        private void SpawnCustomMinion(CustomSkillInfo skill, SkillLocationPayload payload)
        {
            if (payload == null || skill.SummonMonster == null || MinionList.Count >= Math.Max(1, skill.MaxSummons)) return;
            Map map = payload.Map ?? CurrentMap;
            if (map == null || CurrentMap != map) return;

            MonsterObject mob = GetMonster(skill.SummonMonster);
            if (mob == null) return;

            if (!SpawnMonsterNear(mob, map, payload.Location, 0, 3) &&
                !SpawnMonsterNear(mob, map, payload.Origin, 0, 3))
                return;

            mob.Target = Target;
            mob.Master = this;
            MinionList.Add(mob);
            RegisterCustomMinionLifetime(mob, skill.SummonDuration);
        }

        private static bool SpawnMonsterNear(MonsterObject mob, Map map, Point origin, int minRange, int maxRange)
        {
            if (mob == null || map == null) return false;

            foreach (Cell cell in map.GetCells(origin, minRange, maxRange, true))
            {
                if (cell == null || cell.Movements != null || cell.IsBlocking(mob, false)) continue;

                if (mob.Spawn(map, cell.Location))
                    return true;
            }

            return false;
        }

        private void SpawnDamageProxyMinion(CustomSkillInfo skill, SkillDamageProxyPayload payload)
        {
            if (skill.SummonMonster == null || payload?.Target?.Node == null || !CanAttackTarget(payload.Target)) return;
            Map map = payload.Map ?? CurrentMap;
            if (map == null || CurrentMap != map || payload.Target.CurrentMap != map) return;
            if (Doll.CursedList.Contains(payload.Target)) return;

            MonsterObject mob = GetMonster(skill.SummonMonster);
            if (mob is not CustomMonsterObject customProxy) return;

            int duration = skill.SummonDuration > 0 ? skill.SummonDuration : skill.Duration > 0 ? skill.Duration : 10000;
            if (!customProxy.ConfigureDamageProxy(this, payload.Target, TimeSpan.FromMilliseconds(Math.Max(1, duration)))) return;

            bool spawned = SpawnMonsterNear(customProxy, map, payload.Location, 0, 0) ||
                           SpawnMonsterNear(customProxy, map, payload.Origin, 0, 3);
            if (!spawned) return;

            customProxy.Target = payload.Target;
            customProxy.Master = this;
            MinionList.Add(customProxy);
            RegisterCustomMinionLifetime(customProxy, duration);
        }

        private void RegisterCustomMinionLifetime(MonsterObject minion, int duration)
        {
            if (minion == null || duration <= 0) return;

            _customMinionExpiry[minion] = SEnvir.Now.AddMilliseconds(duration);
        }

        private void ProcessCustomMinionLifetimes()
        {
            if (_customMinionExpiry.Count == 0) return;

            foreach (KeyValuePair<MonsterObject, DateTime> pair in _customMinionExpiry.ToList())
            {
                MonsterObject minion = pair.Key;

                if (minion?.Node == null || minion.Master != this)
                {
                    _customMinionExpiry.Remove(minion);
                    continue;
                }

                if (SEnvir.Now < pair.Value) continue;

                _customMinionExpiry.Remove(minion);
                minion.Despawn();
            }
        }

        private struct RuleConditionSpec
        {
            public CustomAiConditionType Condition { get; init; }
            public int Value { get; set; }
            public int Radius { get; set; }
            public BuffType BuffType { get; set; }
            public string CounterName { get; set; }
            public bool Invert { get; init; }
        }

        private sealed class SkillChainPayload
        {
            public Map Map { get; init; }
            public Point Origin { get; init; }
            public List<MapObject> Targets { get; init; }
        }

        private sealed class SkillAreaPayload
        {
            public Map Map { get; init; }
            public Point Location { get; init; }
            public Point Origin { get; init; }
            public int Range { get; init; }
            public MirDirection Direction { get; init; }
        }

        private sealed class SkillDamageProxyPayload
        {
            public Map Map { get; init; }
            public MapObject Target { get; init; }
            public Point Location { get; init; }
            public Point Origin { get; init; }
        }

        private sealed class SkillTargetPayload
        {
            public Map Map { get; init; }
            public MapObject Target { get; init; }
            public Point Origin { get; init; }
            public int Range { get; init; }
        }

        private sealed class SkillLocationPayload
        {
            public Map Map { get; init; }
            public Point Location { get; init; }
            public Point Origin { get; init; }
            public int Range { get; init; }
        }
    }
}
