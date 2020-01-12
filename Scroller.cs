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

        public Scroller(DirectionalScrollSnap scrollSnap)
        {
            _scrollSnap = scrollSnap;
        }

        public void StopScroll()
        {
            State = ScrollState.NotScrolling;
        }

        public void StartScroll()
        {
            if (ContentInBounds)
            {
                State = ScrollState.WillStartScrolling;
            }
            else
            {
                State = ScrollState.ScrollingBackInBounds;
            }
        }

        public void Tick()
        {
            switch(State)
            {
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
