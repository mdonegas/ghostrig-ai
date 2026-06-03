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
    /// Serves as the animation tracking target for the RL training environment.
    /// Plays the raw, jittery pose data and caches bone transforms for comparison.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class GhostTarget : MonoBehaviour
    {
        private Animator targetAnimator;
        private readonly Dictionary<HumanBodyBones, Transform> boneCache = new Dictionary<HumanBodyBones, Transform>();

        private void Awake()
        {
            targetAnimator = GetComponent<Animator>();
            CacheBoneTransforms();
        }

        /// <summary>
        /// Populates the fast-lookup dictionary for all standard humanoid bones.
        /// </summary>
        private void CacheBoneTransforms()
        {
            boneCache.Clear();

            // Loop through all standard humanoid bone enums and cache transforms
            foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;

                Transform boneTransform = targetAnimator.GetBoneTransform(bone);
                if (boneTransform != null)
                {
                    boneCache[bone] = boneTransform;
                }
            }
        }

        /// <summary>
        /// Returns the target bone Transform for a specific Humanoid bone.
        /// </summary>
        /// <param name="bone">The humanoid bone type.</param>
        /// <returns>The target bone Transform, or null if not found.</returns>
        public Transform GetTargetBone(HumanBodyBones bone)
        {
            if (boneCache.TryGetValue(bone, out Transform boneTransform))
            {
                return boneTransform;
            }
            return null;
        }

        /// <summary>
        /// Resets the target's rotation and position.
        /// </summary>
        /// <param name="startPosition">Reset position.</param>
        /// <param name="startRotation">Reset rotation.</param>
        public void ResetPose(Vector3 startPosition, Quaternion startRotation)
        {
            transform.position = startPosition;
            transform.rotation = startRotation;
        }
    }
}
