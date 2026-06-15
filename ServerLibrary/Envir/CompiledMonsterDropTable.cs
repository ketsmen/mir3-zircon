using Library.SystemModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Envir
{
    public sealed class CompiledMonsterDropTable
    {
        private readonly Dictionary<MonsterInfo, CompiledMonsterDrop> _drops;

        private CompiledMonsterDropTable(Dictionary<MonsterInfo, CompiledMonsterDrop> drops)
        {
            _drops = drops;
        }

        public static CompiledMonsterDropTable Build(IEnumerable<MonsterInfo> monsters, IReadOnlyDictionary<MonsterInfo, CompiledMonsterDrop> overrides = null)
        {
            Dictionary<MonsterInfo, CompiledMonsterDrop> drops = new();

            foreach (MonsterInfo monster in monsters)
            {
                if (overrides != null && overrides.TryGetValue(monster, out CompiledMonsterDrop overrideDrop))
                {
                    if (overrideDrop.HasDrops)
                        drops[monster] = overrideDrop;

                    continue;
                }

                CompiledDropEntry[] entries = monster.Drops
                    .Where(x => x?.Item != null && x.Chance > 0)
                    .Select(CompiledDropEntry.FromDropInfo)
                    .ToArray();

                CompiledDropGroup[] groups = monster.DropGroups
                    .Where(x => x?.Items != null && x.Parent == null && x.Chance > 0)
                    .Select(CompiledDropGroup.FromDropGroupInfo)
                    .Where(x => x.HasDrops)
                    .ToArray();

                if (entries.Length == 0 && groups.Length == 0) continue;

                drops[monster] = new CompiledMonsterDrop(entries, groups);
            }

            return new CompiledMonsterDropTable(drops);
        }

        public CompiledMonsterDrop Get(MonsterInfo monster)
        {
            if (monster == null) return CompiledMonsterDrop.Empty;

            return _drops.TryGetValue(monster, out CompiledMonsterDrop drop) ? drop : CompiledMonsterDrop.Empty;
        }
    }

    public sealed class CompiledMonsterDrop
    {
        public static readonly CompiledMonsterDrop Empty = new([], []);

        public CompiledMonsterDrop(CompiledDropEntry[] entries, CompiledDropGroup[] groups)
        {
            Entries = entries;
            Groups = groups;
        }

        public CompiledDropEntry[] Entries { get; }
        public CompiledDropGroup[] Groups { get; }
        public bool HasDrops => Entries.Length > 0 || Groups.Length > 0;
    }

    public readonly struct CompiledDropEntry
    {
        public CompiledDropEntry(ItemInfo item, int chance, int amount, int dropSet, bool partOnly, bool easterEvent, bool mir2Gold, int sequence = 0)
        {
            Item = item;
            Chance = chance;
            Amount = Math.Max(1, amount);
            DropSet = dropSet;
            PartOnly = partOnly;
            EasterEvent = easterEvent;
            Mir2Gold = mir2Gold;
            Sequence = sequence;
        }

        public ItemInfo Item { get; }
        public int Chance { get; }
        public int Amount { get; }
        public int DropSet { get; }
        public bool PartOnly { get; }
        public bool EasterEvent { get; }
        public bool Mir2Gold { get; }
        public int Sequence { get; }

        public static CompiledDropEntry FromDropInfo(DropInfo drop)
        {
            return new CompiledDropEntry(drop.Item, drop.Chance, drop.Amount, drop.DropSet, drop.PartOnly, drop.EasterEvent, drop.Mir2Gold);
        }
    }

    public sealed class CompiledDropGroup
    {
        public CompiledDropGroup(DropGroupMode mode, int chance, int dropSet, int sequence, bool easterEvent, CompiledDropGroupChild[] children)
        {
            Mode = mode;
            Chance = chance;
            DropSet = dropSet;
            Sequence = sequence;
            EasterEvent = easterEvent;
            Children = children;
        }

        public DropGroupMode Mode { get; }
        public int Chance { get; }
        public int DropSet { get; }
        public int Sequence { get; }
        public bool EasterEvent { get; }
        public CompiledDropGroupChild[] Children { get; }
        public bool HasDrops => Children.Length > 0;

        public static CompiledDropGroup FromDropGroupInfo(DropGroupInfo group)
        {
            IEnumerable<CompiledDropGroupChild> items = group.Items
                .Where(x => x?.Item != null && x.Chance > 0)
                .Select(x => CompiledDropGroupChild.FromEntry(
                    new CompiledDropEntry(x.Item, x.Chance, x.Amount, x.DropSet, x.PartOnly, x.EasterEvent, x.Mir2Gold, x.Sequence),
                    x.Sequence,
                    x.Index));

            IEnumerable<CompiledDropGroupChild> groups = group.Groups
                .Where(x => x?.Items != null && x.Chance > 0)
                .Select(x => new { Group = FromDropGroupInfo(x), Index = x.Index })
                .Where(x => x.Group.HasDrops)
                .Select(x => CompiledDropGroupChild.FromGroup(x.Group, x.Group.Sequence, x.Index));

            CompiledDropGroupChild[] children = items
                .Concat(groups)
                .OrderBy(x => x.SortSequence)
                .ThenBy(x => x.Index)
                .ToArray();

            return new CompiledDropGroup(group.Mode, group.Chance, group.DropSet, group.Sequence, group.EasterEvent, children);
        }
    }

    public sealed class CompiledDropGroupChild
    {
        private CompiledDropGroupChild(CompiledDropEntry entry, CompiledDropGroup group, int sequence, int index)
        {
            Entry = entry;
            Group = group;
            Sequence = sequence;
            Index = index;
        }

        public CompiledDropEntry Entry { get; }
        public CompiledDropGroup Group { get; }
        public int Sequence { get; }
        public int SortSequence => Sequence > 0 ? Sequence : Index;
        public int Index { get; }
        public bool IsGroup => Group != null;

        public static CompiledDropGroupChild FromEntry(CompiledDropEntry entry, int sequence, int index)
        {
            return new CompiledDropGroupChild(entry, null, sequence, index);
        }

        public static CompiledDropGroupChild FromGroup(CompiledDropGroup group, int sequence, int index)
        {
            return new CompiledDropGroupChild(default, group, sequence, index);
        }
    }
}
