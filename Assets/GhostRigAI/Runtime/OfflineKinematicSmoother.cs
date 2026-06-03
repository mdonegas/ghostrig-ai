/*
 * COPYRIGHT & LICENSE NOTICE
 * This software is provided for portfolio evaluation and educational purposes only.
 * Unauthorized copying, modification, distribution, or commercial use of this code,
 * via any medium, is strictly prohibited. All rights reserved.
 */

using UnityEngine;
using System;
using System.Collections.Generic;

namespace GhostRigAI
{
    /// <summary>
    /// Smooths skeletal animation poses offline by combining a Quaternion One-Euro Filter (adaptive low-pass)
    /// with a second-order Hermite velocity extrapolation using a two-frame cache history.
    /// </summary>
    public class OfflineKinematicSmoother
    {
        // Filter Tuning Parameters
        private readonly float minCutoff;
        private readonly float beta;
        private readonly float dCutoff;

        // Pose history cache for frame-1 and frame-2 to implement Hermite smoothing
        private Dictionary<HumanBodyBones, Quaternion> lastFramePose = new Dictionary<HumanBodyBones, Quaternion>();
        private Dictionary<HumanBodyBones, Quaternion> secondLastFramePose = new Dictionary<HumanBodyBones, Quaternion>();

        // One-Euro Filter instances for each Humanoid bone
        private readonly Dictionary<HumanBodyBones, OneEuroQuaternionFilter> boneFilters = new Dictionary<HumanBodyBones, OneEuroQuaternionFilter>();

        /// <summary>
        /// Instantiates a new Kinematic Smoother with specified One-Euro tuning parameters.
        /// </summary>
        /// <param name="minCutoff">Minimum cutoff frequency in Hz. Lower values reduce jitter at low speeds.</param>
        /// <param name="beta">Speed coefficient. Higher values reduce lag at high speeds.</param>
        /// <param name="dCutoff">Cutoff frequency for the derivative (velocity) filter in Hz.</param>
        public OfflineKinematicSmoother(float minCutoff = 1.0f, float beta = 0.5f, float dCutoff = 1.0f)
        {
            this.minCutoff = minCutoff;
            this.beta = beta;
            this.dCutoff = dCutoff;
        }

        /// <summary>
        /// Clears the temporal cache history. Call this when beginning a new video bake.
        /// </summary>
        public void ResetHistory()
        {
            lastFramePose.Clear();
            secondLastFramePose.Clear();
            boneFilters.Clear();
        }

        /// <summary>
        /// Applies deterministic temporal smoothing on a raw humanoid pose using a static delta step time.
        /// </summary>
        /// <param name="currentRawPose">The raw decoded Pose from the current frame.</param>
        /// <param name="stepTime">Static time delta (1.0f / targetFramerate).</param>
        /// <returns>A smoothed pose mapping bones to filtered Quaternions.</returns>
        public Dictionary<HumanBodyBones, Quaternion> SmoothPose(Dictionary<HumanBodyBones, Quaternion> currentRawPose, float stepTime)
        {
            if (currentRawPose == null)
            {
                throw new ArgumentNullException(nameof(currentRawPose));
            }

            var smoothedPose = new Dictionary<HumanBodyBones, Quaternion>();

            foreach (var kvp in currentRawPose)
            {
                HumanBodyBones bone = kvp.Key;
                Quaternion rawRot = kvp.Value;

                // 1. Initialize filter if not present
                if (!boneFilters.TryGetValue(bone, out var filter))
                {
                    filter = new OneEuroQuaternionFilter(minCutoff, beta, dCutoff);
                    boneFilters[bone] = filter;
                }

                // 2. Apply One-Euro filter (1st order adaptive low-pass)
                Quaternion filteredRot = filter.Filter(rawRot, stepTime);

                // 3. Apply Hermite/Velocity Extrapolation (uses frame-1 and frame-2 history)
                if (lastFramePose.TryGetValue(bone, out Quaternion q1) && secondLastFramePose.TryGetValue(bone, out Quaternion q2))
                {
                    // Compute absolute angular delta/velocity between frame-2 and frame-1
                    // ensuring shortest path rotation delta
                    if (Quaternion.Dot(q2, q1) < 0f)
                    {
                        q2 = new Quaternion(-q2.x, -q2.y, -q2.z, -q2.w);
                    }

                    // Extrapolate rotation based on velocity: extrapolated = q1 * (q2^-1 * q1)
                    Quaternion angularVelocity = q1 * Quaternion.Inverse(q2);
                    Quaternion extrapolatedRot = angularVelocity * q1;

                    // Blend the adaptive low-pass filtered rotation with the extrapolated velocity curve
                    // this removes acceleration spikes (jerk) from noise transitions
                    filteredRot = Quaternion.Slerp(filteredRot, extrapolatedRot, 0.15f);
                }

                smoothedPose[bone] = filteredRot.normalized;
            }

            // Update history cache
            secondLastFramePose = new Dictionary<HumanBodyBones, Quaternion>(lastFramePose);
            lastFramePose = new Dictionary<HumanBodyBones, Quaternion>(smoothedPose);

            return smoothedPose;
        }

