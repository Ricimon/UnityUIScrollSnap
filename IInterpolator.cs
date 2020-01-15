using UnityEngine;

namespace Ricimon.ScrollSnap
{
    public interface IInterpolator
    {
        float GetInterpolation(float input);
    }

    public class ViscousFluidInterpolator : IInterpolator
    {
        /** Controls the viscous fluid effect (how much of it). */
        private readonly float VISCOUS_FLUID_SCALE = 8.0f;
        private readonly float VISCOUS_FLUID_NORMALIZE;
        private readonly float VISCOUS_FLUID_OFFSET;

        public ViscousFluidInterpolator()
        {
            // must be set to 1.0 (used in viscousFluid())
            VISCOUS_FLUID_NORMALIZE = 1.0f / viscousFluid(1.0f);
            // account for very small floating-point error
            VISCOUS_FLUID_OFFSET = 1.0f - VISCOUS_FLUID_NORMALIZE * viscousFluid(1.0f);
        }

        private float viscousFluid(float x)
        {
            x *= VISCOUS_FLUID_SCALE;
            if (x < 1.0f)
            {
                x -= (1.0f - Mathf.Exp(-x));
            }
            else
            {
                float start = 0.36787944117f;   // 1/e == exp(-1)
                x = 1.0f - Mathf.Exp(1.0f - x);
                x = start + x * (1.0f - start);
            }
            return x;
        }

        public float GetInterpolation(float input)
        {
            float interpolated = VISCOUS_FLUID_NORMALIZE * viscousFluid(input);
            if (interpolated > 0)
            {
                return interpolated + VISCOUS_FLUID_OFFSET;
            }
            return interpolated;
        }
    }
}
