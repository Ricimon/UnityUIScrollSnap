using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ricimon.ScrollSnap
{
    public class DirectionalScrollSnap : UIBehaviour, IBeginDragHandler, IEndDragHandler, IDragHandler, IScrollHandler
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
        public Vector2 contentPosition => content.anchoredPosition;

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
                Vector2 contentSize = content.rect.size;
                Vector2 contentCenter = content.rect.center + contentPosition;
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
        #endregion

        #region Scrolling
        protected override void Awake()
        {
            base.Awake();
            _scroller = new Scroller(this);

            _scroller.ScrollPositionUpdate += (pos) =>
            {
                content.anchoredPosition = ClampTargetPositionToViewBounds(pos);
            };
        }

        private void Update()
        {
            if (!IsActive())
            {
                return;
            }

            float deltaTime = affectedByTimeScaling ? Time.deltaTime : Time.unscaledDeltaTime;
            
            // Update Velocity
            Vector2 newVelocity = (contentPosition - _prevContentPosition) / deltaTime;
            // User dragging should smooth velocity, whereas script-controlled movement should be precise velocity.
            velocity = _scroller.IsScrolling ?
                newVelocity :
                Vector2.Lerp(velocity, newVelocity, deltaTime * 10);

            UpdatePrevData();

            _scroller.Tick();
        }

        private void UpdatePrevData()
        {
            _prevContentPosition = contentPosition;
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (!IsActive())
            {
                return;
            }
            Debug.Log("OnScroll");
        }

        public void OnBeginDrag(PointerEventData ped)
        {
            if (!IsActive())
            {
                return;
            }

            _scroller.StopScroll();

            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewRect, ped.position, ped.pressEventCamera, out _localCursorStartPosition);
            _contentStartPosition = contentPosition;
        }

        public void OnDrag(PointerEventData ped)
        {
            if (!IsActive())
            {
                return;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(viewRect, ped.position, ped.pressEventCamera, out Vector2 localCursor))
            {
                Vector2 pointerDelta = localCursor - _localCursorStartPosition;
                pointerDelta = FitDeltaToScrollDirection(pointerDelta);
                Vector2 position = _contentStartPosition + pointerDelta;

                content.anchoredPosition = ClampTargetPositionToViewBounds(position);
            }
        }

        public void OnEndDrag(PointerEventData ped)
        {
            if (!IsActive())
            {
                return;
            }

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
                if (offset.x != 0)
                {
                    targetPosition.x -= RubberDelta(offset.x, viewBounds.size.x);
                    _scroller.ScrollBackInBounds();
                }
                if (offset.y != 0)
                {
                    targetPosition.y -= RubberDelta(offset.y, viewBounds.size.y);
                    _scroller.ScrollBackInBounds();
                }
            }

            return targetPosition;
        }

        private Vector2 CalculateContentOffsetToStayWithinViewBoundsAfterMoveDelta(Vector2 delta)
        {
            var offset = Vector2.zero;

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

        #region Public Functions
        public override bool IsActive()
        {
            return base.IsActive() && content != null;
        }
        #endregion

        #region Calculators
        private float RubberDelta(float overStretching, float viewSize)
            => (1 - (1 / ((Mathf.Abs(overStretching) * 0.55f / viewSize) + 1))) * viewSize * Mathf.Sign(overStretching);
        #endregion
    }
}
