using System;
using System.Globalization;
using LostFamiliar.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LostFamiliar.Battle
{
    // Apply interactable changes before the existing button visual LateUpdate.
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed partial class ShopPopupController : MonoBehaviour
    {
        [Serializable]
        private sealed class ProductBinding
        {
            [SerializeField] private ShopProductId productId;
            [SerializeField] private TMP_Text nameText;
            [SerializeField] private TMP_Text goldRewardText;
            [SerializeField] private TMP_Text gemRewardText;
            [SerializeField] private Button purchaseButton;
            [SerializeField] private TMP_Text buttonText;
            [NonSerialized] internal UnityAction purchaseAction;
            [NonSerialized] internal string availableButtonText;

            internal ShopProductId Id => productId;
            internal TMP_Text NameText => nameText;
            internal TMP_Text GoldRewardText => goldRewardText;
            internal TMP_Text GemRewardText => gemRewardText;
            internal Button PurchaseButton => purchaseButton;
            internal TMP_Text ButtonText => buttonText;
        }

        [SerializeField] private ProductBinding[] products = Array.Empty<ProductBinding>();
        [SerializeField] private TMP_Text goldText;
        [SerializeField] private TMP_Text gemText;

        private MainBattleLoop _battle;
        private bool _listenersBound;
        private bool _contentInitialized;
        private bool _refreshPending;
        private bool _purchaseInProgress;
        private DateTime _lastLocalDate;
        private float _nextDateCheck;

        public ShopPurchaseResult LastPurchaseResult { get; private set; } = ShopPurchaseResult.NotInitialized;

        private void Awake() => BindButtons();

        public void Bind(MainBattleLoop battle)
        {
            if (_battle != null) _battle.StateChanged -= RequestRefresh;
            _battle = battle;
            if (_battle != null) _battle.StateChanged += RequestRefresh;
            BindButtons();
            RequestRefresh();
        }

        private void BindButtons()
        {
            if (_listenersBound) return;
            _listenersBound = true;
            if (products == null) return;
            foreach (ProductBinding binding in products)
            {
                if (binding == null || binding.PurchaseButton == null) continue;
                ProductBinding captured = binding;
                binding.purchaseAction = () => Purchase(captured.Id);
                binding.PurchaseButton.onClick.AddListener(binding.purchaseAction);
            }
        }

        private void RequestRefresh() => _refreshPending = true;

        private void OnEnable()
        {
            _lastLocalDate = LocalNow.Date;
            _nextDateCheck = Time.unscaledTime + 1f;
            RequestRefresh();
        }

        private void LateUpdate() => Tick(Time.unscaledTime);

        private void Tick(float realtime)
        {
            if (realtime >= _nextDateCheck)
            {
                _nextDateCheck = realtime + 1f;
                DateTime date = LocalNow.Date;
                if (date != _lastLocalDate)
                {
                    _lastLocalDate = date;
                    RequestRefresh();
                }
            }
            RefreshIfNeeded();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused) RequestRefresh();
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused) RequestRefresh();
        }

        private void Purchase(ShopProductId id)
        {
            if (!isActiveAndEnabled || _purchaseInProgress) return;
            _purchaseInProgress = true;
            try
            {
                LastPurchaseResult = PurchaseProduct(id);
                RequestRefresh();
                RefreshIfNeeded();
            }
            finally { _purchaseInProgress = false; }
        }

        private void RefreshIfNeeded()
        {
            if (!_refreshPending || !isActiveAndEnabled) return;
            _refreshPending = false;
            SetText(goldText, MainHUDController.FormatNumber(GoldBalance));
            SetText(gemText, MainHUDController.FormatGem(GemBalance));
            if (products == null) return;
            foreach (ProductBinding binding in products)
            {
                if (binding == null) continue;
                if (!ShopCatalog.TryGet(binding.Id, out ShopProductDefinition product))
                {
                    SetInteractable(binding.PurchaseButton, false);
                    SetText(binding.ButtonText, "구매 불가");
                    continue;
                }
                if (!_contentInitialized)
                {
                    binding.availableButtonText = product.GemCost > 0
                        ? product.GemCost.ToString("N0", CultureInfo.InvariantCulture) : "무료 받기";
                    SetText(binding.NameText, product.Name);
                    SetText(binding.GoldRewardText, product.GoldReward.ToString("N0", CultureInfo.InvariantCulture));
                    SetText(binding.GemRewardText, product.GemReward.ToString("N0", CultureInfo.InvariantCulture));
                }
                ShopPurchaseResult status = GetProductStatus(binding.Id);
                SetInteractable(binding.PurchaseButton, status == ShopPurchaseResult.Success);
                SetText(binding.ButtonText, GetButtonText(binding.availableButtonText, status));
            }
            _contentInitialized = true;
        }

        private DateTime LocalNow
        {
            get
            {
#if UNITY_EDITOR
                if (_editorPreviewData != null) return _editorPreviewNow;
#endif
                return DateTime.Now;
            }
        }

        private double GoldBalance
        {
            get
            {
#if UNITY_EDITOR
                if (_editorPreviewData != null) return _editorPreviewData.gold;
#endif
                return _battle?.Gold ?? 0d;
            }
        }

        private int GemBalance
        {
            get
            {
#if UNITY_EDITOR
                if (_editorPreviewData != null) return _editorPreviewData.gems;
#endif
                return _battle?.Gems ?? 0;
            }
        }

        private ShopPurchaseResult GetProductStatus(ShopProductId id)
        {
#if UNITY_EDITOR
            if (_editorPreviewSystem != null) return _editorPreviewSystem.GetPurchaseStatus(id, LocalNow);
#endif
            return _battle != null ? _battle.GetShopPurchaseStatus(id) : ShopPurchaseResult.NotInitialized;
        }

        private ShopPurchaseResult PurchaseProduct(ShopProductId id)
        {
#if UNITY_EDITOR
            if (_editorPreviewSystem != null) return _editorPreviewSystem.TryPurchase(id, LocalNow);
#endif
            return _battle != null ? _battle.TryPurchaseShopProduct(id) : ShopPurchaseResult.NotInitialized;
        }

        private static string GetButtonText(string availableText, ShopPurchaseResult status)
        {
            if (status == ShopPurchaseResult.AlreadyClaimed) return "수령 완료";
            if (status == ShopPurchaseResult.DailyUnavailable) return "수령 완료";
            if (status == ShopPurchaseResult.NotInitialized) return "준비 중";
            if (status != ShopPurchaseResult.Success && status != ShopPurchaseResult.NotEnoughGems)
                return "구매 불가";
            return availableText;
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null && target.text != value) target.text = value;
        }

        private static void SetInteractable(Button target, bool value)
        {
            if (target != null && target.interactable != value) target.interactable = value;
        }

        private void OnDestroy()
        {
            if (_battle != null) _battle.StateChanged -= RequestRefresh;
            if (products == null) return;
            foreach (ProductBinding binding in products)
            {
                if (binding?.PurchaseButton != null && binding.purchaseAction != null)
                    binding.PurchaseButton.onClick.RemoveListener(binding.purchaseAction);
            }
        }
    }
}
