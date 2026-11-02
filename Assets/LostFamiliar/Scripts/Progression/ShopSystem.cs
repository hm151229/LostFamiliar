using System;
using System.Globalization;

namespace LostFamiliar.Core
{
    public enum ShopPurchaseResult
    {
        Success,
        NotInitialized,
        UnknownProduct,
        AlreadyClaimed,
        DailyUnavailable,
        NotEnoughGems,
        InvalidSaveData,
        BalanceLimitExceeded
    }

    // Mutates the existing save only after every check succeeds. Persistence belongs to MainBattleLoop.
    public sealed class ShopSystem
    {
        private readonly GameSaveData _saveData;

        public ShopSystem(GameSaveData saveData) => _saveData = saveData;

        public ShopPurchaseResult GetPurchaseStatus(ShopProductId id, DateTime localNow) =>
            Evaluate(id, localNow, out _, out _, out _, out _);

        public ShopPurchaseResult TryPurchase(ShopProductId id, DateTime localNow)
        {
            ShopPurchaseResult result = Evaluate(id, localNow, out ShopProductDefinition product,
                out string today, out double gold, out int gems);
            if (result != ShopPurchaseResult.Success) return result;

            _saveData.gold = gold;
            _saveData.gems = gems;
            switch (product.Limit)
            {
                case ShopPurchaseLimit.OncePerSave:
                    _saveData.shop.beginnerPackageClaimed = true;
                    break;
                case ShopPurchaseLimit.DailyGold:
                    _saveData.shop.lastDailyGoldClaimDate = today;
                    break;
                case ShopPurchaseLimit.DailyGems:
                    _saveData.shop.lastDailyGemClaimDate = today;
                    break;
            }
            return ShopPurchaseResult.Success;
        }

        private ShopPurchaseResult Evaluate(ShopProductId id, DateTime localNow,
            out ShopProductDefinition product, out string today, out double gold, out int gems)
        {
            product = null;
            today = null;
            gold = 0d;
            gems = 0;
            if (_saveData == null) return ShopPurchaseResult.NotInitialized;
            if (!ShopCatalog.TryGet(id, out product)) return ShopPurchaseResult.UnknownProduct;
            if (_saveData.shop == null || _saveData.gems < 0 || _saveData.gold < 0d ||
                double.IsNaN(_saveData.gold) || double.IsInfinity(_saveData.gold))
                return ShopPurchaseResult.InvalidSaveData;

            if (product.Limit == ShopPurchaseLimit.OncePerSave && _saveData.shop.beginnerPackageClaimed)
                return ShopPurchaseResult.AlreadyClaimed;
            if (product.Limit == ShopPurchaseLimit.DailyGold || product.Limit == ShopPurchaseLimit.DailyGems)
            {
                string last = product.Limit == ShopPurchaseLimit.DailyGold
                    ? _saveData.shop.lastDailyGoldClaimDate : _saveData.shop.lastDailyGemClaimDate;
                if (!string.IsNullOrEmpty(last))
                {
                    if (!DateTime.TryParseExact(last, "yyyyMMdd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out DateTime claimedDate))
                        return ShopPurchaseResult.InvalidSaveData;
                    // No reset/write at midnight. Clock rollback must not grant another claim.
                    if (localNow.Date <= claimedDate.Date) return ShopPurchaseResult.DailyUnavailable;
                }
                today = localNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            }

            if (_saveData.gems < product.GemCost) return ShopPurchaseResult.NotEnoughGems;
            long nextGems = (long)_saveData.gems - product.GemCost + product.GemReward;
            if (nextGems > int.MaxValue) return ShopPurchaseResult.BalanceLimitExceeded;
            double nextGold = _saveData.gold + product.GoldReward;
            if (double.IsNaN(nextGold) || double.IsInfinity(nextGold) ||
                (product.GoldReward > 0d && nextGold <= _saveData.gold))
                return ShopPurchaseResult.BalanceLimitExceeded;
            // Do not charge gems when a large double balance rounds the reward to a different amount.
            // Permit only sub-millionth-gold arithmetic noise at ordinary balances.
            if (product.GoldReward > 0d &&
                Math.Abs((nextGold - _saveData.gold) - product.GoldReward) > .000001d)
                return ShopPurchaseResult.BalanceLimitExceeded;

            gold = nextGold;
            gems = (int)nextGems;
            return ShopPurchaseResult.Success;
        }
    }
}
