using Library;
using Library.SystemModels;
using MirDB;
using System.Linq;

namespace Server.WinForms
{
    public static class CurrencyBootstrapService
    {
        public static bool EnsureDefaultCurrencies(Session session, bool saveChanges)
        {
            bool changed = false;

            ItemInfo goldItem = EnsureCurrencyItem(session, "Gold", 121, true, ref changed);
            ItemInfo famePointItem = EnsureCurrencyItem(session, "Fame Point", 4010, false, ref changed);
            ItemInfo contributionPointItem = EnsureCurrencyItem(session, "Contribution Point", 4012, false, ref changed);

            CurrencyInfo gold = EnsureCurrency(session, CurrencyType.Gold, "Gold", "Gold", CurrencyCategory.Basic, goldItem, ref changed);
            EnsureGoldImages(session, gold, ref changed);

            EnsureCurrency(session, CurrencyType.GameGold, "Game Gold", "GG", CurrencyCategory.Other, null, ref changed);
            EnsureCurrency(session, CurrencyType.HuntGold, "Hunt Gold", "HG", CurrencyCategory.Other, null, ref changed);
            EnsureCurrency(session, CurrencyType.FP, "Fame Point", "FP", CurrencyCategory.Player, famePointItem, ref changed);
            EnsureCurrency(session, CurrencyType.CP, "Contribution Point", "CP", CurrencyCategory.Player, contributionPointItem, ref changed);

            foreach (CurrencyInfo currency in session.GetCollection<CurrencyInfo>().Binding)
            {
                if (currency.DropItem == null || currency.DropItem.ItemType == ItemType.Currency) continue;

                currency.DropItem.ItemType = ItemType.Currency;
                changed = true;
            }

            if (changed && saveChanges)
                session.Save(true);

            return changed;
        }

        private static ItemInfo EnsureCurrencyItem(Session session, string name, int image, bool canDrop, ref bool changed)
        {
            ItemInfo item = session.GetCollection<ItemInfo>().Binding.FirstOrDefault(x => x.ItemName == name);
            bool created = item == null;
            if (item == null)
            {
                item = session.GetCollection<ItemInfo>().CreateNewObject();
                item.ItemName = name;
                item.StackSize = 25000;
                item.SellRate = 0;
                item.Image = image;
                item.CanDrop = canDrop;
                changed = true;
            }

            if (item.ItemType != ItemType.Currency)
            {
                item.ItemType = ItemType.Currency;
                changed = true;
            }

            if (created && item.ItemEffect != ItemEffect.None)
            {
                item.ItemEffect = ItemEffect.None;
                changed = true;
            }

            return item;
        }

        private static CurrencyInfo EnsureCurrency(Session session, CurrencyType type, string name, string abbreviation, CurrencyCategory category, ItemInfo dropItem, ref bool changed)
        {
            CurrencyInfo currency = session.GetCollection<CurrencyInfo>().Binding.FirstOrDefault(x => x.Type == type);
            if (currency == null)
            {
                currency = session.GetCollection<CurrencyInfo>().CreateNewObject();
                currency.Type = type;
                currency.Category = category;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(currency.Name))
            {
                currency.Name = name;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(currency.Abbreviation))
            {
                currency.Abbreviation = abbreviation;
                changed = true;
            }

            if (dropItem != null && currency.DropItem == null)
            {
                currency.DropItem = dropItem;
                changed = true;
            }

            return currency;
        }

        private static void EnsureGoldImages(Session session, CurrencyInfo gold, ref bool changed)
        {
            if (gold.Images.Count > 0) return;

            AddCurrencyImage(session, gold, 120, 0);
            AddCurrencyImage(session, gold, 121, 100);
            AddCurrencyImage(session, gold, 122, 200);
            AddCurrencyImage(session, gold, 123, 500);
            AddCurrencyImage(session, gold, 124, 1000);
            AddCurrencyImage(session, gold, 125, 1000000);
            AddCurrencyImage(session, gold, 126, 5000000);
            AddCurrencyImage(session, gold, 127, 10000000);
            changed = true;
        }

        private static void AddCurrencyImage(Session session, CurrencyInfo currency, int image, long amount)
        {
            CurrencyInfoImage currencyImage = session.GetCollection<CurrencyInfoImage>().CreateNewObject();
            currencyImage.Image = image;
            currencyImage.Amount = amount;
            currency.Images.Add(currencyImage);
        }
    }
}
