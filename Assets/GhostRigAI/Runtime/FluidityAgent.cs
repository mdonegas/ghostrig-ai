/*
 * COPYRIGHT & LICENSE NOTICE
 * This software is provided for portfolio evaluation and educational purposes only.
 * Unauthorized copying, modification, distribution, or commercial use of this code,
 * via any medium, is strictly prohibited. All rights reserved.
 */

using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using System.Collections.Generic;

namespace GhostRigAI
{
    /// <summary>
    /// Represents a controlled muscle joint in the humanoid ragdoll structure.
    /// Tracks physics history for derivative acceleration calculations (Jerk).
    /// </summary>
    [System.Serializable]
    public class MuscleJoint
    {
        public HumanBodyBones bone;
        public ConfigurableJoint joint;
        public Rigidbody rigidbody;
        
        [HideInInspector] public Vector3 prevAngularVelocity;
        [HideInInspector] public Vector3 prevAngularAcceleration;
    }

    /// <summary>
    /// Reinforcement Learning Agent that learns to control ragdoll muscles
    /// to follow a noisy target motion while minimizing high-frequency jitter (Jerk).
    /// </summary>
    public class FluidityAgent : Agent
    {
        [Header("Target & Environment References")]
        [Tooltip("The reference target playing the raw, noisy AI animation.")]
        public GhostTarget ghostTarget;
        
        [Tooltip("The root pelvis transform to monitor balance.")]
        public Transform hipsTransform;
        
        [Tooltip("Episode ends if Hips fall below this world Y position.")]
        public float fallThreshold = 0.5f;

        [Header("Muscle Definition")]
        public List<MuscleJoint> muscles = new List<MuscleJoint>();

        [Header("Reward Tuning Weights")]
        public float accuracyWeight = 0.1f;
        public float jitterPenaltyWeight = 0.0001f;
        public float energyPenaltyWeight = 0.001f;

        /// <summary>
        /// Cache references and configure initial joint drive strengths.
        /// </summary>
        public override void Initialize()
        {
            foreach (var muscle in muscles)
            {
                if (muscle.rigidbody == null && muscle.joint != null)
                {
                    muscle.rigidbody = muscle.joint.GetComponent<Rigidbody>();
                }
            }
        }

        /// <summary>
        /// Resets the velocities and tracking variables of all muscles at the start of each episode.
        /// </summary>
        public override void OnEpisodeBegin()
        {
            foreach (var muscle in muscles)
            {
                if (muscle.rigidbody != null)
                {
                    muscle.rigidbody.linearVelocity = Vector3.zero;
                    muscle.rigidbody.angularVelocity = Vector3.zero;
                }
                muscle.prevAngularVelocity = Vector3.zero;
                muscle.prevAngularAcceleration = Vector3.zero;
            }
        }

        /// <summary>
        /// Gathers relative coordinate and velocity state observations for ML-Agents.
        /// </summary>
        public override void CollectObservations(VectorSensor sensor)
        {
            if (ghostTarget == null) return;

            // Root relative positions and velocities
            sensor.AddObservation(hipsTransform.localPosition);
            if (hipsTransform.TryGetComponent<Rigidbody>(out var hipsRb))
            {
                sensor.AddObservation(hipsRb.linearVelocity);
                sensor.AddObservation(hipsRb.angularVelocity);
            }
            else
            {
                sensor.AddObservation(Vector3.zero);
                sensor.AddObservation(Vector3.zero);
            }

            // Muscle observations
            foreach (var muscle in muscles)
            {
                if (muscle.joint == null) continue;

                // 1. Local rotation Quaternion (4 observations)
                sensor.AddObservation(muscle.joint.transform.localRotation);

                // 2. Local angular velocity (3 observations)
                sensor.AddObservation(muscle.rigidbody != null ? muscle.rigidbody.angularVelocity : Vector3.zero);

                // 3. Delta rotation difference relative to the target bone (4 observations)
                Transform targetBone = ghostTarget.GetTargetBone(muscle.bone);
                if (targetBone != null)
                {
                    Quaternion deltaRot = Quaternion.Inverse(muscle.joint.transform.rotation) * targetBone.rotation;
                    sensor.AddObservation(deltaRot);
                }
                else
                {
                    sensor.AddObservation(Quaternion.identity);
                }
            }
        }

