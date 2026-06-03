/*
 * COPYRIGHT & LICENSE NOTICE
 * This software is provided for portfolio evaluation and educational purposes only.
 * Unauthorized copying, modification, distribution, or commercial use of this code,
 * via any medium, is strictly prohibited. All rights reserved.
 */

using UnityEngine;
using System.Collections.Generic;

namespace GhostRigAI
{
    /// <summary>
    /// Enforces physical distance limits (Tether Constraints) between the Avatar's root and an anchor point.
    /// Recalculates bone orientations to lean the body toward the tether origin when tension is active.
    /// </summary>
    public static class TetherConstraintIK
    {
        /// <summary>
        /// Recalculates Hips position and bone rotations to respect the maximum tether length constraint.
        /// </summary>
        /// <param name="hipsPosition">The reference Hips root translation (updated if constraint is violated).</param>
        /// <param name="pose">The dictionary of bone rotations to be corrected.</param>
        /// <param name="anchorPoint">The 3D point in space where the tether is anchored.</param>
        /// <param name="maxTetherLength">The maximum allowed distance from the anchor point.</param>
        public static void SolveTether(ref Vector3 hipsPosition, Dictionary<HumanBodyBones, Quaternion> pose, Vector3 anchorPoint, float maxTetherLength)
        {
            if (pose == null) return;

            // 1. Calculate vector from anchor to pelvis
            Vector3 anchorToPelvis = hipsPosition - anchorPoint;
            float currentDistance = anchorToPelvis.magnitude;

            // 2. If the distance exceeds the tether limit, pull the hips position back to the boundary
            if (currentDistance > maxTetherLength && currentDistance > 0.001f)
            {
                Vector3 direction = anchorToPelvis.normalized;
                
                // Reposition Hips root exactly on the constraint sphere boundary
                hipsPosition = anchorPoint + direction * maxTetherLength;

                // 3. Apply angular tension (lean the hips toward the anchor)
                if (pose.TryGetValue(HumanBodyBones.Hips, out Quaternion hipsRot))
                {
                    // Vector pointing from hips back to anchor
                    Vector3 pullDirection = -direction;
                    
                    // Create a rotation that faces the anchor pull
                    Quaternion pullTarget = Quaternion.LookRotation(pullDirection, Vector3.up);
                    
                    // Blend 25% of the pull target into the hips rotation (visual lean/tension)
                    pose[HumanBodyBones.Hips] = Quaternion.Slerp(hipsRot, pullTarget, 0.25f);
                }

                // 4. Apply corrective leg IK tension (rotate legs slightly to react to root pullback)
                ApplyLegTension(pose, direction);
            }
        }

        /// <summary>
        /// Adjusts the leg rotations to visually stretch or bend in response to the tether pull-back direction.
        /// </summary>
        private void ApplyLegTension(Dictionary<HumanBodyBones, Quaternion> pose, Vector3 pullDirection)
        {
            // Rotate the upper legs to counter the pull direction and maintain structural balance
            Quaternion legCorrection = Quaternion.FromToRotation(Vector3.down, -pullDirection);

            // Left Upper Leg
            if (pose.TryGetValue(HumanBodyBones.LeftUpperLeg, out Quaternion lLegRot))
            {
                pose[HumanBodyBones.LeftUpperLeg] = Quaternion.Slerp(lLegRot, legCorrection * lLegRot, 0.15f);
            }

            // Right Upper Leg
            if (pose.TryGetValue(HumanBodyBones.RightUpperLeg, out Quaternion rLegRot))
            {
                pose[HumanBodyBones.RightUpperLeg] = Quaternion.Slerp(rLegRot, legCorrection * rLegRot, 0.15f);
            }
        }
    }
}
