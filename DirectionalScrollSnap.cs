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

        public RectTransform content;

        public MovementType movementType = MovementType.Elastic;

        public MovementDirection movementDirection = MovementDirection.Horizontal;

        public bool lockOtherDirection = true;

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

        private Vector2 _localCursorStartPosition;
        private Vector2 _contentStartPosition;

        private Bounds contentBounds
        {
            get
            {
                Vector2 contentSize = content.rect.size;
                Vector2 contentCenter = content.rect.center + content.anchoredPosition;
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

        private Vector2 _velocity;
        #endregion

        #region Scrolling
        private void Update()
        {
            if (!IsActive())
            {
                return;
            }

            float deltaTime = affectedByTimeScaling ? Time.deltaTime : Time.unscaledDeltaTime;
            
            // Update Velocity
            Vector2 newVelocity = (content.anchoredPosition - _prevContentPosition) / deltaTime;
            _velocity = Vector2.Lerp(_velocity, newVelocity, deltaTime * 10);

            UpdatePrevData();
        }

        private void UpdatePrevData()
        {
            _prevContentPosition = content.anchoredPosition;
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

            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewRect, ped.position, ped.pressEventCamera, out _localCursorStartPosition);
            _contentStartPosition = content.anchoredPosition;
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
                Vector2 offset = CalculateContentOffsetToStayWithinViewBoundsAfterMoveDelta(position - content.anchoredPosition);
                position += offset;

                if (movementType == MovementType.Elastic)
                {
                    if (offset.x != 0)
                    {
                        position.x -= RubberDelta(offset.x, viewBounds.size.x);
                    }
                    if (offset.y != 0)
                    {
                        position.y -= RubberDelta(offset.y, viewBounds.size.y);
                    }
                }

                content.anchoredPosition = position;
            }
        }

        public void OnEndDrag(PointerEventData ped)
        {
            if (!IsActive())
            {
                return;
            }
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
