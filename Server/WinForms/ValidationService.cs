using Library;
using Library.SystemModels;
using MirDB;
using Server.Envir;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Server.WinForms
{
    public sealed class ValidationService
    {
        private readonly MirDbMetadata _metadata;

        public ValidationService(MirDbMetadata metadata)
        {
            _metadata = metadata;
        }

        public List<string> Validate()
        {
            List<string> messages = new List<string>();

            foreach (Type type in _metadata.ObjectTypes)
            {
                ValidateIdentityDuplicates(type, messages);
                ValidateReferences(type, messages);
            }

            ValidateBusinessRules(messages);

            return messages;
        }

        private void ValidateIdentityDuplicates(Type type, List<string> messages)
        {
            PropertyInfo[] identities = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => x.GetCustomAttribute<IsIdentityAttribute>() != null)
                .ToArray();

            if (identities.Length == 0) return;

            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object item in _metadata.GetBinding(type))
            {
                string key = string.Join("|", identities.Select(x => SafeGetText(x, item)));
                if (string.IsNullOrWhiteSpace(key.Replace("|", string.Empty))) continue;

                if (!keys.Add(key))
                    messages.Add($"{type.Name} 存在重复身份字段: {key}");
            }
        }

        private void ValidateReferences(Type type, List<string> messages)
        {
            foreach (object item in _metadata.GetBinding(type))
            {
                if (item is not DBObject owner) continue;

                foreach (PropertyInfo property in _metadata.GetValueProperties(type))
                {
                    if (!property.PropertyType.IsSubclassOf(typeof(DBObject))) continue;

                    object value;
                    try
                    {
                        value = property.GetValue(owner);
                    }
                    catch
                    {
                        continue;
                    }

                    if (value == null) continue;

                    IList targetList = _metadata.GetBinding(property.PropertyType);
                    if (!targetList.Contains(value))
                        messages.Add($"{type.Name} #{owner.Index} 的 {property.Name} 引用了不存在的 {property.PropertyType.Name}。");
                }
            }
        }

        private void ValidateBusinessRules(List<string> messages)
        {
            foreach (CurrencyInfo currency in GetObjects<CurrencyInfo>().Where(x => x.DropItem != null && x.DropItem.ItemType != ItemType.Currency))
                messages.Add($"{Describe(currency)} 的 DropItem 必须是 ItemType.Currency，当前为 {currency.DropItem.ItemType}: {Describe(currency.DropItem)}。");

            foreach (CastleInfo castle in GetObjects<CastleInfo>().Where(x => x.Monster != null && !IsCastleMonster(x.Monster)))
                messages.Add($"{Describe(castle)} 的 Monster 必须是 CastleObjective 或 CastleDefense，当前为 {castle.Monster.Flag}: {Describe(castle.Monster)}。");

            foreach (NPCInfo npc in GetObjects<NPCInfo>())
                ValidateRegion(npc, nameof(NPCInfo.Region), npc.Region, x => x.RegionType == RegionType.None || x.RegionType == RegionType.Npc, "None/Npc", messages);

            foreach (RespawnInfo respawn in GetObjects<RespawnInfo>())
                ValidateRegion(respawn, nameof(RespawnInfo.Region), respawn.Region, x => x.RegionType == RegionType.None || x.RegionType == RegionType.Spawn || x.RegionType == RegionType.SpawnConnection, "None/Spawn/SpawnConnection", messages);

            foreach (MovementInfo movement in GetObjects<MovementInfo>())
            {
                ValidateRegion(movement, nameof(MovementInfo.SourceRegion), movement.SourceRegion, IsMovementRegion, "None/Connection/SpawnConnection", messages);
                ValidateRegion(movement, nameof(MovementInfo.DestinationRegion), movement.DestinationRegion, IsMovementRegion, "None/Connection/SpawnConnection", messages);
            }

            foreach (MilestoneInfoTask task in GetObjects<MilestoneInfoTask>())
                ValidateRegion(task, nameof(MilestoneInfoTask.Region), task.Region, IsAreaRegion, "None/Area", messages);

            foreach (MineInfo mine in GetObjects<MineInfo>().Where(x => x.Region != null && x.Map != null && x.Region.Map != x.Map))
                messages.Add($"{Describe(mine)} 的 Region 必须属于同一个 Map，当前 Map={Describe(mine.Map)}，Region={Describe(mine.Region)}。");

            ValidateEventActions<WorldEventAction>(SEnvir.EventHandler?.GetWorldEventActions(), messages);
            ValidateEventActions<PlayerEventAction>(SEnvir.EventHandler?.GetPlayerEventActions(), messages);
            ValidateEventActions<MonsterEventAction>(SEnvir.EventHandler?.GetMonsterEventActions(), messages);

            ValidateEventActionRegions<WorldEventAction>(messages);
            ValidateEventActionRegions<PlayerEventAction>(messages);
            ValidateEventActionRegions<MonsterEventAction>(messages);
        }

        private IEnumerable<T> GetObjects<T>() where T : DBObject
        {
            try
            {
                return _metadata.GetBinding(typeof(T)).Cast<T>();
            }
            catch
            {
                return Enumerable.Empty<T>();
            }
        }

        private void ValidateRegion(DBObject owner, string propertyName, MapRegion region, Func<MapRegion, bool> filter, string allowedTypes, List<string> messages)
        {
            if (region == null || filter(region)) return;

            messages.Add($"{Describe(owner)} 的 {propertyName} 只允许区域类型 {allowedTypes}，当前为 {region.RegionType}: {Describe(region)}。");
        }

        private void ValidateEventActions<T>(IEnumerable<EventActionType> allowedActions, List<string> messages) where T : BaseEventAction
        {
            if (allowedActions == null) return;

            HashSet<EventActionType> allowed = new HashSet<EventActionType>(allowedActions);

            foreach (T action in GetObjects<T>().Where(x => !allowed.Contains(x.Type)))
                messages.Add($"{Describe(action)} 的 Type={action.Type} 不是 {typeof(T).Name} 支持的事件动作类型。");
        }

        private void ValidateEventActionRegions<T>(List<string> messages) where T : BaseEventAction
        {
            foreach (T action in GetObjects<T>())
                ValidateRegion(action, nameof(BaseEventAction.RegionParameter1), action.RegionParameter1, IsAreaRegion, "None/Area", messages);
        }

        private string Describe(DBObject value)
        {
            return $"{value.GetType().Name} #{value.Index} ({_metadata.GetDisplayText(value)})";
        }

        private static bool IsCastleMonster(MonsterInfo monster)
        {
            return monster.Flag == MonsterFlag.CastleObjective || monster.Flag == MonsterFlag.CastleDefense;
        }

        private static bool IsMovementRegion(MapRegion region)
        {
            return region.RegionType == RegionType.None || region.RegionType == RegionType.Connection || region.RegionType == RegionType.SpawnConnection;
        }

        private static bool IsAreaRegion(MapRegion region)
        {
            return region.RegionType == RegionType.None || region.RegionType == RegionType.Area;
        }

        private static string SafeGetText(PropertyInfo property, object item)
        {
            try
            {
                return property.GetValue(item)?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
