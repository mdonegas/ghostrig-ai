/*
 * COPYRIGHT & LICENSE NOTICE
 * This software is provided for portfolio evaluation and educational purposes only.
 * Unauthorized copying, modification, distribution, or commercial use of this code,
 * via any medium, is strictly prohibited. All rights reserved.
 */

using Unity.InferenceEngine;

namespace GhostRigAI
{
    /// <summary>
    /// Extension methods for Unity Inference Engine classes to support cross-version compatibility.
    /// </summary>
    public static class SentisExtensions
    {
        /// <summary>
        /// Extension method to ensure compatibility with worker synchronization requirements.
        /// In Inference Engine 2.x, readback calls like DownloadToArray/ToReadOnlyArray automatically block
        /// the CPU until GPU processing is complete. This method serves as a semantic synchronizer.
        /// </summary>
        public static void FinishExecution(this Worker worker)
        {
            // No direct blocking call exists on Worker since operations are lazy-synchronized
            // on data readback. This method is provided to satisfy the structural specification.
        }
    }
}
