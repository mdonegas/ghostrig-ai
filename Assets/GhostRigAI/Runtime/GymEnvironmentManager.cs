/*
 * COPYRIGHT & LICENSE NOTICE
 * This software is provided for portfolio evaluation and educational purposes only.
 * Unauthorized copying, modification, distribution, or commercial use of this code,
 * via any medium, is strictly prohibited. All rights reserved.
 */

using UnityEngine;

namespace GhostRigAI
{
    /// <summary>
    /// Coordinates environmental resets for the GhostRig RL Training Gym.
    /// Safely synchronizes poses and clears unmanaged physical momentum between episodes.
    /// </summary>
    public class GymEnvironmentManager : MonoBehaviour
    {
        [Header("Entity References")]
        public FluidityAgent agent;
        public GhostTarget target;

        // Cached starting transforms
        private Vector3 agentStartPos;
        private Quaternion agentStartRot;
        private Vector3 targetStartPos;
        private Quaternion targetStartRot;

        private void Awake()
        {
            // Cache starting locations
            if (agent != null)
            {
                agentStartPos = agent.transform.position;
                agentStartRot = agent.transform.rotation;
            }

            if (target != null)
            {
                targetStartPos = target.transform.position;
                targetStartRot = target.transform.rotation;
            }
        }

        /// <summary>
        /// Resets both target and physical agent transforms to starting states
        /// and halts all accumulated linear and angular momentum of the ragdoll bodies.
        /// </summary>
        public void ResetEnvironment()
        {
            // 1. Reset ghost target pose
            if (target != null)
            {
                target.ResetPose(targetStartPos, targetStartRot);
            }

            // 2. Reset agent ragdoll pose
            if (agent != null)
            {
                agent.transform.position = agentStartPos;
                agent.transform.rotation = agentStartRot;

                // Reset physical rigidbodies to eliminate momentum carry-over between episodes
                Rigidbody[] ragdollRbs = agent.GetComponentsInChildren<Rigidbody>();
                foreach (Rigidbody rb in ragdollRbs)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
        }
    }
}
