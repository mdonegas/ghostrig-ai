/*
 * COPYRIGHT & LICENSE NOTICE
 * This software is provided for portfolio evaluation and educational purposes only.
 * Unauthorized copying, modification, distribution, or commercial use of this code,
 * via any medium, is strictly prohibited. All rights reserved.
 */

using UnityEngine;

namespace GhostRigAI
{
    /// <summary>
    /// Visual debugger component for the GhostRig AI pipeline.
    /// Draws real-time gizmos in the Scene View to represent physics boundaries, tether lengths, and hand crop coordinates.
    /// </summary>
    public class GhostRigTelemetry : MonoBehaviour
    {
        [Header("Debugger State")]
        public bool showTelemetry = true;

        [Header("Tether Configuration")]
        public Vector3 anchorPoint = Vector3.zero;
        public float maxTetherLength = 5.0f;

        [Header("Dynamic Targets")]
        public Transform hipsTransform;
        public Vector3 leftWristWorldPos = Vector3.zero;
        public Vector3 rightWristWorldPos = Vector3.zero;

        private void OnDrawGizmos()
        {
            if (!showTelemetry) return;

            // 1. Draw boundary sphere for Tether Constraint (Neon Blue Translucent)
            Gizmos.color = new Color(0.12f, 0.58f, 0.95f, 0.25f);
            Gizmos.DrawWireSphere(anchorPoint, maxTetherLength);

            // Draw anchor point (Solid Neon Blue)
            Gizmos.color = new Color(0.12f, 0.58f, 0.95f, 1.0f);
            Gizmos.DrawSphere(anchorPoint, 0.12f);

            // 2. Draw physical tether cord connecting Hips to Anchor (Magenta)
            if (hipsTransform != null)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(anchorPoint, hipsTransform.position);
                Gizmos.DrawWireSphere(hipsTransform.position, 0.08f);
            }

            // 3. Draw crop bounds around Wrists for Hand Ingestion (Yellow)
            Gizmos.color = Color.yellow;
            if (leftWristWorldPos != Vector3.zero)
            {
                Gizmos.DrawWireSphere(leftWristWorldPos, 0.12f);
            }
            if (rightWristWorldPos != Vector3.zero)
            {
                Gizmos.DrawWireSphere(rightWristWorldPos, 0.12f);
            }
        }
    }
}
