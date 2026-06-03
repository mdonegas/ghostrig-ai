/*
 * COPYRIGHT & LICENSE NOTICE
 * This software is provided for portfolio evaluation and educational purposes only.
 * Unauthorized copying, modification, distribution, or commercial use of this code,
 * via any medium, is strictly prohibited. All rights reserved.
 */

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections.Generic;

namespace GhostRigAI
{
    /// <summary>
    /// Generates, optimizes, and exports native Unity AnimationClip assets from offline baking history.
    /// Wraps transform bindings, blendshapes, and keyframe reduction.
    /// </summary>
    public static class AnimationClipBuilder
    {
        private const string ExportFolder = "Assets/GhostRigBakes";
        private const string ExportPath = ExportFolder + "/Anim_Bake.anim";

        /// <summary>
        /// Compiles the Master Pose History into an optimized, native Unity AnimationClip.
        /// </summary>
        /// <param name="history">The accumulated frame history of bones and facial weights.</param>
        /// <param name="avatar">The physical animator avatar in the scene to resolve paths.</param>
        /// <param name="framerate">Target framerate of the bake.</param>
        public static void ExportAnimationClip(List<MasterFrameState> history, Animator avatar, float framerate)
        {
            if (history == null || history.Count == 0)
            {
                Debug.LogWarning("[AnimationClipBuilder] Pose history is empty. Export aborted.");
                return;
            }

            if (avatar == null)
            {
                throw new ArgumentNullException(nameof(avatar), "Avatar Animator cannot be null for resolving transform paths.");
            }

            // Create a new native animation clip
            AnimationClip clip = new AnimationClip();
            clip.frameRate = framerate;
            clip.name = "GhostRig_Bake";

            // Resolve target SkinnedMeshRenderer for facial blendshapes
            SkinnedMeshRenderer faceRenderer = avatar.GetComponentInChildren<SkinnedMeshRenderer>();
            string facePath = faceRenderer != null ? GetTransformPath(avatar.transform, faceRenderer.transform) : "";

            // Pre-allocate curve sets to consolidate keys
            // Key: Binding path & property -> Curve
            var rotationCurves = new Dictionary<string, AnimationCurve[]>(); // [0]=>x, [1]=>y, [2]=>z, [3]=>w
            var translationCurves = new Dictionary<string, AnimationCurve[]>(); // [0]=>x, [1]=>y, [2]=>z
            var blendshapeCurves = new Dictionary<string, AnimationCurve>();

            // 1. Accumulate all keyframes from history
            for (int f = 0; f < history.Count; f++)
            {
                MasterFrameState frameState = history[f];
                float time = (float)frameState.FrameIndex / framerate;

                // Process Bones (Rotations)
                foreach (var kvp in frameState.BoneRotations)
                {
                    HumanBodyBones bone = kvp.Key;
                    Quaternion rot = kvp.Value;

                    Transform boneTransform = avatar.GetBoneTransform(bone);
                    if (boneTransform == null) continue;

                    string bonePath = GetTransformPath(avatar.transform, boneTransform);

                    if (!rotationCurves.ContainsKey(bonePath))
                    {
                        rotationCurves[bonePath] = new AnimationCurve[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                    }

                    rotationCurves[bonePath][0].AddKey(time, rot.x);
                    rotationCurves[bonePath][1].AddKey(time, rot.y);
                    rotationCurves[bonePath][2].AddKey(time, rot.z);
                    rotationCurves[bonePath][3].AddKey(time, rot.w);
                }

                // Process Hips Translation
                Transform hipsTransform = avatar.GetBoneTransform(HumanBodyBones.Hips);
                if (hipsTransform != null)
                {
                    string hipsPath = GetTransformPath(avatar.transform, hipsTransform);
                    if (!translationCurves.ContainsKey(hipsPath))
                    {
                        translationCurves[hipsPath] = new AnimationCurve[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                    }

                    translationCurves[hipsPath][0].AddKey(time, frameState.HipsPosition.x);
                    translationCurves[hipsPath][1].AddKey(time, frameState.HipsPosition.y);
                    translationCurves[hipsPath][2].AddKey(time, frameState.HipsPosition.z);
                }

                // Process Face Blendshapes
                if (faceRenderer != null)
                {
                    foreach (var kvp in frameState.BlendshapeWeights)
                    {
                        string shapeName = kvp.Key;
                        float weight = kvp.Value * 100f; // Unity blendshapes range from 0 to 100

                        if (!blendshapeCurves.ContainsKey(shapeName))
                        {
                            blendshapeCurves[shapeName] = new AnimationCurve();
                        }

                        blendshapeCurves[shapeName].AddKey(time, weight);
                    }
                }
            }

            // 2. Apply Keyframe Reduction Compression and Set Curves on Clip
            // Process rotations
            foreach (var kvp in rotationCurves)
            {
                string path = kvp.Key;
                AnimationCurve[] curves = kvp.Value;
                string[] props = { "localRotation.x", "localRotation.y", "localRotation.z", "localRotation.w" };

                for (int i = 0; i < 4; i++)
                {
                    ReduceKeyframes(curves[i], 0.0005f); // Tolerance setting for rotation compression
                    var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), props[i]);
                    AnimationUtility.SetEditorCurve(clip, binding, curves[i]);
                }
            }

            // Process translations
            foreach (var kvp in translationCurves)
            {
                string path = kvp.Key;
                AnimationCurve[] curves = kvp.Value;
                string[] props = { "localPosition.x", "localPosition.y", "localPosition.z" };

                for (int i = 0; i < 3; i++)
                {
                    ReduceKeyframes(curves[i], 0.001f); // Tolerance setting for position compression
                    var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), props[i]);
                    AnimationUtility.SetEditorCurve(clip, binding, curves[i]);
                }
            }

