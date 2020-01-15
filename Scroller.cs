using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ricimon.ScrollSnap
{
    public class Scroller
    {
        public event Action<Vector2> ScrollPositionUpdate;

        public enum ScrollState
        {
            NotScrolling,
            WillStartScrolling,
            ScrollingWithInertia,
            ScrollingBackInBounds,
            Snapping,
        }

        public ScrollState State { get; private set; } = ScrollState.NotScrolling;

        public bool ContentInBounds { get; set; }

        private const float MINIMUM_VELOCITY = 1f;

        private DirectionalScrollSnap _scrollSnap;

        private IInterpolator _interpolator;

        private float _scrollTotalDuration;
        private float _scrollTotalDurationReciprocal;
        private float _scrollDurationLeft;
        private Vector2 _scrollStartPosition;
        private Vector2 _scrollTargetPosition;
        private Vector2 _movementDelta;

        public Scroller(DirectionalScrollSnap scrollSnap)
        {
            _scrollSnap = scrollSnap;
        }

        public void StopScroll()
        {
            State = ScrollState.NotScrolling;
        }

        public void StartScroll(Vector2 startPosition, Vector2 targetPosition, float duration, IInterpolator interpolator)
        {
            if (interpolator == null)
            {
                interpolator = new ViscousFluidInterpolator();
            }

            State = ScrollState.Snapping;
            _interpolator = interpolator;
            _scrollTotalDuration = _scrollDurationLeft = duration;
            _scrollTotalDurationReciprocal = 1 / duration;
            _scrollStartPosition = startPosition;
            _scrollTargetPosition = targetPosition;
            _movementDelta = targetPosition - startPosition;

            //if (ContentInBounds)
            //{
            //    State = ScrollState.WillStartScrolling;
            //}
            //else
            //{
            //    State = ScrollState.ScrollingBackInBounds;
            //}
        }

        public void Tick()
        {
            switch(State)
            {
                case ScrollState.Snapping:
                    _scrollDurationLeft = Mathf.Max(
                        _scrollDurationLeft - (_scrollSnap.affectedByTimeScaling ? Time.deltaTime : Time.unscaledDeltaTime),
                        0f);

                    float x = _interpolator.GetInterpolation((_scrollTotalDuration - _scrollDurationLeft) * _scrollTotalDurationReciprocal);
                    ScrollPositionUpdate?.Invoke(_scrollStartPosition + (x * _movementDelta));

                    if (_scrollDurationLeft == 0)
                    {
                        State = ScrollState.NotScrolling;
                    }
                    break;

                case ScrollState.WillStartScrolling:
                    State = ScrollState.ScrollingWithInertia;
                    goto case ScrollState.ScrollingWithInertia;

                case ScrollState.ScrollingWithInertia:
                    if (!ContentInBounds)
                    {
                        goto case ScrollState.ScrollingBackInBounds;
                    }
                    if (!_scrollSnap.inertia)
                    {
                        State = ScrollState.Snapping;
                        goto case ScrollState.Snapping;
                    }
                    else
                    {
                        float deltaTime = _scrollSnap.affectedByTimeScaling ? Time.deltaTime : Time.unscaledDeltaTime;
                        Vector2 decayedVelocity = _scrollSnap.velocity * Mathf.Pow(_scrollSnap.decelerationRate, deltaTime);

                        //Debug.Log($"Decayed vel: {decayedVelocity.ToString("F6")}");
                        if (Mathf.Abs(decayedVelocity.x) < MINIMUM_VELOCITY &&
                            Mathf.Abs(decayedVelocity.y) < MINIMUM_VELOCITY)
                        {
                            State = ScrollState.NotScrolling;
                            break;
                        }

                        Vector2 endPosition = _scrollSnap.contentPosition + decayedVelocity * deltaTime;

                        ScrollPositionUpdate?.Invoke(endPosition);
                    }
                    break;

                case ScrollState.ScrollingBackInBounds:
                    ScrollPositionUpdate?.Invoke(_scrollSnap.contentPosition);
                    break;
            }
        }

        public static implicit operator bool(Scroller exists)
        {
            return exists != null;
        }
    }
}
