using Client.Scenes;
using Library;
using System.Collections.Generic;
using S = Library.Network.ServerPackets;

namespace Client.Models
{
    public sealed class HeroObject : PlayerObject
    {
        public override ObjectType Race => ObjectType.Hero;

        public string OwnerName;

        public HeroObject(S.ObjectHero info)
        {
            CharacterIndex = info.Index;
            ObjectID = info.ObjectID;

            Name = info.Name;
            OwnerName = info.OwnerName;
            Caption = info.Caption;
            NameColour = info.NameColour;

            Class = info.Class;
            Gender = info.Gender;

            Poison = info.Poison;

            if (info.Buffs != null)
            {
                foreach (BuffType type in info.Buffs.Keys)
                {
                    if (!VisibleBuffs.ContainsKey(type))
                        VisibleBuffs[type] = 0;

                    VisibleBuffs[type] = info.Buffs[type];
                }
            }

            Title = string.IsNullOrEmpty(info.Caption) ? info.OwnerName : info.Caption;

            CurrentLocation = info.Location;
            Direction = info.Direction;

            HairType = info.HairType;
            HairColour = info.HairColour;

            ArmourShape = info.Armour;
            ArmourColour = info.ArmourColour;

            CostumeShape = info.Costume;

            LibraryWeaponShape = info.Weapon;

            HorseShape = 0;

            HelmetShape = info.Helmet;
            ShieldShape = info.Shield;
            HideHead = info.HideHead;

            ArmourEffect = info.ArmourEffect;
            EmblemEffect = info.EmblemEffect;
            WeaponEffect = info.WeaponEffect;
            ShieldEffect = info.ShieldEffect;

            Light = info.Light;
            Dead = info.Dead;
            Horse = HorseType.None;

            UpdateLibraries();

            SetFrame(new ObjectAction(!Dead ? MirAction.Standing : MirAction.Dead, MirDirection.Up, CurrentLocation));

            GameScene.Game.MapControl.AddObject(this);
        }
    }
}
