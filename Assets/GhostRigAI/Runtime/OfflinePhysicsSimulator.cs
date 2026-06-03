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
    /// Decouples Unity's PhysX simulation from real-time execution, allowing deterministic manual frame stepping.
    /// Highly optimized for offline bakers and editors.
    /// </summary>
    public class OfflinePhysicsSimulator
    {
        private bool originalAutoSimulation;

        /// <summary>
        /// Saves the current auto-simulation status and disables automatic PhysX stepping.
        /// </summary>
        public void StartManualSimulation()
        {
            // Cache the original state to prevent breaking the editor/game state when baking ends
            originalAutoSimulation = Physics.autoSimulation;
            
            // Force disable auto-simulation to put PhysX under manual script control
            Physics.autoSimulation = false;
        }

        /// <summary>
        /// Forces the physics engine to run a single simulation step.
        /// </summary>
        /// <param name="stepTime">The physics delta time step (typically 1.0f / framerate).</param>
        public void SimulateStep(float stepTime)
        {
            if (stepTime <= 0f) return;

            // Trigger immediate synchronous simulation of the PhysX scene for the current frame
            Physics.Simulate(stepTime);
        }

        /// <summary>
        /// Restores Unity's physics simulation state back to its original automatic behavior.
        /// </summary>
        public void StopManualSimulation()
        {
            // Restore original physics behavior
            Physics.autoSimulation = originalAutoSimulation;
        }
    }
}
