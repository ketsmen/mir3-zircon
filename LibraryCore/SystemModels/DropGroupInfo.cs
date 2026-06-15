using MirDB;
using System.Text.Json.Serialization;

namespace Library.SystemModels
{
    public enum DropGroupMode
    {
        AllMatched,
        RandomOneMatched,
        FirstMatched,
    }

    public sealed class DropGroupInfo : DBObject
    {
        [IsIdentity]
        [Association("DropGroups")]
        public MonsterInfo Monster
        {
            get { return _Monster; }
            set
            {
                if (_Monster == value) return;

                var oldValue = _Monster;
                _Monster = value;

                OnChanged(oldValue, value, "Monster");
            }
        }
        private MonsterInfo _Monster;

        public DropGroupMode Mode
        {
            get { return _Mode; }
            set
            {
                if (_Mode == value) return;

                var oldValue = _Mode;
                _Mode = value;

                OnChanged(oldValue, value, "Mode");
            }
        }
        private DropGroupMode _Mode;

        public int Chance
        {
            get { return _Chance; }
            set
            {
                if (_Chance == value) return;

                var oldValue = _Chance;
                _Chance = value;

                OnChanged(oldValue, value, "Chance");
            }
        }
        private int _Chance;

        public int DropSet
        {
            get { return _DropSet; }
            set
            {
                if (_DropSet == value) return;

                var oldValue = _DropSet;
                _DropSet = value;

                OnChanged(oldValue, value, "DropSet");
            }
        }
        private int _DropSet;

        public int Sequence
        {
            get { return _Sequence; }
            set
            {
                if (_Sequence == value) return;

                var oldValue = _Sequence;
                _Sequence = value;

                OnChanged(oldValue, value, "Sequence");
            }
        }
        private int _Sequence;

        public bool EasterEvent
        {
            get { return _EasterEvent; }
            set
            {
                if (_EasterEvent == value) return;

                var oldValue = _EasterEvent;
                _EasterEvent = value;

                OnChanged(oldValue, value, "EasterEvent");
            }
        }
        private bool _EasterEvent;

        [Association("NestedDropGroups")]
        public DropGroupInfo Parent
        {
            get { return _Parent; }
            set
            {
                if (_Parent == value) return;

                var oldValue = _Parent;
                _Parent = value;

                OnChanged(oldValue, value, "Parent");
            }
        }
        private DropGroupInfo _Parent;

        [JsonIgnore]
        [Association("DropGroupItems", true)]
        public DBBindingList<DropGroupItemInfo> Items { get; set; }

        [JsonIgnore]
        [Association("NestedDropGroups", true)]
        public DBBindingList<DropGroupInfo> Groups { get; set; }

        protected internal override void OnCreated()
        {
            base.OnCreated();

            Chance = 1;
        }
    }

    public sealed class DropGroupItemInfo : DBObject
    {
        [IsIdentity]
        [Association("DropGroupItems")]
        public DropGroupInfo Group
        {
            get { return _Group; }
            set
            {
                if (_Group == value) return;

                var oldValue = _Group;
                _Group = value;

                OnChanged(oldValue, value, "Group");
            }
        }
        private DropGroupInfo _Group;

        [IsIdentity]
        [Association("DropGroupItemDrops")]
        public ItemInfo Item
        {
            get { return _Item; }
            set
            {
                if (_Item == value) return;

                var oldValue = _Item;
                _Item = value;

                OnChanged(oldValue, value, "Item");
            }
        }
        private ItemInfo _Item;

        public int Chance
        {
            get { return _Chance; }
            set
            {
                if (_Chance == value) return;

                var oldValue = _Chance;
                _Chance = value;

                OnChanged(oldValue, value, "Chance");
            }
        }
        private int _Chance;

        public int Amount
        {
            get { return _Amount; }
            set
            {
                if (_Amount == value) return;

                var oldValue = _Amount;
                _Amount = value;

                OnChanged(oldValue, value, "Amount");
            }
        }
        private int _Amount;

        public int Sequence
        {
            get { return _Sequence; }
            set
            {
                if (_Sequence == value) return;

                var oldValue = _Sequence;
                _Sequence = value;

                OnChanged(oldValue, value, "Sequence");
            }
        }
        private int _Sequence;

        public int DropSet
        {
            get { return _DropSet; }
            set
            {
                if (_DropSet == value) return;

                var oldValue = _DropSet;
                _DropSet = value;

                OnChanged(oldValue, value, "DropSet");
            }
        }
        private int _DropSet;

        public bool PartOnly
        {
            get { return _PartOnly; }
            set
            {
                if (_PartOnly == value) return;

                var oldValue = _PartOnly;
                _PartOnly = value;

                OnChanged(oldValue, value, "PartOnly");
            }
        }
        private bool _PartOnly;

        public bool EasterEvent
        {
            get { return _EasterEvent; }
            set
            {
                if (_EasterEvent == value) return;

                var oldValue = _EasterEvent;
                _EasterEvent = value;

                OnChanged(oldValue, value, "EasterEvent");
            }
        }
        private bool _EasterEvent;

        public bool Mir2Gold
        {
            get { return _Mir2Gold; }
            set
            {
                if (_Mir2Gold == value) return;

                var oldValue = _Mir2Gold;
                _Mir2Gold = value;

                OnChanged(oldValue, value, "Mir2Gold");
            }
        }
        private bool _Mir2Gold;

        protected internal override void OnCreated()
        {
            base.OnCreated();

            Chance = 1;
            Amount = 1;
        }
    }
}
