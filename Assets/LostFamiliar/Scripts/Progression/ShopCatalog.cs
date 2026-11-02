using System;
using System.Collections.Generic;

namespace LostFamiliar.Core
{
    public enum ShopProductId
    {
        BeginnerPackage = 0,
        SmallGoldPouch = 1,
        LargeGoldChest = 2,
        DailyGold = 3,
        DailyGems = 4
    }

    public enum ShopPurchaseLimit { Unlimited, OncePerSave, DailyGold, DailyGems }

    public sealed class ShopProductDefinition
    {
        public ShopProductId Id { get; }
        public string Name { get; }
        public int GemCost { get; }
        public double GoldReward { get; }
        public int GemReward { get; }
        public ShopPurchaseLimit Limit { get; }

        internal ShopProductDefinition(ShopProductId id, string name, int gemCost,
            double goldReward, int gemReward, ShopPurchaseLimit limit)
        {
            Id = id;
            Name = name;
            GemCost = gemCost;
            GoldReward = goldReward;
            GemReward = gemReward;
            Limit = limit;
        }
    }

    public static class ShopCatalog
    {
        public static IReadOnlyList<ShopProductDefinition> Products { get; } = Array.AsReadOnly(new[]
        {
            new ShopProductDefinition(ShopProductId.BeginnerPackage, "초보자 지원 패키지", 0, 10000d, 500, ShopPurchaseLimit.OncePerSave),
            new ShopProductDefinition(ShopProductId.SmallGoldPouch, "작은 골드 주머니", 100, 10000d, 0, ShopPurchaseLimit.Unlimited),
            new ShopProductDefinition(ShopProductId.LargeGoldChest, "커다란 골드 상자", 800, 100000d, 0, ShopPurchaseLimit.Unlimited),
            new ShopProductDefinition(ShopProductId.DailyGold, "오늘의 골드", 0, 5000d, 0, ShopPurchaseLimit.DailyGold),
            new ShopProductDefinition(ShopProductId.DailyGems, "오늘의 보석", 0, 0d, 100, ShopPurchaseLimit.DailyGems)
        });

        public static bool TryGet(ShopProductId id, out ShopProductDefinition product)
        {
            for (int i = 0; i < Products.Count; i++)
            {
                if (Products[i].Id != id) continue;
                product = Products[i];
                return true;
            }
            product = null;
            return false;
        }
    }
}
