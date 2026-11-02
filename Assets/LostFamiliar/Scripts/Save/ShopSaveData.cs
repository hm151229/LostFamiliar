using System;

namespace LostFamiliar.Core
{
    [Serializable]
    public sealed class ShopSaveData
    {
        public bool beginnerPackageClaimed;
        public string lastDailyGoldClaimDate = string.Empty;
        public string lastDailyGemClaimDate = string.Empty;

        public void Normalize()
        {
            lastDailyGoldClaimDate ??= string.Empty;
            lastDailyGemClaimDate ??= string.Empty;
        }
    }
}
