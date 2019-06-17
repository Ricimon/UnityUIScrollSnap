using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ricimon.ScrollSnap
{
    public class Scroller
    {
        private const float MINIMUM_VELOCITY = 1f;

        public event Action<Vector2> ScrollPositionUpdate;

        public bool IsScrolling => 
            _scrollState != ScrollState.NotScrolling && 
            _scrollState != ScrollState.WillStartScrolling;

        private DirectionalScrollSnap _scrollSnap;

        private enum ScrollState
        {
            NotScrolling,
            WillStartScrolling,
            ScrollingWithInertia,
            Snapping,
        }

        private ScrollState _scrollState = ScrollState.NotScrolling;

        public Scroller(DirectionalScrollSnap scrollSnap)
        {
            _scrollSnap = scrollSnap;
        }

        public void StopScroll()
        {
            _scrollState = ScrollState.NotScrolling;
        }

        public void StartScroll()
        {
            _scrollState = ScrollState.WillStartScrolling;
        }

        public void Tick()
        {
            switch(_scrollState)
            {
                case ScrollState.WillStartScrolling:
                    _scrollState = ScrollState.ScrollingWithInertia;
                    goto case ScrollState.ScrollingWithInertia;
                case ScrollState.ScrollingWithInertia:
                    if (!_scrollSnap.inertia)
                    {
                        _scrollState = ScrollState.Snapping;
                    }
                    else
                    {
                        float deltaTime = _scrollSnap.affectedByTimeScaling ? Time.deltaTime : Time.unscaledDeltaTime;
                        Vector2 decayedVelocity = _scrollSnap.velocity * Mathf.Pow(_scrollSnap.decelerationRate, deltaTime);

                        //Debug.Log($"Decayed vel: {decayedVelocity.ToString("F6")}");
                        if (Mathf.Abs(decayedVelocity.x) < MINIMUM_VELOCITY &&
                            Mathf.Abs(decayedVelocity.y) < MINIMUM_VELOCITY)
                        {
                            _scrollState = ScrollState.NotScrolling;
                            break;
                        }

                        Vector2 endPosition = _scrollSnap.contentPosition + decayedVelocity * deltaTime;

                        ScrollPositionUpdate?.Invoke(endPosition);
                    }
                    break;
            }
        }
    }
}
