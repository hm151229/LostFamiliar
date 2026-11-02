using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Profiling;
#endif

namespace LostFamiliar.UI
{
    /// <summary>Fits direct child slots to the layout rect while preserving their aspect ratio.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class ResponsiveSlotLayout : MonoBehaviour
    {
        [Header("Slot Size")]
        [SerializeField, Min(.01f)] private float widthToHeightRatio = 1f;
        [SerializeField, Min(0f)] private float minimumWidth;
        [SerializeField, Min(0f)] private float maximumWidth;

        [Header("Grid")]
        [Tooltip("0이면 GridLayoutGroup의 Constraint Count를 사용합니다.")]
        [SerializeField, Min(0)] private int gridColumnOverride;

        [Header("Children")]
        [SerializeField] private bool includeInactiveChildren;
        [Tooltip("활성화하면 자식의 LayoutElement를 자동으로 추가하고 크기를 적용합니다.")]
        [SerializeField] private bool createLayoutElements = true;

        private struct ChildEntry
        {
            internal GameObject child;
            internal LayoutElement element;
            internal bool participates;
        }

        // Explicit comparisons avoid ValueType.Equals boxing in the steady-state check.
        private struct Settings
        {
            internal int kind, left, right, top, bottom, columns, constraint;
            internal float ratio, min, max, spacingX, spacingY;
            internal Vector2 cellSize;
            internal bool inactive, create, controlWidth, controlHeight, expandWidth, expandHeight;

            internal bool Matches(Settings other) =>
                kind == other.kind && left == other.left && right == other.right &&
                top == other.top && bottom == other.bottom && columns == other.columns &&
                constraint == other.constraint && ratio == other.ratio && min == other.min &&
                max == other.max && spacingX == other.spacingX && spacingY == other.spacingY &&
                cellSize.Equals(other.cellSize) && inactive == other.inactive && create == other.create &&
                controlWidth == other.controlWidth && controlHeight == other.controlHeight &&
                expandWidth == other.expandWidth && expandHeight == other.expandHeight;
        }

        private RectTransform _rectTransform;
        private GridLayoutGroup _grid;
        private HorizontalLayoutGroup _horizontal;
        private VerticalLayoutGroup _vertical;
        private readonly List<ChildEntry> _children = new();
        private Vector2 _lastRectSize = new(float.MinValue, float.MinValue);
        private Settings _lastSettings;
        private int _lastChildCount = -1;
        private int _layoutChildCount;
        private bool _componentsDirty = true;
        private bool _childrenDirty = true;
        private bool _refreshQueued;
        private bool _refreshing;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static readonly ProfilerMarker LateUpdateMarker = new("ResponsiveSlotLayout.LateUpdate");
        private static readonly ProfilerMarker ChildrenMarker = new("ResponsiveSlotLayout.CheckParticipation");
        private static readonly ProfilerMarker CacheMarker = new("ResponsiveSlotLayout.CacheChildren");
        private static readonly ProfilerMarker RefreshMarker = new("ResponsiveSlotLayout.Refresh");
        private static readonly ProfilerMarker RebuildMarker = new("ResponsiveSlotLayout.MarkLayoutForRebuild");
#endif

        private void Awake() => CacheComponents();

        private void OnEnable()
        {
            // One initial application, after Awake, including changes made while inactive.
            RefreshNow();
        }

        private void OnValidate()
        {
            widthToHeightRatio = Mathf.Max(.01f, widthToHeightRatio);
            minimumWidth = Mathf.Max(0f, minimumWidth);
            maximumWidth = Mathf.Max(0f, maximumWidth);
            QueueRefresh();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (_rectTransform == null || _rectTransform.rect.size != _lastRectSize)
                _refreshQueued = true;
        }

        private void OnTransformChildrenChanged() => QueueRefresh();

        // Call this after changing the layout component set or applying a batch of slot changes.
        public void QueueRefresh()
        {
            _refreshQueued = true;
            _componentsDirty = true;
            _childrenDirty = true;
        }

        private void LateUpdate()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using var sample = LateUpdateMarker.Auto();
#endif
            CacheComponents();
            if (_rectTransform == null) return;
            if (transform.childCount != _lastChildCount)
                _childrenDirty = true;

            bool changed = false;
            if (_grid == null && (_horizontal != null || _vertical != null))
            {
                CacheChildren();
                changed = CheckParticipation();
            }
            // Grid cell dimensions do not depend on child participation. Its LayoutGroup
            // already handles SetActive/ignoreLayout when arranging the cells.
            if (_refreshQueued || changed || _rectTransform.rect.size != _lastRectSize ||
                transform.childCount != _lastChildCount || !_lastSettings.Matches(ReadSettings()))
                RefreshLayout(true);
        }

