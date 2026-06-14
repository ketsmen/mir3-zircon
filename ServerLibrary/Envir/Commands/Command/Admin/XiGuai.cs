using Library;
using Server.Models;
using Server.Models.Monsters;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Server.Envir.Commands.Command.Admin
{
    class XiGuai : AbstractCommand<IAdminCommand>
    {
        private const int SearchRange = 100;
        private const int PullRange = 10;

        public override string VALUE => "吸怪";

        public override void Action(PlayerObject player)
        {
            Map map = player.CurrentMap;
            if (map == null) return;

            List<Cell> destinations = GetPullDestinations(map, player.CurrentLocation, PullRange);
            if (destinations.Count == 0)
            {
                player.Connection.ReceiveChat("吸怪失败：附近没有可用落点。", MessageType.System);
                return;
            }

            int moved = 0;

            for (int i = 0; i < map.Objects.Count; i++)
            {
                MapObject mapObject = map.Objects[i];
                if (mapObject.Race == ObjectType.Hero) continue;
                if (mapObject is not MonsterObject monster) continue;
                if (!CanPullMonster(monster, player.CurrentLocation)) continue;

                if (!TryPullMonster(monster, destinations)) continue;

                moved++;
                monster.ApplyPoison(new Poison
                {
                    Owner = monster,
                    Type = PoisonType.Paralysis,
                    TickFrequency = TimeSpan.FromSeconds(5),
                    TickCount = 1,
                });

                if (destinations.Count == 0) break;
            }

            player.Connection.ReceiveChat($"吸怪完成：{moved} 只。", MessageType.System);
        }

        private static bool CanPullMonster(MonsterObject monster, Point center)
        {
            if (monster?.Node == null || monster.Dead || !monster.Visible) return false;
            if (!Functions.InRange(monster.CurrentLocation, center, SearchRange)) return false;
            if (monster is Guard || monster is Puppet) return false;
            if (monster.PetOwner != null || monster.InSafeZone) return false;
            if (monster.Target != null) return false;
            if (monster.MonsterInfo == null || monster.MonsterInfo.IsBoss || !monster.MonsterInfo.CanPush) return false;

            return true;
        }

        private static List<Cell> GetPullDestinations(Map map, Point center, int range)
        {
            List<Cell> cells = new List<Cell>();

            int minX = Math.Max(0, center.X - range);
            int maxX = Math.Min(map.Width - 1, center.X + range);
            int minY = Math.Max(0, center.Y - range);
            int maxY = Math.Min(map.Height - 1, center.Y + range);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Cell cell = map.GetCell(x, y);
                    if (cell == null || cell.Movements != null) continue;

                    cells.Add(cell);
                }
            }

            return cells;
        }

        private static bool TryPullMonster(MonsterObject monster, List<Cell> destinations)
        {
            while (destinations.Count > 0)
            {
                int index = SEnvir.Random.Next(destinations.Count);
                Cell cell = destinations[index];

                if (cell.Movements != null)
                {
                    destinations.RemoveAt(index);
                    continue;
                }

                if (monster.Teleport(cell.Map, cell.Location))
                    return true;

                destinations.RemoveAt(index);
            }

            return false;
        }
    }
}
