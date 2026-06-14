using Library;
using MirDB;
using Server.Envir;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Server.DBModels
{
    [UserObject]
    public sealed class HeroInfo : DBObject
    {
        [Association("Heroes")]
        public CharacterInfo Character
        {
            get { return _Character; }
            set
            {
                if (_Character == value) return;

                var oldValue = _Character;
                _Character = value;

                OnChanged(oldValue, value, "Character");
            }
        }
        private CharacterInfo _Character;

        [Association("CurrentHero")]
        public CharacterInfo CurrentCharacter
        {
            get { return _CurrentCharacter; }
            set
            {
                if (_CurrentCharacter == value) return;

                var oldValue = _CurrentCharacter;
                _CurrentCharacter = value;

                OnChanged(oldValue, value, "CurrentCharacter");
            }
        }
        private CharacterInfo _CurrentCharacter;

        public string Name
        {
            get { return _Name; }
            set
            {
                if (_Name == value) return;

                string oldValue = _Name;
                _Name = value;

                OnChanged(oldValue, value, "Name");
            }
        }
        private string _Name;

        public MirClass Class
        {
            get { return _Class; }
            set
            {
                if (_Class == value) return;

                MirClass oldValue = _Class;
                _Class = value;

                OnChanged(oldValue, value, "Class");
            }
        }
        private MirClass _Class;

        public MirGender Gender
        {
            get { return _Gender; }
            set
            {
                if (_Gender == value) return;

                MirGender oldValue = _Gender;
                _Gender = value;

                OnChanged(oldValue, value, "Gender");
            }
        }
        private MirGender _Gender;

        public int Level
        {
            get { return _Level; }
            set
            {
                if (_Level == value) return;

                int oldValue = _Level;
                _Level = value;

                OnChanged(oldValue, value, "Level");
            }
        }
        private int _Level = 1;

        public decimal Experience
        {
            get { return _Experience; }
            set
            {
                if (_Experience == value) return;

                decimal oldValue = _Experience;
                _Experience = value;

                OnChanged(oldValue, value, "Experience");
            }
        }
        private decimal _Experience;

        public int CurrentHP
        {
            get { return _CurrentHP; }
            set
            {
                if (_CurrentHP == value) return;

                int oldValue = _CurrentHP;
                _CurrentHP = value;

                OnChanged(oldValue, value, "CurrentHP");
            }
        }
        private int _CurrentHP;

        public int CurrentMP
        {
            get { return _CurrentMP; }
            set
            {
                if (_CurrentMP == value) return;

                int oldValue = _CurrentMP;
                _CurrentMP = value;

                OnChanged(oldValue, value, "CurrentMP");
            }
        }
        private int _CurrentMP;

        public DateTime ReviveTime
        {
            get { return _ReviveTime; }
            set
            {
                if (_ReviveTime == value) return;

                DateTime oldValue = _ReviveTime;
                _ReviveTime = value;

                OnChanged(oldValue, value, "ReviveTime");
            }
        }
        private DateTime _ReviveTime;

        public int HairType
        {
            get { return _HairType; }
            set
            {
                if (_HairType == value) return;

                int oldValue = _HairType;
                _HairType = value;

                OnChanged(oldValue, value, "HairType");
            }
        }
        private int _HairType;

        public Color HairColour
        {
            get { return _HairColour; }
            set
            {
                if (_HairColour == value) return;

                Color oldValue = _HairColour;
                _HairColour = value;

                OnChanged(oldValue, value, "HairColour");
            }
        }
        private Color _HairColour;

        public byte Grade
        {
            get { return _Grade; }
            set
            {
                if (_Grade == value) return;

                byte oldValue = _Grade;
                _Grade = value;

                OnChanged(oldValue, value, "Grade");
            }
        }
        private byte _Grade;

        public bool AutoPot
        {
            get { return _AutoPot; }
            set
            {
                if (_AutoPot == value) return;

                bool oldValue = _AutoPot;
                _AutoPot = value;

                OnChanged(oldValue, value, "AutoPot");
            }
        }
        private bool _AutoPot;

        public int HPItemIndex
        {
            get { return _HPItemIndex; }
            set
            {
                if (_HPItemIndex == value) return;

                int oldValue = _HPItemIndex;
                _HPItemIndex = value;

                OnChanged(oldValue, value, "HPItemIndex");
            }
        }
        private int _HPItemIndex;

        public int MPItemIndex
        {
            get { return _MPItemIndex; }
            set
            {
                if (_MPItemIndex == value) return;

                int oldValue = _MPItemIndex;
                _MPItemIndex = value;

                OnChanged(oldValue, value, "MPItemIndex");
            }
        }
        private int _MPItemIndex;

        public byte AutoHPPercent
        {
            get { return _AutoHPPercent; }
            set
            {
                if (_AutoHPPercent == value) return;

                byte oldValue = _AutoHPPercent;
                _AutoHPPercent = value;

                OnChanged(oldValue, value, "AutoHPPercent");
            }
        }
        private byte _AutoHPPercent = 50;

        public byte AutoMPPercent
        {
            get { return _AutoMPPercent; }
            set
            {
                if (_AutoMPPercent == value) return;

                byte oldValue = _AutoMPPercent;
                _AutoMPPercent = value;

                OnChanged(oldValue, value, "AutoMPPercent");
            }
        }
        private byte _AutoMPPercent = 50;

        public DateTime CreationDate
        {
            get { return _CreationDate; }
            set
            {
                if (_CreationDate == value) return;

                DateTime oldValue = _CreationDate;
                _CreationDate = value;

                OnChanged(oldValue, value, "CreationDate");
            }
        }
        private DateTime _CreationDate;

        public bool Deleted
        {
            get { return _Deleted; }
            set
            {
                if (_Deleted == value) return;

                bool oldValue = _Deleted;
                _Deleted = value;

                OnChanged(oldValue, value, "Deleted");
            }
        }
        private bool _Deleted;

        public int Slot
        {
            get { return _Slot; }
            set
            {
                if (_Slot == value) return;

                int oldValue = _Slot;
                _Slot = value;

                OnChanged(oldValue, value, "Slot");
            }
        }
        private int _Slot;

        [Association("Items", true)]
        public DBBindingList<UserItem> Items { get; set; }

        [Association("Magics", true)]
        public DBBindingList<UserMagic> Magics { get; set; }

        public ClientHeroInformation ToClientInformation()
        {
            return new ClientHeroInformation
            {
                Index = Index,
                Name = Name,
                Class = Class,
                Gender = Gender,
                Level = Math.Max(1, Level),
                Slot = Slot
            };
        }

        public ClientUserHero ToClientHero(Stats stats = null)
        {
            stats ??= new Stats();

            return new ClientUserHero
            {
                Index = Index,
                Name = Name,
                Class = Class,
                Gender = Gender,
                Level = Math.Max(1, Level),
                HairType = HairType,
                HairColour = HairColour,
                Grade = Grade,
                Experience = Experience,
                MaxExperience = Level < Globals.ExperienceList.Count ? Globals.ExperienceList[Level] : 0,
                CurrentHP = CurrentHP,
                CurrentMP = CurrentMP,
                MaxHP = stats[Stat.Health],
                MaxMP = stats[Stat.Mana],
                Stats = new Stats(stats),
                AutoPot = AutoPot,
                HPItemIndex = HPItemIndex,
                MPItemIndex = MPItemIndex,
                AutoHPPercent = AutoHPPercent,
                AutoMPPercent = AutoMPPercent,
                Items = Items?.Select(x => x.ToClientInfo()).ToList() ?? new List<ClientUserItem>(),
                Magics = Magics?.Select(x => x.ToClientInfo()).ToList() ?? new List<ClientUserMagic>(),
            };
        }

        protected override void OnDeleted()
        {
            Character = null;
            CurrentCharacter = null;

            base.OnDeleted();
        }

        public override string ToString()
        {
            return Name ?? string.Empty;
        }
    }
}
