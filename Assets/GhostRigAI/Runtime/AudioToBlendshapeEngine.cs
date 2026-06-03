/*
 * COPYRIGHT & LICENSE NOTICE
 * This software is provided for portfolio evaluation and educational purposes only.
 * Unauthorized copying, modification, distribution, or commercial use of this code,
 * via any medium, is strictly prohibited. All rights reserved.
 */

using UnityEngine;
using Unity.Sentis;
using System;
using System.Collections.Generic;

namespace GhostRigAI
{
    /// <summary>
    /// Acoustic-to-facial animation engine for GhostRig AI Phase 5.
    /// Extracts localized audio segments and runs inference to predict facial blendshape weights.
    /// </summary>
    public class AudioToBlendshapeEngine : IDisposable
    {
        private Model runtimeModel;
        private IWorker worker;
        private const int SampleWindowSize = 1024; // Standard context window for acoustic extraction

        // Standard ARKit Facial Blendshape mapping (52 shape indices or subset)
        private static readonly string[] BlendshapeNames = new string[]
        {
            "jawOpen", "jawLeft", "jawRight", "jawForward",
            "mouthClose", "mouthFunnel", "mouthPucker", "mouthLeft", "mouthRight",
            "mouthSmileLeft", "mouthSmileRight", "mouthFrownLeft", "mouthFrownRight",
            "mouthDimpleLeft", "mouthDimpleRight", "mouthStretchLeft", "mouthStretchRight",
            "mouthRollLower", "mouthRollUpper", "mouthShrugLower", "mouthShrugUpper",
            "mouthPressLeft", "mouthPressRight", "mouthLowerDownLeft", "mouthLowerDownRight",
            "mouthUpperUpLeft", "mouthUpperUpRight",
            "cheekPuff", "cheekSquintLeft", "cheekSquintRight",
            "noseSneerLeft", "noseSneerRight", "tongueOut",
            "eyeBlinkLeft", "eyeBlinkRight", "eyeLookDownLeft", "eyeLookDownRight",
            "eyeLookInLeft", "eyeLookInRight", "eyeLookOutLeft", "eyeLookOutRight",
            "eyeLookUpLeft", "eyeLookUpRight", "eyeSquintLeft", "eyeSquintRight",
            "eyeWideLeft", "eyeWideRight",
            "browDownLeft", "browDownRight", "browInnerUp", "browOuterUpLeft", "browOuterUpRight"
        };

        /// <summary>
        /// Initializes the Audio-to-Blendshape engine.
        /// </summary>
        /// <param name="faceModelAsset">ONNX model asset for acoustic blendshape prediction.</param>
        public AudioToBlendshapeEngine(ModelAsset faceModelAsset)
        {
            if (faceModelAsset == null)
            {
                throw new ArgumentNullException(nameof(faceModelAsset));
            }

            runtimeModel = ModelLoader.Load(faceModelAsset);
            worker = WorkerFactory.CreateWorker(BackendType.GPUCompute, runtimeModel);
        }

        /// <summary>
        /// Samples the voice track, runs the acoustic model, and decodes the blendshape weights for the current frame.
        /// </summary>
        /// <param name="voiceTrack">The source AudioClip voice track.</param>
        /// <param name="currentFrame">The current frame index in the video.</param>
        /// <param name="videoFramerate">The framerate of the source video clip.</param>
        /// <returns>A dictionary mapping blendshape names to their target weights [0.0f to 1.0f].</returns>
        public Dictionary<string, float> ProcessAudio(AudioClip voiceTrack, int currentFrame, float videoFramerate)
        {
            var blendshapes = new Dictionary<string, float>();

            if (voiceTrack == null)
            {
                // Return default weights if no audio clip is assigned
                PopulateDefaultBlendshapes(blendshapes);
                return blendshapes;
            }

            // 1. Calculate target time in seconds and locate center audio sample
            float time = (float)currentFrame / videoFramerate;
            int frequency = voiceTrack.frequency;
            int centerSample = (int)(time * frequency);

            // 2. Extract a window of samples centered around the frame time
            float[] audioSamples = new float[SampleWindowSize];
            int startIndex = centerSample - (SampleWindowSize / 2);

            // Clamping boundaries to prevent array index out of range issues
            if (startIndex < 0)
            {
                startIndex = 0;
            }
            if (startIndex > voiceTrack.samples - SampleWindowSize)
            {
                startIndex = Math.Max(0, voiceTrack.samples - SampleWindowSize);
            }

            try
            {
                voiceTrack.GetData(audioSamples, startIndex);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AudioToBlendshapeEngine] Failed to read audio samples at index {startIndex}: {ex.Message}");
                PopulateDefaultBlendshapes(blendshapes);
                return blendshapes;
            }

            TensorFloat inputTensor = null;
            TensorFloat outputTensor = null;

            try
            {
                // 3. Create input Tensor representing the audio signal (1 x SampleWindowSize)
                inputTensor = new TensorFloat(new TensorShape(1, SampleWindowSize), audioSamples);

                // 4. Run inference
                worker.Execute(inputTensor);
                worker.FinishExecution();

                // 5. Retrieve output weights
                outputTensor = worker.PeekOutput() as TensorFloat;
                float[] rawWeights = outputTensor.DownloadToArray();

                // 6. Decode output array to ARKit blendshape name dictionary
                for (int i = 0; i < BlendshapeNames.Length; i++)
                {
                    float weight = 0.0f;
                    if (rawWeights != null && i < rawWeights.Length)
                    {
                        // Clamp the neural prediction between 0.0 (inactive) and 1.0 (fully active)
                        weight = Mathf.Clamp01(rawWeights[i]);
                    }
                    blendshapes[BlendshapeNames[i]] = weight;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AudioToBlendshapeEngine] Error during acoustic inference: {ex.Message}");
                PopulateDefaultBlendshapes(blendshapes);
            }
            finally
            {
                // CRITICAL Memory Cleanup to prevent VRAM accumulation
                if (inputTensor != null) inputTensor.Dispose();
                if (outputTensor != null) outputTensor.Dispose();
            }

            return blendshapes;
        }

        private void PopulateDefaultBlendshapes(Dictionary<string, float> blendshapes)
        {
            foreach (var shape in BlendshapeNames)
            {
                blendshapes[shape] = 0.0f;
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
