using UnityEngine;
using UnityEngine.UI;

namespace LostFamiliar.Battle
{
    // Serialized opt-in or safely discovered dynamic button. Owns only this dedicated group.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button), typeof(CanvasGroup))]
    public sealed class CanvasGroupDisabledVisual : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float disabledAlpha = .5019608f;

        private Button _button;
        private CanvasGroup _group;
        private float _baseAlpha;
        private bool _wasInteractable;
        private bool _hasAppliedState;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _group = GetComponent<CanvasGroup>();
            _baseAlpha = _group.alpha;
        }

        internal void SetDisabledAlpha(float alpha)
        {
            disabledAlpha = Mathf.Clamp01(alpha);
            _hasAppliedState = false;
            ApplyState();
        }

        private void OnEnable()
        {
            _hasAppliedState = false;
            ApplyState();
        }

        private void LateUpdate() => ApplyState();

        private void ApplyState()
        {
            if (_button == null || _group == null) return;
            // Include parent CanvasGroup interaction restrictions without changing input policy.
            bool interactable = _button.IsInteractable();
            if (_hasAppliedState && interactable == _wasInteractable) return;
            _group.alpha = _baseAlpha * (interactable ? 1f : disabledAlpha);
            _wasInteractable = interactable;
            _hasAppliedState = true;
        }

        private void OnDisable()
        {
            if (_group != null) _group.alpha = _baseAlpha;
            _hasAppliedState = false;
        }
    }
}
