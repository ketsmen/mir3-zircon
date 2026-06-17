using Library;
using Library.SystemModels;
using Server.DBModels;
using Server.Envir;
using Server.Models.Monsters;
using System;
using System.Drawing;
using System.Linq;

namespace Server.Models.Magics
{
    [MagicType(MagicType.CursedDoll)]
    public class CursedDoll : MagicObject
    {
        protected override Element Element => Element.None;

        public CursedDoll(PlayerObject player, UserMagic magic) : base(player, magic)
        {

        }

        public override MagicCast MagicCast(MapObject target, Point location, MirDirection direction)
        {
            var response = new MagicCast
            {
                Ob = target
            };

            var info = SEnvir.MonsterInfoList.Binding.First(x => x.Flag == MonsterFlag.CursedDoll);

            if (!Player.CanAttackTarget(target) || Doll.CursedList.Contains(target) || (target is MonsterObject mon && mon.MonsterInfo.IsBoss) || target.Level > Player.Level + 2)
            {
                response.Cast = false;
                response.Ob = null;
                return response;
            }

            if (!Player.UseAmulet(1, 1))
            {
                response.Cast = false;
                response.Ob = null;
                return response;
            }

            var delay = SEnvir.Now.AddMilliseconds(500);

            ActionList.Add(new DelayedAction(delay, ActionType.DelayMagic, Type, target, CurrentMap, Functions.Move(CurrentLocation, direction, 1), info));

            return response;
        }

        public override void MagicComplete(params object[] data)
        {
            MapObject target = (MapObject)data[1];
            var map = (Map)data[2];
            var location = (Point)data[3];
            var info = (MonsterInfo)data[4];

            if (target?.Node == null) return;

            MonsterObject ob = MonsterObject.GetMonster(info);

            if (ob == null) return;

            TimeSpan duration = TimeSpan.FromSeconds(10 + Magic.Level * 5);

            if (ob is Doll doll)
            {
                doll.DollTarget = target;
                doll.Caster = Player;
                doll.AliveTime = SEnvir.Now.Add(duration);
            }
            else if (ob is CustomMonsterObject customDoll)
            {
                if (!customDoll.ConfigureDamageProxy(Player, target, duration))
                    return;
            }
            else
            {
                return;
            }

            Cell cell = map.GetCell(location);

            if (cell == null || cell.Movements != null || !ob.Spawn(map, location))
                ob.Spawn(CurrentMap, CurrentLocation);

            Player.LevelMagic(Magic);
        }
    }
}