        [ContextMenu("Refresh Responsive Layout")]
        public void RefreshNow()
        {
            QueueRefresh();
            RefreshLayout(false);
        }

        private void RefreshLayout(bool participationChecked)
        {
            if (_refreshing) return;
            CacheComponents();
            if (_rectTransform == null) return;
            _refreshing = true;
            _refreshQueued = false;
            try
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                using var sample = RefreshMarker.Auto();
#endif
                if (_grid == null && (_horizontal != null || _vertical != null) && !participationChecked)
                {
                    CacheChildren();
                    CheckParticipation();
                }

                bool changed = false;
                if (_grid != null) changed = RefreshGrid();
                else if (_horizontal != null) changed = RefreshHorizontal();
                else if (_vertical != null) changed = RefreshVertical();

                _lastRectSize = _rectTransform.rect.size;
                _lastChildCount = transform.childCount;
                _lastSettings = ReadSettings();
                if (changed)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    using var rebuildSample = RebuildMarker.Auto();
#endif
                    LayoutRebuilder.MarkLayoutForRebuild(_rectTransform);
                }
            }
            finally { _refreshing = false; }
        }

        private bool RefreshGrid()
        {
            int columns = gridColumnOverride > 0 ? gridColumnOverride : Mathf.Max(1, _grid.constraintCount);
            bool changed = false;
            if (_grid.constraint != GridLayoutGroup.Constraint.FixedColumnCount)
            {
                _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                changed = true;
            }
            if (_grid.constraintCount != columns)
            {
                _grid.constraintCount = columns;
                changed = true;
            }
            float availableWidth = GetInnerWidth(_grid.padding) - _grid.spacing.x * Mathf.Max(0, columns - 1);
            float width = ClampWidth(availableWidth / columns);
            Vector2 size = new(width, width / widthToHeightRatio);
            if (!_grid.cellSize.Equals(size)) { _grid.cellSize = size; changed = true; }
            return changed;
        }

        private bool RefreshHorizontal()
        {
            if (_layoutChildCount <= 0) return false;
            float availableWidth = GetInnerWidth(_horizontal.padding) -
                                   _horizontal.spacing * Mathf.Max(0, _layoutChildCount - 1);
            float width = ClampWidth(availableWidth / _layoutChildCount);
            bool changed = ConfigureGroup(_horizontal);
            return ApplyChildSizes(width, width / widthToHeightRatio) || changed;
        }

        private bool RefreshVertical()
        {
            float width = ClampWidth(GetInnerWidth(_vertical.padding));
            bool changed = ConfigureGroup(_vertical);
            return ApplyChildSizes(width, width / widthToHeightRatio) || changed;
        }

        private static bool ConfigureGroup(HorizontalOrVerticalLayoutGroup group)
        {
            bool changed = false;
            if (!group.childControlWidth) { group.childControlWidth = true; changed = true; }
            if (!group.childControlHeight) { group.childControlHeight = true; changed = true; }
            if (group.childForceExpandWidth) { group.childForceExpandWidth = false; changed = true; }
            if (group.childForceExpandHeight) { group.childForceExpandHeight = false; changed = true; }
            return changed;
        }

        private float GetInnerWidth(RectOffset padding) => Mathf.Max(0f,
            _rectTransform.rect.width - padding.left - padding.right);

        private float ClampWidth(float width)
        {
            width = Mathf.Max(0f, width);
            if (minimumWidth > 0f) width = Mathf.Max(minimumWidth, width);
            if (maximumWidth > 0f) width = Mathf.Min(maximumWidth, width);
            return width;
        }

        private void CacheChildren()
        {
            if (!_childrenDirty) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using var sample = CacheMarker.Auto();
#endif
            _children.Clear();
            for (int i = 0; i < transform.childCount; i++)
            {
                GameObject child = transform.GetChild(i).gameObject;
                _children.Add(new ChildEntry { child = child, element = child.GetComponent<LayoutElement>() });
            }
            _childrenDirty = false;
        }

        private bool CheckParticipation()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using var sample = ChildrenMarker.Auto();
