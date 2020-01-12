using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Ricimon.ScrollSnap
{
    [SelectionBase]
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class DirectionalScrollSnap : UIBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IEndDragHandler, IDragHandler, IScrollHandler, ICanvasElement, ILayoutGroup
    {
        #region Variables
        public enum MovementDirection
        {
            Horizontal,
            Vertical
        }

        public enum MovementType
        {
            Clamped,
            Elastic
        }

        public enum SnapType
        {
            SnapToNearest,
            SnapToLastPassed,
            SnapToNext
        }

        public enum InterpolatorType
        {
            Accelerate,
            AccelerateDecelerate,
            Anticipate,
            AnticipateOvershoot,
            Decelerate,
            DecelerateAccelerate,
            Linear,
            Overshoot,
            ViscousFluid
        }

        [Serializable] public class Vector2Event : UnityEvent<Vector2> { }

        // Inspector parameters ----------------------------------------------------
        public RectTransform content;

        public MovementDirection movementDirection = MovementDirection.Horizontal;

        [Tooltip("Prevent scroll movement in the other movement direction.")]
        public bool lockOtherDirection = true;

        public MovementType movementType = MovementType.Elastic;

        public bool inertia = true;

        [Tooltip("Speed reduction per second. A value of 0.5 halves the speed each second. This is only used when inertia is enabled.")]
        public float decelerationRate = 0.135f;

        public SnapType snapType = SnapType.SnapToNearest;

        public InterpolatorType interpolatorType = InterpolatorType.ViscousFluid;

        /// <summary>
        /// The maximum interpolation duration, which occurs when moving to the next snap position when directly on a snap position.
        /// </summary>
        [Tooltip("The maximum interpolation duration, which occurs when moving to the next snap position when directly on a snap position.")]
        public float maxSnapDuration = 0.25f;

        public RectTransform viewport;

        public bool affectedByTimeScaling = false;

        [SerializeField] private bool _drawGizmos = true;

        public Vector2Event OnValueChanged = new Vector2Event();

        // Public accessors ---------------------------------------------------------
        /// <summary>
        /// Content position relative to the viewport
        /// </summary>
        public Vector2 contentPosition
        {
            get
            {
                if (!content)
                    return Vector2.zero;

                Vector2 pos = content.anchoredPosition;
                if (content.parent != viewRect)
                    pos = viewRect.InverseTransformVector(content.TransformVector(pos));
                return pos;
            }
        }

        public Vector2 velocity { get; private set; }

        // Private ------------------------------------------------------------------
        private RectTransform _rectTransform;
        private RectTransform rectTransform
        {
            get
            {
                if (_rectTransform == null)
                {
                    _rectTransform = GetComponent<RectTransform>();
                }
                return _rectTransform;
            }
        }

        private RectTransform viewRect
        {
            get
            {
                if (viewport != null)
                {
                    return viewport;
                }
                return rectTransform;
            }
        }

        private Scroller _scroller;

        private Vector2 _localCursorStartPosition;
        private Vector2 _contentStartPosition;

        private Bounds contentBounds
        {
            get
            {
                if (!content)
                    return new Bounds();

                Vector2 contentSize = content.rect.size;
                Vector2 contentCenter = content.anchoredPosition;
                if (content.parent != viewRect)
                {
                    contentSize = viewRect.InverseTransformVector(content.TransformVector(contentSize));
                    contentCenter = viewRect.InverseTransformVector(content.TransformVector(contentCenter));
                }

                // Pad content bounds to the size of view bounds if necessary.
                Bounds viewBounds = this.viewBounds;
                contentSize.x = Mathf.Max(contentSize.x, viewBounds.size.x);
                contentSize.y = Mathf.Max(contentSize.y, viewBounds.size.y);
                return new Bounds(contentCenter, contentSize);
            }
        }

        private Bounds viewBounds => new Bounds(viewRect.rect.center, viewRect.rect.size);

        private Vector2 _prevContentPosition;

        // Content Rects
        private List<RectTransform> _contentRects = new List<RectTransform>();

        private DrivenRectTransformTracker m_Tracker;
        #endregion

        #region Layout
        public virtual void Rebuild(CanvasUpdate executing)
        {
            if (executing == CanvasUpdate.PostLayout)
            {
                UpdatePrevData();
            }
        }

        public virtual void LayoutComplete() 
        {}

        public virtual void GraphicUpdateComplete()
        {}

        public virtual void SetLayoutHorizontal()
        {
            m_Tracker.Clear();
            if (movementDirection == MovementDirection.Horizontal)
            {
                CollectAndResizeContent(0);
            }
        }

        public virtual void SetLayoutVertical()
        {
            if (movementDirection == MovementDirection.Vertical)
            {
                CollectAndResizeContent(1);
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            CanvasUpdateRegistry.RegisterCanvasElementForLayoutRebuild(this);
            SetDirty();
        }

        protected override void OnDisable()
        {
            CanvasUpdateRegistry.UnRegisterCanvasElementForRebuild(this);

            _scroller?.StopScroll();
            m_Tracker.Clear();
            velocity = Vector2.zero;
            LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
            base.OnDisable();
        }

        protected void SetDirty()
        {
            if (!IsActive())
                return;

            LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
        }

        protected void SetDirtyCaching()
        {
            if (!IsActive())
                return;

            CanvasUpdateRegistry.RegisterCanvasElementForLayoutRebuild(this);
            LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            SetDirtyCaching();
        }
#endif
        #endregion

        #region Scrolling
        protected override void Awake()
        {
            base.Awake();
            _scroller = new Scroller(this);

            _scroller.ScrollPositionUpdate += (pos) =>
            {
                Vector2 targetPosition = ClampTargetPositionToViewBounds(pos);
                if (content.parent != viewRect)
                {
                    targetPosition = content.InverseTransformVector(viewRect.TransformVector(targetPosition));
                }
                content.anchoredPosition = targetPosition;
            };
        }

        protected override void Start()
        {
            //CollectContent();
        }

        protected virtual void LateUpdate()
        {
            if (!IsActive())
                return;

            if (!_scroller)
                return;

            float deltaTime = affectedByTimeScaling ? Time.deltaTime : Time.unscaledDeltaTime;
            
            // Update Velocity
            Vector2 newVelocity = (contentPosition - _prevContentPosition) / deltaTime;
            // User dragging should smooth velocity, whereas script-controlled movement should be precise velocity.
            if (_scroller.State == Scroller.ScrollState.NotScrolling)
            {
                velocity = new Vector2(
                    Mathf.Abs(newVelocity.x) > Mathf.Abs(velocity.x) ? Mathf.Lerp(velocity.x, newVelocity.x, deltaTime * 10) : newVelocity.x,
                    Mathf.Abs(newVelocity.y) > Mathf.Abs(velocity.y) ? Mathf.Lerp(velocity.y, newVelocity.y, deltaTime * 10) : newVelocity.y);
            }
            else if (_scroller.State != Scroller.ScrollState.WillStartScrolling)
            {
                velocity = newVelocity;
            }

            UpdatePrevData();

            _scroller.Tick();
        }

        protected void UpdatePrevData()
        {
            _prevContentPosition = contentPosition;
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (!IsActive())
                return;
        }

        public virtual void OnInitializePotentialDrag(PointerEventData ped)
        {
            if (ped.button != PointerEventData.InputButton.Left)
                return;

            _scroller.StopScroll();
        }
        
        public virtual void OnBeginDrag(PointerEventData ped)
        {
            if (ped.button != PointerEventData.InputButton.Left)
                return;

            if (!IsActive())
                return;

            _scroller.StopScroll();

            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewRect, ped.position, ped.pressEventCamera, out _localCursorStartPosition);
            _contentStartPosition = contentPosition;
        }

        public virtual void OnDrag(PointerEventData ped)
        {
            if (ped.button != PointerEventData.InputButton.Left)
                return;

            if (!IsActive())
                return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(viewRect, ped.position, ped.pressEventCamera, out Vector2 localCursor))
            {
                Vector2 pointerDelta = localCursor - _localCursorStartPosition;
                pointerDelta = FitDeltaToScrollDirection(pointerDelta);
                Vector2 position = _contentStartPosition + pointerDelta;

                content.anchoredPosition = ClampTargetPositionToViewBounds(position);
            }
        }

        public virtual void OnEndDrag(PointerEventData ped)
        {
            if (!IsActive())
                return;

            _scroller.StartScroll();
        }

        private Vector2 FitDeltaToScrollDirection(Vector2 delta)
        {
            if (!lockOtherDirection)
            {
                return delta;
            }
            else
            {
                if (movementDirection == MovementDirection.Horizontal)
                {
                    delta.y = 0;
                }
                else
                {
                    delta.x = 0;
                }
                return delta;
            }
        }

        private Vector2 ClampTargetPositionToViewBounds(Vector2 targetPosition)
        {
            Vector2 offset = CalculateContentOffsetToStayWithinViewBoundsAfterMoveDelta(targetPosition - content.anchoredPosition);
            targetPosition += offset;

            if (movementType == MovementType.Elastic)
            {
                bool inBounds = true;
                if (offset.x != 0)
                {
                    targetPosition.x -= RubberDelta(offset.x, viewBounds.size.x);
                    inBounds = false;
                }
                if (offset.y != 0)
                {
                    targetPosition.y -= RubberDelta(offset.y, viewBounds.size.y);
                    inBounds = false;
                }
                _scroller.ContentInBounds = inBounds;
            }

            return targetPosition;
        }

        private Vector2 CalculateContentOffsetToStayWithinViewBoundsAfterMoveDelta(Vector2 delta)
        {
            var offset = Vector2.zero;

            Bounds contentBounds = this.contentBounds;  // cache
            Vector2 min = (Vector2)contentBounds.min + delta;
            Vector2 max = (Vector2)contentBounds.max + delta;
            Bounds viewBounds = this.viewBounds;

            if (movementDirection == MovementDirection.Horizontal || !lockOtherDirection)
            {
                if (min.x > viewBounds.min.x)
                {
                    offset.x = viewBounds.min.x - min.x;
                }
                else if (max.x < viewBounds.max.x)
                {
                    offset.x = viewBounds.max.x - max.x;
                }
            }
            
            if (movementDirection == MovementDirection.Vertical || !lockOtherDirection)
            {
                if (min.y > viewBounds.min.y)
                {
                    offset.y = viewBounds.min.y - min.y;
                }
                else if (max.y < viewBounds.max.y)
                {
                    offset.y = viewBounds.max.y - max.y;
                }
            }

            return offset;
        }
        #endregion

        #region Content Management
        private void CollectContent()
        {
            _contentRects.Clear();

            if (!content)
                return;

            foreach (RectTransform rt in content)
            {
                _contentRects.Add(rt);
            }
        }

        private void CollectAndResizeContent(int axis)
        {
            _contentRects.Clear();

            if (!content)
                return;

            // Use Horizontal/Vertical LayoutGroup to visually sort all the content rects
            var layoutGroup = content.GetComponent<LayoutGroup>();
            if (axis == 0)
            {
                if (!layoutGroup)
                {
                    AddLayoutGroupToContent<HorizontalLayoutGroup>();
                }
                else if (!(layoutGroup is HorizontalLayoutGroup))
                {
                    Destroy(layoutGroup);
                    AddLayoutGroupToContent<HorizontalLayoutGroup>();
                }
            }
            else if (axis == 1)
            {
                if (!layoutGroup)
                {
                    AddLayoutGroupToContent<VerticalLayoutGroup>();
                }
                else if (!(layoutGroup is VerticalLayoutGroup))
                {
                    Destroy(layoutGroup);
                    AddLayoutGroupToContent<VerticalLayoutGroup>();
                }
            }

            // Gather
            float totalContentLength = 0f;  // could be width or height depending on scroll movement direction
            foreach (RectTransform rt in content)
            {
                _contentRects.Add(rt);
                totalContentLength += rt.sizeDelta[axis];
            }

            // Resize the Content rect to fit the content (TODO: Add boolean to toggle this off?)
            Vector2 tempContentSizeDelta = content.sizeDelta;
            tempContentSizeDelta[axis] = totalContentLength;
            m_Tracker.Add(this, content, (axis == 0 ?
                DrivenTransformProperties.SizeDeltaX :
                DrivenTransformProperties.SizeDeltaY) |
                DrivenTransformProperties.Pivot);
#if UNITY_EDITOR
            Undo.RecordObject(content, "Fit Content Size");
#endif
            content.sizeDelta = tempContentSizeDelta;
            content.pivot = new Vector2(0.5f, 0.5f);
        }

        private void AddLayoutGroupToContent<T>() where T : HorizontalOrVerticalLayoutGroup
        {
#if UNITY_EDITOR
            var layoutGroup = Undo.AddComponent<T>(content.gameObject);
#else
            var layoutGroup = content.gameObject.AddComponent<T>();
#endif
            layoutGroup.childForceExpandWidth = layoutGroup.childForceExpandHeight = false;
        }

        protected float GetRectScrollPosition(int index)
        {
            int axis = movementDirection == MovementDirection.Horizontal ?
                0 :
                1;
            return _contentRects[index].anchoredPosition[axis] + contentPosition[axis] - contentBounds.extents[axis];
        }

        protected virtual void OnDrawGizmosSelected()
        {
            if (_drawGizmos)
            {
                Color initialColor = Gizmos.color;

                Vector3[] corners = new Vector3[4];
                content.GetWorldCorners(corners);

                Vector3 bottomLeftWorld = corners[0];
                Vector3 topLeftWorld = corners[1];
                Vector3 topRightWorld = corners[2];
                Vector3 bottomRightWorld = corners[3];

                Vector3 up = topRightWorld - bottomRightWorld;
                Vector3 left = topLeftWorld - topRightWorld;

                foreach(RectTransform rt in content)
                {
                    Gizmos.color = Color.cyan;
                    if (movementDirection == MovementDirection.Horizontal)
                    {
                        Gizmos.DrawRay(rt.position - up / 2f, up);
                    }
                    else
                    {
                        Gizmos.DrawRay(rt.position - left / 2f, left);
                    }
                }

                Gizmos.color = initialColor;
            }
        }
        #endregion

        #region Public Functions
        public override bool IsActive()
        {
            return base.IsActive() && content != null;
        }

        public virtual bool TryGetClosestElement(out int index, out RectTransform rectTransform)
        {
            if (!content || _contentRects.Count == 0)
            {
                index = 0;
                rectTransform = null;
                return false;
            }

            float closestDistance = Mathf.Abs(GetRectScrollPosition(0));
            index = 0;
            for (int i = 1; i < _contentRects.Count; i++)
            {
                float displacement = GetRectScrollPosition(i);
                float distance = Mathf.Abs(displacement);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    index = i;
                }
                if (displacement >= 0)
                    break;
            }
            rectTransform = _contentRects[index];
            return true;
        }
        #endregion

        #region Calculators
        internal static float RubberDelta(float overStretching, float viewSize)
            => (1 - (1 / ((Mathf.Abs(overStretching) * 0.55f / viewSize) + 1))) * viewSize * Mathf.Sign(overStretching);
        #endregion
    }
}
