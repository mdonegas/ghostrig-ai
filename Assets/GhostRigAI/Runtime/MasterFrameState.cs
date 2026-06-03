/*
 * COPYRIGHT & LICENSE NOTICE
 * This software is provided for portfolio evaluation and educational purposes only.
 * Unauthorized copying, modification, distribution, or commercial use of this code,
 * via any medium, is strictly prohibited. All rights reserved.
 */

using UnityEngine;
using System.Collections.Generic;

namespace GhostRigAI
{
    /// <summary>
    /// Represents the compiled, unified multimodal frame state of the humanoid rig (body + fingers + facial blendshapes + root position).
    /// </summary>
    public class MasterFrameState
    {
        public int FrameIndex;
        public float TimeStamp;
        public Vector3 HipsPosition;
        public Dictionary<HumanBodyBones, Quaternion> BoneRotations = new Dictionary<HumanBodyBones, Quaternion>();
        public Dictionary<string, float> BlendshapeWeights = new Dictionary<string, float>();
    }
}
