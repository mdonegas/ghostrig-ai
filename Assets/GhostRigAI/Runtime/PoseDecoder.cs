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
    /// Decodes flat neural network predictions into 3D Quaternions relative to the Unity Humanoid bone structure.
    /// Implements coordinate extraction and vector math to calculate absolute joint orientations.
    /// </summary>
    public static class PoseDecoder
    {
        // The model predicts 17 key joints, each having X, Y, Z coordinates (17 * 3 = 51 floats)
        private const int ExpectedFloats = 51;
        private const int JointCount = 17;

        /// <summary>
        /// Translates a flat float array containing 3D coordinate predictions into Quaternions for each Humanoid bone.
        /// </summary>
        /// <param name="rawPredictions">The flat float array (51 elements) from Phase 2.</param>
        /// <returns>A dictionary mapping Humanoid bones to their calculated orientation Quaternions.</returns>
        public static Dictionary<HumanBodyBones, Quaternion> DecodePose(float[] rawPredictions)
        {
            if (rawPredictions == null)
            {
                throw new ArgumentNullException(nameof(rawPredictions), "Raw predictions cannot be null.");
            }

            var pose = new Dictionary<HumanBodyBones, Quaternion>();

            // Parse coordinate vectors
            Vector3[] joints = ParseJointPositions(rawPredictions);

            // Compute rotations using 3D vector algebra relative to standard T-pose bind directions
            
            // 1. Hips (Root orientation)
            // Up vector points from Hips to Spine
            Vector3 hipsUp = (joints[1] - joints[0]).normalized; 
            // Right vector points from Left Leg to Right Leg
            Vector3 hipsRight = (joints[14] - joints[11]).normalized; 
            // Forward vector is the cross product of Right and Up
            Vector3 hipsForward = Vector3.Cross(hipsRight, hipsUp).normalized;
            if (hipsForward == Vector3.zero) hipsForward = Vector3.forward;
            pose[HumanBodyBones.Hips] = Quaternion.LookRotation(hipsForward, hipsUp);

            // 2. Spine & Upper Body
            Vector3 spineDir = (joints[2] - joints[1]).normalized; // Spine -> Chest
            pose[HumanBodyBones.Spine] = Quaternion.FromToRotation(Vector3.up, spineDir);

            Vector3 chestDir = (joints[3] - joints[2]).normalized; // Chest -> Neck
            pose[HumanBodyBones.Chest] = Quaternion.FromToRotation(Vector3.up, chestDir);

            Vector3 neckDir = (joints[4] - joints[3]).normalized; // Neck -> Head
            pose[HumanBodyBones.Neck] = Quaternion.FromToRotation(Vector3.up, neckDir);

            // Head (inherits neck direction)
            pose[HumanBodyBones.Head] = pose[HumanBodyBones.Neck];

            // 3. Left Arm (Bind direction: Vector3.left)
            Vector3 leftUpperArmDir = (joints[6] - joints[5]).normalized; // Shoulder -> Elbow
            pose[HumanBodyBones.LeftUpperArm] = Quaternion.FromToRotation(Vector3.left, leftUpperArmDir);

            Vector3 leftLowerArmDir = (joints[7] - joints[6]).normalized; // Elbow -> Wrist
            pose[HumanBodyBones.LeftLowerArm] = Quaternion.FromToRotation(Vector3.left, leftLowerArmDir);

            // 4. Right Arm (Bind direction: Vector3.right)
            Vector3 rightUpperArmDir = (joints[9] - joints[8]).normalized; // Shoulder -> Elbow
            pose[HumanBodyBones.RightUpperArm] = Quaternion.FromToRotation(Vector3.right, rightUpperArmDir);

            Vector3 rightLowerArmDir = (joints[10] - joints[9]).normalized; // Elbow -> Wrist
            pose[HumanBodyBones.RightLowerArm] = Quaternion.FromToRotation(Vector3.right, rightLowerArmDir);

            // 5. Left Leg (Bind direction: Vector3.down)
            Vector3 leftUpperLegDir = (joints[12] - joints[11]).normalized; // Hip -> Knee
            pose[HumanBodyBones.LeftUpperLeg] = Quaternion.FromToRotation(Vector3.down, leftUpperLegDir);

            Vector3 leftLowerLegDir = (joints[13] - joints[12]).normalized; // Knee -> Foot
            pose[HumanBodyBones.LeftLowerLeg] = Quaternion.FromToRotation(Vector3.down, leftLowerLegDir);

            // 6. Right Leg (Bind direction: Vector3.down)
            Vector3 rightUpperLegDir = (joints[15] - joints[14]).normalized; // Hip -> Knee
            pose[HumanBodyBones.RightUpperLeg] = Quaternion.FromToRotation(Vector3.down, rightUpperLegDir);

            Vector3 rightLowerLegDir = (joints[16] - joints[15]).normalized; // Knee -> Foot
            pose[HumanBodyBones.RightLowerLeg] = Quaternion.FromToRotation(Vector3.down, rightLowerLegDir);

            return pose;
        }

        /// <summary>
        /// Safely extracts 3D Vector3 positions from the flat float array.
        /// Handles array sizing issues gracefully by filling missing joints with default positions.
        /// </summary>
        private static Vector3[] ParseJointPositions(float[] flatArray)
        {
            Vector3[] positions = new Vector3[JointCount];

            // If the array is empty, populate with a default T-pose model structure
            if (flatArray.Length < ExpectedFloats)
            {
                Debug.LogWarning($"[PoseDecoder] Raw predictions array size ({flatArray.Length}) is less than expected ({ExpectedFloats}). Populating with default T-pose.");
                return GenerateDefaultTPose();
            }

            for (int i = 0; i < JointCount; i++)
            {
                float x = flatArray[i * 3 + 0];
                float y = flatArray[i * 3 + 1];
                float z = flatArray[i * 3 + 2];
                positions[i] = new Vector3(x, y, z);
            }

            return positions;
        }

        /// <summary>
        /// Generates a fallback 3D joint coordinate map representing a standard T-pose.
        /// </summary>
        private static Vector3[] GenerateDefaultTPose()
        {
            Vector3[] tpose = new Vector3[JointCount];

            tpose[0]  = new Vector3(0.0f, 1.0f, 0.0f);   // Hips
            tpose[1]  = new Vector3(0.0f, 1.2f, 0.0f);   // Spine
            tpose[2]  = new Vector3(0.0f, 1.4f, 0.0f);   // Chest
            tpose[3]  = new Vector3(0.0f, 1.6f, 0.0f);   // Neck
            tpose[4]  = new Vector3(0.0f, 1.7f, 0.0f);   // Head
            
            tpose[5]  = new Vector3(-0.2f, 1.4f, 0.0f);  // L Shoulder
            tpose[6]  = new Vector3(-0.5f, 1.4f, 0.0f);  // L Elbow
            tpose[7]  = new Vector3(-0.8f, 1.4f, 0.0f);  // L Wrist
            
            tpose[8]  = new Vector3(0.2f, 1.4f, 0.0f);   // R Shoulder
            tpose[9]  = new Vector3(0.5f, 1.4f, 0.0f);   // R Elbow
            tpose[10] = new Vector3(0.8f, 1.4f, 0.0f);   // R Wrist
            
            tpose[11] = new Vector3(-0.15f, 1.0f, 0.0f); // L Hip
            tpose[12] = new Vector3(-0.15f, 0.5f, 0.0f); // L Knee
            tpose[13] = new Vector3(-0.15f, 0.0f, 0.0f); // L Foot
            
            tpose[14] = new Vector3(0.15f, 1.0f, 0.0f);  // R Hip
            tpose[15] = new Vector3(0.15f, 0.5f, 0.0f);  // R Knee
            tpose[16] = new Vector3(0.15f, 0.0f, 0.0f);  // R Foot

            return tpose;
        }

        private static readonly Dictionary<HumanBodyBones, HumanBodyBones> BoneParents = new Dictionary<HumanBodyBones, HumanBodyBones>
        {
            { HumanBodyBones.Spine, HumanBodyBones.Hips },
            { HumanBodyBones.Chest, HumanBodyBones.Spine },
            { HumanBodyBones.Neck, HumanBodyBones.Chest },
            { HumanBodyBones.Head, HumanBodyBones.Neck },
            
            { HumanBodyBones.LeftUpperArm, HumanBodyBones.Chest },
            { HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftUpperArm },
            
            { HumanBodyBones.RightUpperArm, HumanBodyBones.Chest },
            { HumanBodyBones.RightLowerArm, HumanBodyBones.RightUpperArm },
            
            { HumanBodyBones.LeftUpperLeg, HumanBodyBones.Hips },
            { HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftUpperLeg },
            
            { HumanBodyBones.RightUpperLeg, HumanBodyBones.Hips },
            { HumanBodyBones.RightLowerLeg, HumanBodyBones.RightUpperLeg },
            
            // Left Hand Fingers
            { HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbProximal },
            { HumanBodyBones.LeftThumbDistal, HumanBodyBones.LeftThumbIntermediate },
            { HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexProximal },
            { HumanBodyBones.LeftIndexDistal, HumanBodyBones.LeftIndexIntermediate },
            { HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleProximal },
            { HumanBodyBones.LeftMiddleDistal, HumanBodyBones.LeftMiddleIntermediate },
            { HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingProximal },
            { HumanBodyBones.LeftRingDistal, HumanBodyBones.LeftRingIntermediate },
            { HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleProximal },
            { HumanBodyBones.LeftLittleDistal, HumanBodyBones.LeftLittleIntermediate },
            
            // Right Hand Fingers
            { HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbProximal },
            { HumanBodyBones.RightThumbDistal, HumanBodyBones.RightThumbIntermediate },
            { HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexProximal },
            { HumanBodyBones.RightIndexDistal, HumanBodyBones.RightIndexIntermediate },
            { HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleProximal },
            { HumanBodyBones.RightMiddleDistal, HumanBodyBones.RightMiddleIntermediate },
            { HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingProximal },
            { HumanBodyBones.RightRingDistal, HumanBodyBones.RightRingIntermediate },
            { HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleProximal },
            { HumanBodyBones.RightLittleDistal, HumanBodyBones.RightLittleIntermediate }
        };

        /// <summary>
        /// Converts absolute/world humanoid orientations into parent-relative local orientations in-place.
        /// </summary>
        /// <param name="pose">The dictionary of bone rotations to convert.</param>
        public static void ConvertToLocalRotations(Dictionary<HumanBodyBones, Quaternion> pose)
        {
            if (pose == null) return;

            // Clone absolute rotations so we can read original values during conversion
            var absPose = new Dictionary<HumanBodyBones, Quaternion>(pose);

            foreach (var kvp in absPose)
            {
                HumanBodyBones bone = kvp.Key;
                Quaternion absRot = kvp.Value;

                if (BoneParents.TryGetValue(bone, out HumanBodyBones parentBone))
                {
                    if (absPose.TryGetValue(parentBone, out Quaternion parentAbsRot))
                    {
                        pose[bone] = Quaternion.Inverse(parentAbsRot) * absRot;
                    }
                }
            }
        }
    }
}
