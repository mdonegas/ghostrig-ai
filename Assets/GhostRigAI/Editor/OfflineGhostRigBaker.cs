/*
 * COPYRIGHT & LICENSE NOTICE
 * This software is provided for portfolio evaluation and educational purposes only.
 * Unauthorized copying, modification, distribution, or commercial use of this code,
 * via any medium, is strictly prohibited. All rights reserved.
 */

using UnityEngine;
using UnityEditor;
using UnityEngine.Video;
using Unity.InferenceEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GhostRigAI;

namespace GhostRigAI.Editor
{
    /// <summary>
    /// Editor Window interface and main orchestrator loop for GhostRig AI.
    /// Coordinates Phase 1 (Vision Ingestion), Phase 2 (Neural Engine), Phase 3 (Kinematic Translation & Smoothing),
    /// Phase 4 (Dexterity/Hand Crop), Phase 5 (Facial Audio/Audio2Face), Phase 6 (Offline Physics Stepping & Tether IK),
    /// and Phase 7 (Animation Curve Baker & Asset Exporter) in a unified offline processing loop.
    /// </summary>
    public class OfflineGhostRigBaker : EditorWindow
    {
        // System Inputs (Multimodal Pipelines)
        private VideoClip sourceVideo;
        private AudioClip voiceTrack;
        private ModelAsset onnxModel;
        private ModelAsset handModel;
        private ModelAsset faceModel;
        private int targetFramerate = 30;

        // Physical Avatar & Constraints (Phase 6 & 7)
        private Animator avatar;
        private Vector3 anchorPoint = Vector3.zero;
        private float maxTetherLength = 5.0f;

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
            window.minSize = new Vector2(450, 640);
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
            GUILayout.Label("1. MULTIMODAL CONFIGURATION", sectionTitleStyle);

            EditorGUI.BeginDisabledGroup(isBaking);
            
            GUILayout.BeginVertical(EditorStyles.helpBox);
            
            sourceVideo = (VideoClip)EditorGUILayout.ObjectField("Source Video Clip", sourceVideo, typeof(VideoClip), false);
            voiceTrack = (AudioClip)EditorGUILayout.ObjectField("Voice Track (Audio)", voiceTrack, typeof(AudioClip), false);
            
            GUILayout.Space(5);
            onnxModel = (ModelAsset)EditorGUILayout.ObjectField("Body Pose Model", onnxModel, typeof(ModelAsset), false);
            handModel = (ModelAsset)EditorGUILayout.ObjectField("Hand Pose Model", handModel, typeof(ModelAsset), false);
            faceModel = (ModelAsset)EditorGUILayout.ObjectField("Acoustic Face Model", faceModel, typeof(ModelAsset), false);
            
            GUILayout.Space(5);
            targetFramerate = EditorGUILayout.IntPopup("Target Framerate", targetFramerate, 
                new string[] { "30 FPS", "60 FPS" }, 
                new int[] { 30, 60 });

            GUILayout.EndVertical();

            GUILayout.Label("2. PHYSICAL AVATAR & CONSTRAINTS", sectionTitleStyle);
            GUILayout.BeginVertical(EditorStyles.helpBox);
            
            avatar = (Animator)EditorGUILayout.ObjectField("Physical Avatar Rig", avatar, typeof(Animator), true);
            anchorPoint = EditorGUILayout.Vector3Field("Tether Anchor Point", anchorPoint);
            maxTetherLength = EditorGUILayout.FloatField("Max Tether Length", maxTetherLength);

            GUILayout.EndVertical();
            
            EditorGUI.EndDisabledGroup();
        }

