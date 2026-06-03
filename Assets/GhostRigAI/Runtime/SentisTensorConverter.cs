/*
 * COPYRIGHT & LICENSE NOTICE
 * This software is provided for portfolio evaluation and educational purposes only.
 * Unauthorized copying, modification, distribution, or commercial use of this code,
 * via any medium, is strictly prohibited. All rights reserved.
 */

using UnityEngine;
using Unity.InferenceEngine;
using System;

namespace GhostRigAI
{
    /// <summary>
    /// Utility class responsible for preprocessing textures and converting them to Unity Sentis Tensors.
    /// Implements GPU-accelerated center cropping and resizing.
    /// </summary>
    public static class SentisTensorConverter
    {
        private const int TargetSize = 256;
        private const int TargetChannels = 3; // RGB

        /// <summary>
        /// Crops a source texture to a 1:1 ratio, resizes it to 256x256, and converts it to a 1x3x256x256 Sentis Tensor.
        /// </summary>
        /// <param name="sourceTexture">The raw source RenderTexture or Texture.</param>
        /// <returns>A Tensor<float> containing the normalized image data in NCHW format (1x3x256x256).</returns>
        public static Tensor<float> ConvertToTensor(Texture sourceTexture)
        {
            if (sourceTexture == null)
            {
                throw new ArgumentNullException(nameof(sourceTexture), "Source texture cannot be null.");
            }

            // Calculate scale and offset for a 1:1 Center Crop
            float srcWidth = sourceTexture.width;
            float srcHeight = sourceTexture.height;
            Vector2 scale = Vector2.one;
            Vector2 offset = Vector2.zero;

            if (srcWidth > srcHeight)
            {
                // Landscape: Fit height, crop sides
                float ratio = srcHeight / srcWidth;
                scale.x = ratio;
                offset.x = 0.5f * (1f - ratio);
            }
            else if (srcHeight > srcWidth)
            {
                // Portrait: Fit width, crop top/bottom
                float ratio = srcWidth / srcHeight;
                scale.y = ratio;
                offset.y = 0.5f * (1f - ratio);
            }

            // Allocate a temporary RenderTexture for cropping and resizing
            RenderTexture croppedRT = RenderTexture.GetTemporary(TargetSize, TargetSize, 0, RenderTextureFormat.ARGB32);
            croppedRT.name = "GhostRig_CroppedResized_RT";
            croppedRT.filterMode = FilterMode.Bilinear;
            croppedRT.wrapMode = TextureWrapMode.Clamp;

            // Perform GPU-accelerated cropping and resizing via Graphics.Blit
            // Graphics.Blit with scale/offset maps: uv_source = uv_dest * scale + offset
            Graphics.Blit(sourceTexture, croppedRT, scale, offset);

            Tensor<float> tensor = null;
            try
            {
                // Convert the RenderTexture to a Sentis Tensor<float>
                // Layout is batch (1) x channels (3) x height (256) x width (256)
                // Values are normalized to [0.0, 1.0] by default.
                tensor = new Tensor<float>(new TensorShape(1, TargetChannels, TargetSize, TargetSize));
                TextureConverter.ToTensor(croppedRT, tensor);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GhostRig AI] Failed to convert texture to Sentis Tensor: {ex.Message}");
                if (tensor != null)
                {
                    tensor.Dispose();
                    tensor = null;
                }
                throw;
            }
            finally
            {
                // Release the temporary RenderTexture back to the pool to prevent VRAM memory leaks
                RenderTexture.ReleaseTemporary(croppedRT);
            }

            return tensor;
        }
    }
}
