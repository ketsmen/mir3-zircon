using Client.Controls;
using Client.Scenes;
using Client.Scenes.Views;
using Library;

namespace Client.Envir
{
    internal sealed class ItemSyncTarget
    {
        public DXItemCell Cell;
        public ClientUserItem[] ItemGrid;
        public int Slot;

        public ClientUserItem Item
        {
            get => Cell != null ? Cell.Item : ItemGrid[Slot];
            set
            {
                if (Cell != null)
                    Cell.Item = value;
                else
                    ItemGrid[Slot] = value;
            }
        }

        public void Unlock()
        {
            if (Cell != null) Cell.Locked = false;
        }

        public void ClearSelection()
        {
            if (Cell != null) Cell.Selected = false;
        }

        public void ClearLink()
        {
            if (Cell != null) Cell.Link = null;
        }

        public void Refresh()
        {
            Cell?.RefreshItem();
        }
    }

    internal static class ItemSync
    {
        public static bool IsHeroGrid(GridType grid)
        {
            return Functions.IsHeroGrid(grid);
        }

        public static int NormalizeHeroIndex(int heroIndex)
        {
            if (heroIndex > 0) return heroIndex;

            return GameScene.Game?.CurrentHeroIndex ?? GameScene.Game?.CurrentHero?.Index ?? 0;
        }

        public static bool TryGetHeroItemArray(GridType grid, int heroIndex, out ClientUserItem[] itemGrid, out bool visible)
        {
            itemGrid = null;
            visible = false;

            if (!IsHeroGrid(grid)) return false;

            heroIndex = NormalizeHeroIndex(heroIndex);
            if (heroIndex <= 0) return false;

            HeroDialog heroBox = GameScene.Game?.HeroBox;
            if (heroBox != null && heroBox.IsDisplayingHero(heroIndex))
            {
                visible = true;
                return heroBox.TryGetDisplayedHeroGrid(grid, out itemGrid);
            }

            if (heroIndex != (GameScene.Game?.CurrentHeroIndex ?? 0)) return false;

            itemGrid = GetCurrentHeroGrid(grid);
            return itemGrid != null;
        }

        public static bool TryGetCell(GridType grid, int slot, out DXItemCell cell)
        {
            cell = null;

            DXItemCell[] cells;

            switch (grid)
            {
                case GridType.Inventory:
                    cells = GameScene.Game.InventoryBox.Grid.Grid;
                    break;
                case GridType.Equipment:
                    cells = GameScene.Game.CharacterBox.Grid;
                    break;
                case GridType.Storage:
                    cells = GameScene.Game.StorageBox.Grid.Grid;
                    break;
                case GridType.PartsStorage:
                    cells = GameScene.Game.StorageBox.PartGrid.Grid;
                    break;
                case GridType.GuildStorage:
                    cells = GameScene.Game.GuildBox.StorageGrid.Grid;
                    break;
                case GridType.CompanionInventory:
                    cells = GameScene.Game.CompanionBox.InventoryGrid.Grid;
                    break;
                case GridType.CompanionEquipment:
                    cells = GameScene.Game.CompanionBox.EquipmentGrid;
                    break;
                case GridType.HeroInventory:
                    cells = GameScene.Game.HeroBox.InventoryGrid.Grid;
                    break;
                case GridType.HeroEquipment:
                    cells = GameScene.Game.HeroBox.EquipmentGrid;
                    break;
                default:
                    return false;
            }

            if (cells == null || slot < 0 || slot >= cells.Length) return false;

            cell = cells[slot];
            return cell != null;
        }

        public static bool TryGetTarget(GridType grid, int slot, int heroIndex, out ItemSyncTarget target)
        {
            target = null;

            if (IsHeroGrid(grid))
            {
                heroIndex = NormalizeHeroIndex(heroIndex);
                if (heroIndex <= 0) return false;

                HeroDialog heroBox = GameScene.Game?.HeroBox;
                if (heroBox != null && heroBox.IsDisplayingHero(heroIndex))
                {
                    if (!TryGetCell(grid, slot, out DXItemCell cell)) return false;

                    target = new ItemSyncTarget { Cell = cell, Slot = slot };
                    return true;
                }

                if (heroIndex != (GameScene.Game?.CurrentHeroIndex ?? 0)) return false;

                ClientUserItem[] itemGrid = GetCurrentHeroGrid(grid);
                if (itemGrid == null || slot < 0 || slot >= itemGrid.Length) return false;

                target = new ItemSyncTarget { ItemGrid = itemGrid, Slot = slot };
                return true;
            }

            if (!TryGetCell(grid, slot, out DXItemCell normalCell)) return false;

            target = new ItemSyncTarget { Cell = normalCell, Slot = slot };
            return true;
        }

        public static void RefreshHeroItems(int heroIndex, bool fullRefresh)
        {
            heroIndex = NormalizeHeroIndex(heroIndex);
            GameScene.Game?.HeroBox?.RefreshHeroItems(heroIndex, fullRefresh);
        }

        private static ClientUserItem[] GetCurrentHeroGrid(GridType grid)
        {
            ClientUserHero hero = GameScene.Game?.CurrentHero;
            if (hero == null) return null;

            return grid switch
            {
                GridType.HeroInventory => hero.InventoryArray,
                GridType.HeroEquipment => hero.EquipmentArray,
                _ => null
            };
        }
    }
}