            // Process blendshapes
            if (faceRenderer != null)
            {
                foreach (var kvp in blendshapeCurves)
                {
                    string shapeName = kvp.Key;
                    AnimationCurve curve = kvp.Value;

                    ReduceKeyframes(curve, 0.05f); // Tolerance setting for facial weights
                    var binding = EditorCurveBinding.FloatCurve(facePath, typeof(SkinnedMeshRenderer), "blendShape." + shapeName);
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
            }

            // 3. Ensure target directory exists and save the asset
            if (!Directory.Exists(ExportFolder))
            {
                Directory.CreateDirectory(ExportFolder);
            }

            AssetDatabase.CreateAsset(clip, ExportPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[AnimationClipBuilder] Native Animation Clip successfully saved to: {ExportPath}");
        }

        /// <summary>
        /// Custom linear collinearity keyframe reduction algorithm to minimize file footprint.
        /// Removes intermediate keys if they lie within a specified tolerance threshold.
        /// </summary>
        private static void ReduceKeyframes(AnimationCurve curve, float tolerance)
        {
            if (curve.length <= 2) return;

            // Iterate backwards through the curve to safely remove keys
            for (int i = curve.length - 2; i > 0; i--)
            {
                Keyframe prev = curve[i - 1];
                Keyframe curr = curve[i];
                Keyframe next = curve[i + 1];

                // Calculate time ratio
                float tRange = next.time - prev.time;
                float ratio = tRange > 0.0001f ? (curr.time - prev.time) / tRange : 0.5f;

                // Linearly interpolate value
                float lerpedVal = Mathf.Lerp(prev.value, next.value, ratio);

                // If deviation is within tolerance, the keyframe is redundant and removed
                if (Mathf.Abs(curr.value - lerpedVal) < tolerance)
                {
                    curve.RemoveKey(i);
                }
            }
        }

        /// <summary>
        /// Recursively resolves the relative hierarchy transform path from a root transform to a target transform.
        /// </summary>
        private static string GetTransformPath(Transform root, Transform target)
        {
            if (root == target) return "";

            string path = target.name;
            Transform parent = target.parent;
            while (parent != null && parent != root)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
    }
}
#endif
