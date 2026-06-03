/*
 * COPYRIGHT & LICENSE NOTICE
 * This software is provided for portfolio evaluation and educational purposes only.
 * Unauthorized copying, modification, distribution, or commercial use of this code,
 * via any medium, is strictly prohibited. All rights reserved.
 */

using UnityEngine;
using UnityEditor;
using UnityEngine.Video;
using Unity.Sentis;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GhostRigAI;

namespace GhostRigAI.Editor
{
    /// <summary>
    /// Editor Window interface and main orchestrator loop for GhostRig AI.
    /// Manages Phase 1 (Vision Ingestion), Phase 2 (Neural Engine Inference), and Phase 3 (Kinematic Translation and Smoothing).
    /// </summary>
    public class OfflineGhostRigBaker : EditorWindow
    {
        // System Inputs (Phase 1 & 2)
        private VideoClip sourceVideo;
        private ModelAsset onnxModel;
        private int targetFramerate = 30;

        // Ingestion & Inference State
        private bool isBaking = false;
        private long currentFrameIndex = 0;
        private long totalFrames = 0;
        private string statusText = "Idle";
        private float progress = 0f;
        private System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();

        // Styles & Colors
        private GUIStyle headerStyle;
        private GUIStyle sectionTitleStyle;
        private GUIStyle buttonStyle;
        private GUIStyle infoBoxStyle;
        
        private readonly Color primaryColor = new Color(0.12f, 0.58f, 0.95f); // Neon Blue
        private readonly Color accentColor = new Color(0.6f, 0.2f, 0.8f);    // Purple
        private readonly Color darkBgColor = new Color(0.1f, 0.1f, 0.13f);    // Slate Dark
        private readonly Color lightPanelColor = new Color(0.16f, 0.16f, 0.2f); // Medium Grey

        [MenuItem("Window/GhostRig AI/Offline Baker")]
        public static void ShowWindow()
        {
            OfflineGhostRigBaker window = GetWindow<OfflineGhostRigBaker>("GhostRig AI - Offline Baker");
            window.minSize = new Vector2(450, 520);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
        }

        private void InitializeStyles()
        {
            if (headerStyle != null) return;

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                margin = new RectOffset(0, 0, 10, 5),
                normal = { textColor = primaryColor }
            };

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                fixedHeight = 35,
                margin = new RectOffset(0, 0, 10, 10)
            };

            infoBoxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 11,
                padding = new RectOffset(10, 10, 10, 10)
            };
        }

        private void OnGUI()
        {
            InitializeStyles();

            // Draw Dark Background
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), darkBgColor);

            // Title Header
            DrawHeader();

            GUILayout.BeginArea(new Rect(15, 75, position.width - 30, position.height - 90));
            
            // Configuration Section
            DrawConfiguration();

            // Video Stats Section
            DrawVideoStats();

            // Progress Section
            DrawProgressPanel();

            // Control Buttons Section
            DrawControls();

            GUILayout.EndArea();
        }

        private void DrawHeader()
        {
            Rect headerRect = new Rect(0, 0, position.width, 65);
            EditorGUI.DrawRect(headerRect, lightPanelColor);

            // Neon horizontal accent line
            EditorGUI.DrawRect(new Rect(0, 63, position.width, 2), primaryColor);

            GUILayout.Space(15);
            GUILayout.Label("G H O S T R I G   A I", headerStyle);
            GUILayout.Label("Offline Video-to-MoCap Orchestrator", EditorStyles.centeredGreyMiniLabel);
        }

        private void DrawConfiguration()
        {
            GUILayout.Label("1. CONFIGURATION", sectionTitleStyle);

            EditorGUI.BeginDisabledGroup(isBaking);
            
            GUILayout.BeginVertical(EditorStyles.helpBox);
            
            sourceVideo = (VideoClip)EditorGUILayout.ObjectField("Source Video Clip", sourceVideo, typeof(VideoClip), false);
            onnxModel = (ModelAsset)EditorGUILayout.ObjectField("Neural Network Model", onnxModel, typeof(ModelAsset), false);
            
            targetFramerate = EditorGUILayout.IntPopup("Target Framerate", targetFramerate, 
                new string[] { "30 FPS", "60 FPS" }, 
                new int[] { 30, 60 });

            GUILayout.EndVertical();
            
            EditorGUI.EndDisabledGroup();
        }

        private void DrawVideoStats()
        {
            if (sourceVideo == null) return;

            GUILayout.Label("2. VIDEO METADATA", sectionTitleStyle);
            GUILayout.BeginVertical(EditorStyles.helpBox);

            double duration = sourceVideo.length;
            long frames = (long)sourceVideo.frameCount;
            double originalFps = sourceVideo.frameRate;

            GUILayout.Label($"<b>Dimensions:</b> {sourceVideo.width} x {sourceVideo.height} px", infoBoxStyle);
            GUILayout.Label($"<b>Duration:</b> {duration:F2} seconds", infoBoxStyle);
            GUILayout.Label($"<b>Total Frames:</b> {frames}", infoBoxStyle);
            GUILayout.Label($"<b>Original Framerate:</b> {originalFps:F2} FPS", infoBoxStyle);

            GUILayout.EndVertical();
        }

        private void DrawProgressPanel()
        {
            if (!isBaking) return;

            GUILayout.Label("3. ORCHESTRATION PROGRESS", sectionTitleStyle);
            GUILayout.BeginVertical(EditorStyles.helpBox);

            float pct = progress * 100f;
            GUILayout.Label($"Processing: {pct:F1}% ({currentFrameIndex}/{totalFrames} Frames)");

            // Custom styled progress bar (Gradient Effect)
            Rect rect = GUILayoutUtility.GetRect(18, 18, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f)); // Background
            float progressWidth = rect.width * progress;
            if (progressWidth > 0)
            {
                Rect progressRect = new Rect(rect.x, rect.y, progressWidth, rect.height);
                EditorGUI.DrawRect(progressRect, primaryColor); // Filled
            }

            // Stats info
            TimeSpan elapsed = stopwatch.Elapsed;
            double avgTimePerFrame = currentFrameIndex > 0 ? elapsed.TotalMilliseconds / currentFrameIndex : 0;
            double estTotalMs = avgTimePerFrame * totalFrames;
            double remainingMs = Math.Max(0, estTotalMs - elapsed.TotalMilliseconds);
            TimeSpan remaining = TimeSpan.FromMilliseconds(remainingMs);

            GUILayout.Space(5);
            GUILayout.Label($"Status: {statusText}");
            GUILayout.Label($"Elapsed: {elapsed:mm\\:ss} | Remaining: {remaining:mm\\:ss} | Speed: {avgTimePerFrame:F1}ms/frame");

            GUILayout.EndVertical();
        }

        private void DrawControls()
        {
            GUILayout.FlexibleSpace();

            if (sourceVideo == null || onnxModel == null)
            {
                EditorGUILayout.HelpBox("Please assign a source Video Clip and a Neural Network Model to begin baking.", MessageType.Info);
                return;
            }

            if (!isBaking)
            {
                Color oldColor = GUI.backgroundColor;
                GUI.backgroundColor = primaryColor;
                
                if (GUILayout.Button("▶ START PIPELINE BAKE", buttonStyle))
                {
                    StartBakingProcess();
                }

                GUI.backgroundColor = oldColor;
            }
            else
            {
                Color oldColor = GUI.backgroundColor;
                GUI.backgroundColor = Color.red;

                if (GUILayout.Button("⏹ CANCEL BAKE", buttonStyle))
                {
                    isBaking = false;
                }

                GUI.backgroundColor = oldColor;
            }
        }

        private async void StartBakingProcess()
        {
            isBaking = true;
            progress = 0f;
            currentFrameIndex = 0;
            totalFrames = 0;
            statusText = "Initializing...";
            stopwatch.Reset();
            stopwatch.Start();

            VideoPlayer player = null;
            RenderTexture renderTexture = null;
            SentisOfflineEngine neuralEngine = null;

            // Static Step Time for Offline Temporal Smoothing (Phase 3)
            float stepTime = 1.0f / targetFramerate;
            OfflineKinematicSmoother poseSmoother = new OfflineKinematicSmoother();

            try
            {
                statusText = "Setting up Neural Engine...";
                neuralEngine = new SentisOfflineEngine(onnxModel);

                statusText = "Setting up Video Extractor...";
                player = VideoFrameExtractor.SetupExtractor(sourceVideo, out renderTexture);

                statusText = "Preparing decoder and buffering video...";
                await VideoFrameExtractor.PrepareVideoAsync(player);

                totalFrames = (long)player.frameCount;
                statusText = $"Starting Execution Loop ({totalFrames} frames)...";

                // Initialize Kinematic Smoother history
                poseSmoother.ResetHistory();

                // Master Loop
                for (long frame = 0; frame < totalFrames; frame++)
                {
                    if (!isBaking)
                    {
                        statusText = "Cancelled by user.";
                        break;
                    }

                    currentFrameIndex = frame;
                    progress = (float)frame / totalFrames;
                    statusText = $"Extracting frame {frame}...";

                    if (!Application.isPlaying)
                    {
                        EditorApplication.QueuePlayerLoopUpdate();
                    }

                    // Await the sendFrameReadyEvents (Phase 1)
                    bool frameReady = await VideoFrameExtractor.SeekToFrameAsync(player, frame);
                    if (!frameReady)
                    {
                        Debug.LogWarning($"[GhostRig AI] Skipping frame {frame} due to extraction timeout.");
                        continue;
                    }

                    statusText = $"Preprocessing frame {frame}...";

                    // Preprocess and Convert to Tensor (Phase 1 Output)
                    TensorFloat inputTensor = SentisTensorConverter.ConvertToTensor(renderTexture);

                    statusText = $"Running Neural Inference on frame {frame}...";

                    // Neural Engine Inference (Phase 2 Output)
                    TensorFloat rawPredictions = neuralEngine.ProcessTensor(inputTensor);

                    // Extract the raw predictions data
                    float[] predictionValues = rawPredictions.DownloadToArray();

                    statusText = $"Translating Kinematics & Smoothing frame {frame}...";

                    // Phase 3: Kinematic Translator (Raw joint rotation Quaternions)
                    Dictionary<HumanBodyBones, Quaternion> rawPose = PoseDecoder.DecodePose(predictionValues);

                    // Phase 3: Kinematic Smoother (One-Euro & Hermite Filtered rotations)
                    Dictionary<HumanBodyBones, Quaternion> smoothedPose = poseSmoother.SmoothPose(rawPose, stepTime);

                    // Print progress validation log to Console (including sample joint angles to check translation)
                    if (frame % 30 == 0 || frame == totalFrames - 1)
                    {
                        string inputShapeStr = string.Join("x", inputTensor.shape);
                        string outputShapeStr = string.Join("x", rawPredictions.shape);
                        
                        Quaternion rawSpine = rawPose.ContainsKey(HumanBodyBones.Spine) ? rawPose[HumanBodyBones.Spine] : Quaternion.identity;
                        Quaternion smoothSpine = smoothedPose.ContainsKey(HumanBodyBones.Spine) ? smoothedPose[HumanBodyBones.Spine] : Quaternion.identity;

                        Debug.Log($"[GhostRig AI] Frame {frame} successfully baked.\n" +
                                  $"Input Shape: {inputShapeStr} | Output Shape: {outputShapeStr}\n" +
                                  $"Raw Spine Euler: {rawSpine.eulerAngles} | Smooth Spine Euler: {smoothSpine.eulerAngles}");
                    }

                    // CRITICAL: Memory Management (Disposing of unmanaged resources immediately)
                    rawPredictions.Dispose();
                    inputTensor.Dispose();

                    // Await next frame logic
                    await Task.Yield();
                }

                if (isBaking)
                {
                    progress = 1.0f;
                    currentFrameIndex = totalFrames;
                    statusText = "Completed successfully!";
                    Debug.Log($"[GhostRig AI] Offline Orchestration completed. Total processed: {totalFrames} frames in {stopwatch.Elapsed:mm\\:ss}.");
                }
            }
            catch (Exception ex)
            {
                statusText = "Error encountered.";
                Debug.LogError($"[GhostRig AI] Exception during Offline Ingestion: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                statusText = "Cleaning up resources...";
                
                // Dispose of VideoPlayer resources
                VideoFrameExtractor.Cleanup(player, renderTexture);
                
                // Dispose of Neural Engine resources
                neuralEngine?.Dispose();
                
                stopwatch.Stop();
                isBaking = false;
            }
        }
    }
}
