/*
 * COPYRIGHT & LICENSE NOTICE
 * This software is provided for portfolio evaluation and educational purposes only.
 * Unauthorized copying, modification, distribution, or commercial use of this code,
 * via any medium, is strictly prohibited. All rights reserved.
 */

using UnityEngine;
using Unity.InferenceEngine;
using System;
using System.Collections.Generic;

namespace GhostRigAI
{
    /// <summary>
    /// Isolates local wrist regions from the video frame using normalized coordinates,
    /// executes a dedicated Hand Pose ONNX worker, and decodes detailed finger rotations.
    /// </summary>
    public class HandMicroCropper : IDisposable
    {
        private Model runtimeModel;
        private Worker worker;
        private const int TargetSize = 256;
        private const int TargetChannels = 3;

        /// <summary>
        /// Initializes the Hand Micro-Cropper with a dedicated hand model.
        /// </summary>
        /// <param name="handModelAsset">ONNX model asset for hand tracking.</param>
        public HandMicroCropper(ModelAsset handModelAsset)
        {
            if (handModelAsset == null)
            {
                throw new ArgumentNullException(nameof(handModelAsset));
            }

            runtimeModel = ModelLoader.Load(handModelAsset);
            worker = new Worker(runtimeModel, BackendType.GPUCompute);
        }

        /// <summary>
        /// Crops the wrist region from the source frame, runs inference, and decodes the detailed finger rotations.
        /// </summary>
        /// <param name="sourceFrame">The source frame texture (usually 1:1 cropped from video).</param>
        /// <param name="wristCoords">Normalized 2D screen coordinates of the wrist [0.0 to 1.0].</param>
        /// <param name="isLeftHand">True to map output to Left hand bones; False for Right hand bones.</param>
        /// <returns>A dictionary of parent-relative finger rotations.</returns>
        public Dictionary<HumanBodyBones, Quaternion> ProcessHand(Texture sourceFrame, Vector2 wristCoords, bool isLeftHand)
        {
            if (sourceFrame == null)
            {
                throw new ArgumentNullException(nameof(sourceFrame));
            }

            var fingerRotations = new Dictionary<HumanBodyBones, Quaternion>();

            // 1. Calculate aspect-ratio-corrected bounding box scale and offset centered at the wrist
            float aspect = (float)sourceFrame.width / sourceFrame.height;
            float cropWidth = 0.25f; // Bounding box width (25% of screen width)
            float cropHeight = cropWidth * aspect; // Scale height to ensure pixel-perfect square crop

            Vector2 scale = new Vector2(cropWidth, cropHeight);
            Vector2 offset = new Vector2(wristCoords.x - cropWidth * 0.5f, wristCoords.y - cropHeight * 0.5f);

            // Clamp offset to keep crop within image boundaries
            offset.x = Mathf.Clamp(offset.x, 0.0f, 1.0f - scale.x);
            offset.y = Mathf.Clamp(offset.y, 0.0f, 1.0f - scale.y);

            // 2. Allocate temporary RenderTexture and perform GPU blitting
            RenderTexture croppedRT = RenderTexture.GetTemporary(TargetSize, TargetSize, 0, RenderTextureFormat.ARGB32);
            croppedRT.name = isLeftHand ? "GhostRig_LeftHand_Crop_RT" : "GhostRig_RightHand_Crop_RT";
            croppedRT.filterMode = FilterMode.Bilinear;
            croppedRT.wrapMode = TextureWrapMode.Clamp;

            Graphics.Blit(sourceFrame, croppedRT, scale, offset);

            Tensor<float> inputTensor = null;
            Tensor<float> outputTensor = null;

            try
            {
                // 3. Convert crop texture to Tensor
                inputTensor = new Tensor<float>(new TensorShape(1, TargetChannels, TargetSize, TargetSize));
                TextureConverter.ToTensor(croppedRT, inputTensor);

                // 4. Run inference
                worker.Schedule(inputTensor);
                worker.FinishExecution();

                // 5. Retrieve output keypoints
                outputTensor = worker.PeekOutput() as Tensor<float>;
                float[] handJointsFlat = outputTensor.DownloadToArray();

                // 6. Decode joint positions to parent-relative finger rotations
                DecodeFingerRotations(handJointsFlat, isLeftHand, fingerRotations);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HandMicroCropper] Error during hand inference: {ex.Message}");
            }
            finally
            {
                // CRITICAL Memory Cleanup to prevent VRAM accumulation
                if (inputTensor != null) inputTensor.Dispose();
                if (outputTensor != null) outputTensor.Dispose();
                RenderTexture.ReleaseTemporary(croppedRT);
            }

            return fingerRotations;
        }

