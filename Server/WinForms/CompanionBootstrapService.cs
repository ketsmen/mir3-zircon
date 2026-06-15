using Library;
using Library.SystemModels;
using MirDB;
using System.Linq;

namespace Server.WinForms
{
    public static class CompanionBootstrapService
    {
        public static int EnsureDefaultCurrency(Session session, bool saveChanges)
        {
            CurrencyInfo goldCurrency = session.GetCollection<CurrencyInfo>().Binding.FirstOrDefault(x => x.Type == CurrencyType.Gold);
            if (goldCurrency == null) return 0;

            int updated = 0;

            foreach (CompanionInfo companion in session.GetCollection<CompanionInfo>().Binding)
            {
                if (companion.Currency != null) continue;

                companion.Currency = goldCurrency;
                updated++;
            }

            if (updated > 0 && saveChanges)
                session.Save(true);

            return updated;
        }
    }
}
