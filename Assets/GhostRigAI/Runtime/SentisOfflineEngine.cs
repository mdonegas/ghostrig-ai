/*
 * COPYRIGHT & LICENSE NOTICE
 * This software is provided for portfolio evaluation and educational purposes only.
 * Unauthorized copying, modification, distribution, or commercial use of this code,
 * via any medium, is strictly prohibited. All rights reserved.
 */

using UnityEngine;
using Unity.Sentis;
using System;

namespace GhostRigAI
{
    /// <summary>
    /// Managed Neural Engine runner for GhostRig AI Phase 2.
    /// Handles the ONNX model runtime lifecycle and schedules inference execution.
    /// </summary>
    public class SentisOfflineEngine : IDisposable
    {
        private Model runtimeModel;
        private IWorker worker;

        /// <summary>
        /// Initializes the Neural Engine by loading the ONNX model and spawning a GPU Compute worker.
        /// </summary>
        /// <param name="onnxModel">The imported ONNX model asset.</param>
        public SentisOfflineEngine(ModelAsset onnxModel)
        {
            if (onnxModel == null)
            {
                throw new ArgumentNullException(nameof(onnxModel), "ONNX model asset cannot be null.");
            }

            try
            {
                // Compile the model asset into a runtime model representation
                runtimeModel = ModelLoader.Load(onnxModel);

                // Create the worker inference engine using GPUCompute backend
                worker = WorkerFactory.CreateWorker(BackendType.GPUCompute, runtimeModel);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GhostRig AI] Failed to initialize Sentis Offline Engine: {ex.Message}");
                Cleanup();
                throw;
            }
        }

        /// <summary>
        /// Executes inference on the input tensor and returns the raw predictions tensor.
        /// </summary>
        /// <param name="inputTensor">The preprocessed input TensorFloat (1 x 3 x 256 x 256).</param>
        /// <returns>The raw prediction TensorFloat representing joint poses/rotations.</returns>
        public TensorFloat ProcessTensor(TensorFloat inputTensor)
        {
            if (worker == null)
            {
                throw new InvalidOperationException("Neural Engine has not been initialized or has already been disposed.");
            }

            if (inputTensor == null)
            {
                throw new ArgumentNullException(nameof(inputTensor), "Input tensor cannot be null.");
            }

            // Execute inference using the input tensor
            worker.Execute(inputTensor);

            // Block the CPU thread to ensure GPU execution is complete before proceeding
            worker.FinishExecution();

            // Capture output tensor (non-allocating peek)
            TensorFloat rawPredictions = worker.PeekOutput() as TensorFloat;
            
            return rawPredictions;
        }

        /// <summary>
        /// Internal helper to release unmanaged memory.
        /// </summary>
        private void Cleanup()
        {
            if (worker != null)
            {
                worker.Dispose();
                worker = null;
            }
            runtimeModel = null;
        }

        /// <summary>
        /// Disposes of the worker and internal runtime model representation to prevent VRAM memory leaks.
        /// </summary>
        public void Dispose()
        {
            Cleanup();
            GC.SuppressFinalize(this);
        }

        ~SentisOfflineEngine()
        {
            Cleanup();
        }
    }
}