        private void DrawVideoStats()
        {
            if (sourceVideo == null) return;

            GUILayout.Label("3. VIDEO METADATA", sectionTitleStyle);
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

            GUILayout.Label("4. ORCHESTRATION PROGRESS", sectionTitleStyle);
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

            if (sourceVideo == null || onnxModel == null || handModel == null || faceModel == null || avatar == null)
            {
                EditorGUILayout.HelpBox("Please assign all required assets (Video, Audio, Body/Hand/Face models, and the physical Avatar) to begin baking.", MessageType.Info);
                return;
            }

            if (!isBaking)
            {
                Color oldColor = GUI.backgroundColor;
                GUI.backgroundColor = primaryColor;
                
                if (GUILayout.Button("▶ START MULTIMODAL PIPELINE BAKE", buttonStyle))
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
            
            // Engines representing all phases
            SentisOfflineEngine bodyEngine = null;
            HandMicroCropper handEngine = null;
            AudioToBlendshapeEngine faceEngine = null;
            OfflinePhysicsSimulator physicsSimulator = new OfflinePhysicsSimulator();
            GhostRigTelemetry telemetry = null;

            // Static Step Time for Temporal Smoothing & PhysX Stepping
            float stepTime = 1.0f / targetFramerate;
            OfflineKinematicSmoother poseSmoother = new OfflineKinematicSmoother();
            List<MasterFrameState> poseHistory = new List<MasterFrameState>();

            try
            {
                statusText = "Setting up Neural Engines...";
                bodyEngine = new SentisOfflineEngine(onnxModel);
                handEngine = new HandMicroCropper(handModel);
                faceEngine = new AudioToBlendshapeEngine(faceModel);

                statusText = "Setting up Physics Simulator...";
                physicsSimulator.StartManualSimulation();

                statusText = "Setting up Telemetry Visualizer...";
                telemetry = avatar.gameObject.GetComponent<GhostRigTelemetry>();
                if (telemetry == null)
                {
                    telemetry = avatar.gameObject.AddComponent<GhostRigTelemetry>();
                }
                telemetry.anchorPoint = anchorPoint;
                telemetry.maxTetherLength = maxTetherLength;
                telemetry.hipsTransform = avatar.GetBoneTransform(HumanBodyBones.Hips);

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

                    statusText = $"Processing Body Pose on frame {frame}...";

                    // Preprocess and Convert to Tensor (Phase 1 Output)
                    Tensor<float> inputTensor = SentisTensorConverter.ConvertToTensor(renderTexture);

                    // Body Neural Engine Inference (Phase 2 Output)
                    Tensor<float> rawPredictions = bodyEngine.ProcessTensor(inputTensor);

                    // Extract the raw prediction coordinates back to CPU
                    float[] predictionValues = rawPredictions.DownloadToArray();

                    // Phase 3: Kinematic Translator (Raw body joint rotation Quaternions)
                    Dictionary<HumanBodyBones, Quaternion> rawBodyPose = PoseDecoder.DecodePose(predictionValues);

                    // Phase 3: Kinematic Smoother (One-Euro & Hermite Filtered rotations)
                    Dictionary<HumanBodyBones, Quaternion> smoothedBodyPose = poseSmoother.SmoothPose(rawBodyPose, stepTime);

                    statusText = $"Cropping & Processing Hands on frame {frame}...";

                    // Phase 4: Hand Micro-Cropper (Isolates left/right wrist coordinates from Phase 3 coordinates)
                    Vector2 leftWristCoords = new Vector2(
                        Mathf.Clamp01(predictionValues[7 * 3 + 0]),
                        Mathf.Clamp01(predictionValues[7 * 3 + 1])
                    );
                    Vector2 rightWristCoords = new Vector2(
                        Mathf.Clamp01(predictionValues[10 * 3 + 0]),
                        Mathf.Clamp01(predictionValues[10 * 3 + 1])
                    );

                    // Process both hands through the secondary hand pose worker
                    Dictionary<HumanBodyBones, Quaternion> leftHandPose = handEngine.ProcessHand(renderTexture, leftWristCoords, true);
                    Dictionary<HumanBodyBones, Quaternion> rightHandPose = handEngine.ProcessHand(renderTexture, rightWristCoords, false);

                    statusText = $"Processing Facial Audio on frame {frame}...";

                    // Phase 5: Facial Audio (Audio2Face)
                    Dictionary<string, float> facePose = faceEngine.ProcessAudio(voiceTrack, (int)frame, targetFramerate);

                    // 6. Compile the complete "Master Frame State"
                    MasterFrameState masterState = new MasterFrameState
                    {
                        FrameIndex = (int)frame,
                        TimeStamp = frame * stepTime,
                        HipsPosition = new Vector3(predictionValues[0], predictionValues[1], predictionValues[2])
                    };

                    // Merge Body
                    foreach (var kvp in smoothedBodyPose)
                    {
                        masterState.BoneRotations[kvp.Key] = kvp.Value;
                    }
                    // Merge Left Hand
                    foreach (var kvp in leftHandPose)
                    {
                        masterState.BoneRotations[kvp.Key] = kvp.Value;
                    }
                    // Merge Right Hand
                    foreach (var kvp in rightHandPose)
                    {
                        masterState.BoneRotations[kvp.Key] = kvp.Value;
                    }
                    // Merge Face Blendshapes
                    foreach (var kvp in facePose)
                    {
                        masterState.BlendshapeWeights[kvp.Key] = kvp.Value;
                    }

                    statusText = $"Applying physics constraints on frame {frame}...";

                    // Phase 6: Apply pose to physical avatar using Animator.SetBoneLocalRotation
                    foreach (var kvp in masterState.BoneRotations)
                    {
                        avatar.SetBoneLocalRotation(kvp.Key, kvp.Value);
                    }

                    // Position Hips relative to raw predicted hips root
                    Transform hipsTransform = avatar.GetBoneTransform(HumanBodyBones.Hips);
                    if (hipsTransform != null)
                    {
                        hipsTransform.position = masterState.HipsPosition;
                    }

                    // Execute Physics step manually (Advancing PhysX scene)
                    physicsSimulator.SimulateStep(stepTime);

                    // Solve Tether Constraint (Silver Cord) and apply IK corrections back to pose
                    TetherConstraintIK.SolveTether(ref masterState.HipsPosition, masterState.BoneRotations, anchorPoint, maxTetherLength);

                    // Update avatar hips to reflect resolved Tether position
                    if (hipsTransform != null)
                    {
                        hipsTransform.position = masterState.HipsPosition;
                        hipsTransform.localRotation = masterState.BoneRotations[HumanBodyBones.Hips];
                    }

                    // Update visual telemetry in Scene View
                    Transform leftWristTransform = avatar.GetBoneTransform(HumanBodyBones.LeftHand);
                    Transform rightWristTransform = avatar.GetBoneTransform(HumanBodyBones.RightHand);
                    telemetry.leftWristWorldPos = leftWristTransform != null ? leftWristTransform.position : Vector3.zero;
                    telemetry.rightWristWorldPos = rightWristTransform != null ? rightWristTransform.position : Vector3.zero;

                    // Append to baking history (Phase 7 Input)
                    poseHistory.Add(masterState);

                    // Print progress validation log to Console
                    if (frame % 30 == 0 || frame == totalFrames - 1)
                    {
                        Quaternion spine = masterState.BoneRotations.ContainsKey(HumanBodyBones.Spine) ? masterState.BoneRotations[HumanBodyBones.Spine] : Quaternion.identity;
                        float jawOpen = masterState.BlendshapeWeights.ContainsKey("jawOpen") ? masterState.BlendshapeWeights["jawOpen"] : 0.0f;

                        Debug.Log($"[GhostRig AI] Frame {frame} Compiled State:\n" +
                                  $"Resolved Hips Pos: {masterState.HipsPosition} | " +
                                  $"Spine Euler: {spine.eulerAngles} | " +
                                  $"Jaw Open Weight: {jawOpen:F2}");
                    }

                    // CRITICAL: Memory Management (Disposing of unmanaged body tensors immediately)
                    rawPredictions.Dispose();
                    inputTensor.Dispose();

                    // Await next frame logic
                    await Task.Yield();
                }

                if (isBaking)
                {
                    statusText = "Baking Animation Curves & Exporting Clip...";
                    
                    // Phase 7: Export the native animation clip asset
                    AnimationClipBuilder.ExportAnimationClip(poseHistory, avatar, targetFramerate);

                    progress = 1.0f;
                    currentFrameIndex = totalFrames;
                    statusText = "Completed successfully!";
                    Debug.Log($"[GhostRig AI] Offline Exporter completed. Processed: {totalFrames} frames in {stopwatch.Elapsed:mm\\:ss}.");
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
                
                // Restore auto-simulation state (Restores Physics.autoSimulation = true)
                physicsSimulator.StopManualSimulation();

                // Remove telemetry visualizer script from avatar
                if (telemetry != null)
                {
                    if (Application.isPlaying) Destroy(telemetry);
                    else DestroyImmediate(telemetry);
                }

                // Dispose of VideoPlayer resources
                VideoFrameExtractor.Cleanup(player, renderTexture);
                
                // Dispose of all Neural Engine resources (all 3 models)
                bodyEngine?.Dispose();
                handEngine?.Dispose();
                faceEngine?.Dispose();
                
                stopwatch.Stop();
                isBaking = false;
            }
        }
    }
}