        /// <summary>
        /// Decodes a flat prediction array of 21 hand joints (63 floats) into parent-relative finger Quaternions.
        /// </summary>
        private void DecodeFingerRotations(float[] flatJoints, bool isLeftHand, Dictionary<HumanBodyBones, Quaternion> rotMap)
        {
            // MediaPipe Hand Pose returns 21 joints:
            // 0: Wrist, 1-4: Thumb, 5-8: Index, 9-12: Middle, 13-16: Ring, 17-20: Pinky
            int expectedValues = 21 * 3;
            Vector3[] joints = new Vector3[21];

            if (flatJoints == null || flatJoints.Length < expectedValues)
            {
                // Gracefully fallback to default flat open hand rotations if predictions are incomplete
                PopulateDefaultHand(isLeftHand, rotMap);
                return;
            }

            for (int i = 0; i < 21; i++)
            {
                joints[i] = new Vector3(flatJoints[i * 3 + 0], flatJoints[i * 3 + 1], flatJoints[i * 3 + 2]);
            }

            // Reference directions for left/right fingers
            Vector3 armDir = isLeftHand ? Vector3.left : Vector3.right;

            // Map index/middle/ring/little fingers using FromToRotation along the bone segments
            // Index Finger
            Vector3 indexProx = (joints[6] - joints[5]).normalized;
            Vector3 indexInt = (joints[7] - joints[6]).normalized;
            Vector3 indexDist = (joints[8] - joints[7]).normalized;
            
            rotMap[isLeftHand ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal] = Quaternion.FromToRotation(armDir, indexProx);
            rotMap[isLeftHand ? HumanBodyBones.LeftIndexIntermediate : HumanBodyBones.RightIndexIntermediate] = Quaternion.FromToRotation(armDir, indexInt);
            rotMap[isLeftHand ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal] = Quaternion.FromToRotation(armDir, indexDist);

            // Middle Finger
            Vector3 middleProx = (joints[10] - joints[9]).normalized;
            Vector3 middleInt = (joints[11] - joints[10]).normalized;
            Vector3 middleDist = (joints[12] - joints[11]).normalized;
            
            rotMap[isLeftHand ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal] = Quaternion.FromToRotation(armDir, middleProx);
            rotMap[isLeftHand ? HumanBodyBones.LeftMiddleIntermediate : HumanBodyBones.RightMiddleIntermediate] = Quaternion.FromToRotation(armDir, middleInt);
            rotMap[isLeftHand ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal] = Quaternion.FromToRotation(armDir, middleDist);

            // Ring Finger
            Vector3 ringProx = (joints[14] - joints[13]).normalized;
            Vector3 ringInt = (joints[15] - joints[14]).normalized;
            Vector3 ringDist = (joints[16] - joints[15]).normalized;
            
            rotMap[isLeftHand ? HumanBodyBones.LeftRingProximal : HumanBodyBones.RightRingProximal] = Quaternion.FromToRotation(armDir, ringProx);
            rotMap[isLeftHand ? HumanBodyBones.LeftRingIntermediate : HumanBodyBones.RightRingIntermediate] = Quaternion.FromToRotation(armDir, ringInt);
            rotMap[isLeftHand ? HumanBodyBones.LeftRingDistal : HumanBodyBones.RightRingDistal] = Quaternion.FromToRotation(armDir, ringDist);

            // Little Finger (Pinky)
            Vector3 littleProx = (joints[18] - joints[17]).normalized;
            Vector3 littleInt = (joints[19] - joints[18]).normalized;
            Vector3 littleDist = (joints[20] - joints[19]).normalized;
            
            rotMap[isLeftHand ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal] = Quaternion.FromToRotation(armDir, littleProx);
            rotMap[isLeftHand ? HumanBodyBones.LeftLittleIntermediate : HumanBodyBones.RightLittleIntermediate] = Quaternion.FromToRotation(armDir, littleInt);
            rotMap[isLeftHand ? HumanBodyBones.LeftLittleDistal : HumanBodyBones.RightLittleDistal] = Quaternion.FromToRotation(armDir, littleDist);

            // Thumb
            Vector3 thumbProx = (joints[2] - joints[1]).normalized;
            Vector3 thumbInt = (joints[3] - joints[2]).normalized;
            Vector3 thumbDist = (joints[4] - joints[3]).normalized;
            
            rotMap[isLeftHand ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal] = Quaternion.FromToRotation(armDir, thumbProx);
            rotMap[isLeftHand ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate] = Quaternion.FromToRotation(armDir, thumbInt);
            rotMap[isLeftHand ? HumanBodyBones.LeftThumbDistal : HumanBodyBones.RightThumbDistal] = Quaternion.FromToRotation(armDir, thumbDist);
        }

        private void PopulateDefaultHand(bool isLeftHand, Dictionary<HumanBodyBones, Quaternion> rotMap)
        {
            HumanBodyBones[] handBones = new HumanBodyBones[]
            {
                isLeftHand ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal,
                isLeftHand ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate,
                isLeftHand ? HumanBodyBones.LeftThumbDistal : HumanBodyBones.RightThumbDistal,
                isLeftHand ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal,
                isLeftHand ? HumanBodyBones.LeftIndexIntermediate : HumanBodyBones.RightIndexIntermediate,
                isLeftHand ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal,
                isLeftHand ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal,
                isLeftHand ? HumanBodyBones.LeftMiddleIntermediate : HumanBodyBones.RightMiddleIntermediate,
                isLeftHand ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal,
                isLeftHand ? HumanBodyBones.LeftRingProximal : HumanBodyBones.RightRingProximal,
                isLeftHand ? HumanBodyBones.LeftRingIntermediate : HumanBodyBones.RightRingIntermediate,
                isLeftHand ? HumanBodyBones.LeftRingDistal : HumanBodyBones.RightRingDistal,
                isLeftHand ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal,
                isLeftHand ? HumanBodyBones.LeftLittleIntermediate : HumanBodyBones.RightLittleIntermediate,
                isLeftHand ? HumanBodyBones.LeftLittleDistal : HumanBodyBones.RightLittleDistal
            };

            foreach (var bone in handBones)
            {
                rotMap[bone] = Quaternion.identity;
            }
        }

        public void Dispose()
        {
            if (worker != null)
            {
                worker.Dispose();
                worker = null;
            }
            runtimeModel = null;
        }
    }
}