        #region Nested Filter Implementations

        /// <summary>
        /// Internal One-Euro Filter wrapper for Unity Quaternion structures.
        /// Handles double-cover hemisphere validation to prevent 180-degree flip artifacts.
        /// </summary>
        private class OneEuroQuaternionFilter
        {
            private readonly OneEuroFloatFilter xFilter;
            private readonly OneEuroFloatFilter yFilter;
            private readonly OneEuroFloatFilter zFilter;
            private readonly OneEuroFloatFilter wFilter;

            private Quaternion lastOutput = Quaternion.identity;
            private bool hasLastOutput = false;

            public OneEuroQuaternionFilter(float minCutoff, float beta, float dCutoff)
            {
                xFilter = new OneEuroFloatFilter(minCutoff, beta, dCutoff);
                yFilter = new OneEuroFloatFilter(minCutoff, beta, dCutoff);
                zFilter = new OneEuroFloatFilter(minCutoff, beta, dCutoff);
                wFilter = new OneEuroFloatFilter(minCutoff, beta, dCutoff);
            }

            public Quaternion Filter(Quaternion input, float dt)
            {
                if (!hasLastOutput)
                {
                    lastOutput = input;
                    hasLastOutput = true;
                    return input;
                }

                // Check dot product to handle Quaternion double-cover representation (q and -q are identical)
                // Negate quaternion components if dot product is negative to prevent interpolation flips
                if (Quaternion.Dot(input, lastOutput) < 0f)
                {
                    input = new Quaternion(-input.x, -input.y, -input.z, -input.w);
                }

                // Filter each component independently
                float x = xFilter.Filter(input.x, dt);
                float y = yFilter.Filter(input.y, dt);
                float z = zFilter.Filter(input.z, dt);
                float w = wFilter.Filter(input.w, dt);

                Quaternion result = new Quaternion(x, y, z, w);
                result.Normalize(); // Normalize to ensure a valid rotation quaternion

                lastOutput = result;
                return result;
            }
        }

        /// <summary>
        /// First-order low-pass filter with adaptive cutoff frequency based on velocity.
        /// </summary>
        private class OneEuroFloatFilter
        {
            private readonly float minCutoff;
            private readonly float beta;
            private readonly float dCutoff;

            private float lastValue = 0f;
            private float lastDeriv = 0f;
            private bool hasLastValue = false;

            public OneEuroFloatFilter(float minCutoff, float beta, float dCutoff)
            {
                this.minCutoff = minCutoff;
                this.beta = beta;
                this.dCutoff = dCutoff;
            }

            public float Filter(float value, float dt)
            {
                if (!hasLastValue)
                {
                    lastValue = value;
                    hasLastValue = true;
                    return value;
                }

                // 1. Calculate raw velocity (derivative)
                float deriv = (value - lastValue) / dt;

                // 2. Filter velocity to reduce high-frequency noise spikes
                float alphaD = CalculateAlpha(dCutoff, dt);
                float filteredDeriv = alphaD * deriv + (1f - alphaD) * lastDeriv;

                // 3. Compute adaptive cutoff frequency based on filtered velocity
                float cutoff = minCutoff + beta * Math.Abs(filteredDeriv);

                // 4. Filter the value using the adaptive cutoff
                float alpha = CalculateAlpha(cutoff, dt);
                float filteredValue = alpha * value + (1f - alpha) * lastValue;

                // Update state variables
                lastValue = filteredValue;
                lastDeriv = filteredDeriv;

                return filteredValue;
            }

            private float CalculateAlpha(float cutoff, float dt)
            {
                // tau = 1.0 / (2 * pi * fc)
                float tau = 1.0f / (2.0f * Mathf.PI * cutoff);
                return dt / (dt + tau);
            }
        }

        #endregion
    }
}