        /// <summary>
        /// Converts continuous muscle actions to motor torque and executes reward shaping algorithms.
        /// </summary>
        public override void OnActionReceived(ActionBuffers actions)
        {
            if (ghostTarget == null) return;

            int actionIndex = 0;
            float totalActionForce = 0.0f;
            float totalJerk = 0.0f;
            float totalMatchScore = 0.0f;

            // 1. Process Actions (continuous floats map target rotations of muscle joints)
            foreach (var muscle in muscles)
            {
                if (muscle.joint == null) continue;

                // Retrieve pitch, yaw, roll control values
                float pitch = actions.ContinuousActions[actionIndex++];
                float yaw = actions.ContinuousActions[actionIndex++];
                float roll = actions.ContinuousActions[actionIndex++];

                // Accumulate action energy (torque regularization)
                totalActionForce += (pitch * pitch) + (yaw * yaw) + (roll * roll);

                // Translate action value [-1.0, 1.0] to joint Euler rotation range [-60, 60] degrees
                Vector3 targetEuler = new Vector3(pitch, yaw, roll) * 60f;
                muscle.joint.targetRotation = Quaternion.Euler(targetEuler);

                // Calculate angular velocity, acceleration, and jerk (acceleration derivative)
                if (muscle.rigidbody != null)
                {
                    Vector3 angularVelocity = muscle.rigidbody.angularVelocity;
                    Vector3 angularAcceleration = (angularVelocity - muscle.prevAngularVelocity) / Time.fixedDeltaTime;
                    Vector3 angularJerk = (angularAcceleration - muscle.prevAngularAcceleration) / Time.fixedDeltaTime;

                    totalJerk += angularJerk.sqrMagnitude;

                    // Update physics derivatives histories
                    muscle.prevAngularVelocity = angularVelocity;
                    muscle.prevAngularAcceleration = angularAcceleration;
                }

                // Calculate bone orientation accuracy score compared to the target bone
                Transform targetBone = ghostTarget.GetTargetBone(muscle.bone);
                if (targetBone != null)
                {
                    float angleDiff = Quaternion.Angle(muscle.joint.transform.rotation, targetBone.rotation);
                    
                    // Normalize score: 1.0 if perfectly aligned, 0.0 if 180 degrees offset
                    float matchScore = 1.0f - (angleDiff / 180.0f);
                    totalMatchScore += matchScore;
                }
            }

            // 2. Apply Reward Shaping

            // A. Accuracy Reward (+)
            float avgMatchScore = muscles.Count > 0 ? totalMatchScore / muscles.Count : 0.0f;
            AddReward(avgMatchScore * accuracyWeight);

            // B. Jitter Penalty (-)
            float avgJerk = muscles.Count > 0 ? totalJerk / muscles.Count : 0.0f;
            AddReward(-avgJerk * jitterPenaltyWeight);

            // C. Energy Penalty (-)
            AddReward(-totalActionForce * energyPenaltyWeight);

            // 3. Monitor Fall Termination Conditions
            if (hipsTransform != null && hipsTransform.position.y < fallThreshold)
            {
                // Apply a massive balance-loss penalty and end episode
                AddReward(-15.0f);
                EndEpisode();
            }
        }

        /// <summary>
        /// Standard keyboard/joystick manual override for environment debugging and heuristics.
        /// </summary>
        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var continuousActions = actionsOut.ContinuousActions;
            for (int i = 0; i < continuousActions.Length; i++)
            {
                // Inject minor noise to verify musculoskeletal responses manually
                continuousActions[i] = UnityEngine.Random.Range(-0.2f, 0.2f);
            }
        }
    }
}