#endif
            bool changed = false;
            int count = 0;
            for (int i = 0; i < _children.Count; i++)
            {
                ChildEntry entry = _children[i];
                if (entry.child == null) { _childrenDirty = true; _refreshQueued = true; continue; }
                bool eligible = includeInactiveChildren || entry.child.activeSelf;
                bool entryChanged = false;
                // Missing/destroyed components are the narrow fallback: component addition
                // has no parent callback. Normal createLayoutElements=true slots are cached.
                if (eligible && entry.element == null)
                {
                    entry.element = entry.child.GetComponent<LayoutElement>();
                    entryChanged = true;
                    // Apply sizes to a newly added element, or recreate a removed one.
                    if (entry.element != null || createLayoutElements) changed = true;
                }
                bool participates = eligible && (entry.element == null || !entry.element.ignoreLayout);
                if (participates) count++;
                if (entry.participates != participates)
                {
                    changed = true;
                    entryChanged = true;
                    entry.participates = participates;
                }
                if (entryChanged) _children[i] = entry;
            }
            _layoutChildCount = count;
            return changed;
        }

        private bool ApplyChildSizes(float width, float height)
        {
            bool changed = false;
            for (int i = 0; i < _children.Count; i++)
            {
                ChildEntry entry = _children[i];
                if (!entry.participates || entry.child == null) continue;
                LayoutElement element = entry.element;
                if (element == null && createLayoutElements)
                {
                    element = entry.child.AddComponent<LayoutElement>();
                    entry.element = element;
                    _children[i] = entry;
                    changed = true;
                }
                if (element == null || element.ignoreLayout) continue;
                if (element.minWidth != width) { element.minWidth = width; changed = true; }
                if (element.preferredWidth != width) { element.preferredWidth = width; changed = true; }
                if (element.flexibleWidth != 0f) { element.flexibleWidth = 0f; changed = true; }
                if (element.minHeight != height) { element.minHeight = height; changed = true; }
                if (element.preferredHeight != height) { element.preferredHeight = height; changed = true; }
                if (element.flexibleHeight != 0f) { element.flexibleHeight = 0f; changed = true; }
            }
            return changed;
        }

        private void CacheComponents()
        {
            if (!_componentsDirty && _rectTransform != null) return;
            _rectTransform = GetComponent<RectTransform>();
            _grid = GetComponent<GridLayoutGroup>();
            _horizontal = GetComponent<HorizontalLayoutGroup>();
            _vertical = GetComponent<VerticalLayoutGroup>();
            _componentsDirty = false;
        }

        private Settings ReadSettings()
        {
            Settings settings = new Settings
            {
                ratio = widthToHeightRatio, min = minimumWidth, max = maximumWidth,
                inactive = includeInactiveChildren, create = createLayoutElements,
                columns = gridColumnOverride
            };
            RectOffset padding = null;
            if (_grid != null)
            {
                settings.kind = 1;
                padding = _grid.padding;
                settings.spacingX = _grid.spacing.x;
                settings.spacingY = _grid.spacing.y;
                settings.columns = gridColumnOverride > 0 ? gridColumnOverride : _grid.constraintCount;
                settings.constraint = (int)_grid.constraint;
                settings.cellSize = _grid.cellSize;
            }
            else
            {
                HorizontalOrVerticalLayoutGroup group = _horizontal != null
                    ? (HorizontalOrVerticalLayoutGroup)_horizontal : _vertical;
                if (group != null)
                {
                    settings.kind = _horizontal != null ? 2 : 3;
                    padding = group.padding;
                    settings.spacingX = group.spacing;
                    settings.controlWidth = group.childControlWidth;
                    settings.controlHeight = group.childControlHeight;
                    settings.expandWidth = group.childForceExpandWidth;
                    settings.expandHeight = group.childForceExpandHeight;
                }
            }
            if (padding != null)
            {
                settings.left = padding.left; settings.right = padding.right;
                settings.top = padding.top; settings.bottom = padding.bottom;
            }
            return settings;
        }
    }
}
