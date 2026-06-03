/*
 * COPYRIGHT & LICENSE NOTICE
 * This software is provided for portfolio evaluation and educational purposes only.
 * Unauthorized copying, modification, distribution, or commercial use of this code,
 * via any medium, is strictly prohibited. All rights reserved.
 */

using Unity.Sentis;

namespace GhostRigAI
{
    /// <summary>
    /// Extension methods for Unity Sentis classes to support cross-version compatibility (Sentis 1.x vs 2.x).
    /// </summary>
    public static class SentisExtensions
    {
        /// <summary>
        /// Compatibility wrapper that downloads tensor data to the CPU and returns a flat float array.
        /// Maps DownloadToArray (Sentis 2.x standard) to ToReadOnlyArray (Sentis 1.x standard).
        /// </summary>
        public static float[] DownloadToArray(this TensorFloat tensor)
        {
            if (tensor == null)
            {
                return null;
            }

            // Sync GPU-to-CPU data
            tensor.MakeReadable();

            // Return the read-only CPU array copy
            return tensor.ToReadOnlyArray();
        }

        /// <summary>
        /// Extension method to ensure compatibility with worker synchronization requirements.
        /// In Sentis, readback calls like DownloadToArray/ToReadOnlyArray automatically block
        /// the CPU until GPU processing is complete. This method serves as a semantic synchronizer.
        /// </summary>
        public static void FinishExecution(this IWorker worker)
        {
            // No direct blocking call exists on IWorker since operations are lazy-synchronized
            // on data readback. This method is provided to satisfy the structural specification.
        }
    }
}
