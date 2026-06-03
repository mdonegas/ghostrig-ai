/*
 * COPYRIGHT & LICENSE NOTICE
 * This software is provided for portfolio evaluation and educational purposes only.
 * Unauthorized copying, modification, distribution, or commercial use of this code,
 * via any medium, is strictly prohibited. All rights reserved.
 */

using UnityEngine;
using UnityEngine.Video;
using System;
using System.Threading.Tasks;

namespace GhostRigAI
{
    /// <summary>
    /// Utility class responsible for extracting specific frames from a VideoClip in a deterministic manner.
    /// Highly optimized for offline processing in both Editor and Runtime environments.
    /// </summary>
    public static class VideoFrameExtractor
    {
        /// <summary>
        /// Instantiates and configures a temporary VideoPlayer component pointing to a target VideoClip.
        /// </summary>
        /// <param name="clip">The source video clip.</param>
        /// <param name="renderTexture">Output RenderTexture containing the raw frame texture.</param>
        /// <returns>A configured VideoPlayer instance.</returns>
        public static VideoPlayer SetupExtractor(VideoClip clip, out RenderTexture renderTexture)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip), "Source video clip cannot be null.");
            }

            // Create a temporary hidden GameObject to host the VideoPlayer
            GameObject playerHolder = new GameObject("GhostRig_VideoPlayer_Temp");
            playerHolder.hideFlags = HideFlags.HideAndDontSave;

            VideoPlayer videoPlayer = playerHolder.AddComponent<VideoPlayer>();
            videoPlayer.playOnAwake = false;
            videoPlayer.source = VideoSource.VideoClip;
            videoPlayer.clip = clip;
            
            // Use APIOnly render mode or RenderTexture mode to prevent outputting to camera
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            
            // Create target RenderTexture with matching video dimensions
            renderTexture = new RenderTexture((int)clip.width, (int)clip.height, 0, RenderTextureFormat.ARGB32);
            renderTexture.name = "GhostRig_VideoPlayer_RT";
            renderTexture.antiAliasing = 1;
            renderTexture.useMipMap = false;
            renderTexture.Create();

            videoPlayer.targetTexture = renderTexture;
            videoPlayer.sendFrameReadyEvents = true;

            return videoPlayer;
        }

        /// <summary>
        /// Asynchronously prepares the VideoPlayer for playback.
        /// </summary>
        /// <param name="videoPlayer">The active VideoPlayer component.</param>
        /// <param name="timeoutMs">Timeout in milliseconds before throwing an exception.</param>
        public static async Task PrepareVideoAsync(VideoPlayer videoPlayer, int timeoutMs = 15000)
        {
            if (videoPlayer == null)
            {
                throw new ArgumentNullException(nameof(videoPlayer));
            }

            if (videoPlayer.isPrepared)
            {
                return;
            }

            var tcs = new TaskCompletionSource<bool>();
            VideoPlayer.EventHandler preparedHandler = null;

            preparedHandler = (vp) =>
            {
                videoPlayer.prepareCompleted -= preparedHandler;
                tcs.TrySetResult(true);
            };

            videoPlayer.prepareCompleted += preparedHandler;
            videoPlayer.Prepare();

            var delayTask = Task.Delay(timeoutMs);
            var completedTask = await Task.WhenAny(tcs.Task, delayTask);

            if (completedTask == delayTask)
            {
                videoPlayer.prepareCompleted -= preparedHandler;
                throw new TimeoutException("[GhostRig AI] Video player preparation timed out.");
            }
        }

        /// <summary>
        /// Seeks to a specific frame index and awaits the sendFrameReadyEvents callback.
        /// </summary>
        /// <param name="videoPlayer">The active VideoPlayer component.</param>
        /// <param name="frameIndex">The index of the frame to load.</param>
        /// <param name="timeoutMs">Timeout in milliseconds for the seek operation.</param>
        /// <returns>True if the frame was successfully loaded; false if timed out.</returns>
        public static async Task<bool> SeekToFrameAsync(VideoPlayer videoPlayer, long frameIndex, int timeoutMs = 5000)
        {
            if (videoPlayer == null)
            {
                throw new ArgumentNullException(nameof(videoPlayer));
            }

            var tcs = new TaskCompletionSource<bool>();
            VideoPlayer.FrameReadyEventHandler frameReadyHandler = null;

            frameReadyHandler = (vp, index) =>
            {
                if (index == frameIndex)
                {
                    videoPlayer.frameReady -= frameReadyHandler;
                    tcs.TrySetResult(true);
                }
            };

            videoPlayer.frameReady += frameReadyHandler;
            
            // Force active events and seek
            videoPlayer.sendFrameReadyEvents = true;
            videoPlayer.frame = frameIndex;
            videoPlayer.Pause();

            var delayTask = Task.Delay(timeoutMs);
            var completedTask = await Task.WhenAny(tcs.Task, delayTask);

            if (completedTask == delayTask)
            {
                videoPlayer.frameReady -= frameReadyHandler;
                Debug.LogWarning($"[GhostRig AI] Seek to frame {frameIndex} timed out. Continuing with fallbacks.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Safely disposes and cleans up the VideoPlayer components and temporary RenderTextures.
        /// Prevents VRAM leaks.
        /// </summary>
        /// <param name="videoPlayer">The VideoPlayer to destroy.</param>
        /// <param name="renderTexture">The target RenderTexture to release.</param>
        public static void Cleanup(VideoPlayer videoPlayer, RenderTexture renderTexture)
        {
            if (videoPlayer != null)
            {
                GameObject holder = videoPlayer.gameObject;
                videoPlayer.targetTexture = null;
                
                if (Application.isPlaying)
                {
                    GameObject.Destroy(holder);
                }
                else
                {
                    GameObject.DestroyImmediate(holder);
                }
            }

            if (renderTexture != null)
            {
                renderTexture.Release();
                if (Application.isPlaying)
                {
                    GameObject.Destroy(renderTexture);
                }
                else
                {
                    GameObject.DestroyImmediate(renderTexture);
                }
            }
        }
    }
}
