﻿using MLAgents;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Aircraft
{
    public class AircraftAgent : Agent
    {
        [Header("Movement parameters")]
        public float thrust = 100000f;
        public float pitchSpeed = 100f;
        public float yawSpeed = 100f;
        public float rollSpeed = 100f;
        public float boostMulitplier = 2f;
        public int nextCheckpointIndex;

        [Header("Explosion parameters")]
        [Tooltip("The aircraft mesh that will disappear on explosion.")]
        public GameObject meshObject;

        [Tooltip("The GameObject of the explosion particle effect.")]
        public GameObject explosionEffect;

        [Header("Training")]
        [Tooltip("Number of steps to timeout after in training.")]
        public int stepTimeOut = 300;

        // Components to keep track of.
        private AircraftArea area;
        new private Rigidbody rigidbody;
        private TrailRenderer trail;
        private RayPerception3D rayPerception;

        // When the next step timeout will be during training
        private float nextStepTimeout;

        // Whether the aircraft is frozen (intentionally not flying)
        private bool frozen = false;

        // Controls
        private float pitchChange = 0f;
        private float smoothPitchChange = 0f;
        private float maxPitchAngle = 45f;

        private float yawChange = 0f;
        private float smoothYawChange = 0f;

        private float rollChange = 0f;
        private float smoothRollChange = 0f;
        private float maxRollAngle = 45f;

        private bool boost;

        public override void InitializeAgent()
        {
            base.InitializeAgent();
            area = this.GetComponentInParent<AircraftArea>();
            rigidbody = this.GetComponent<Rigidbody>();
            trail = this.GetComponent<TrailRenderer>();
            rayPerception = this.GetComponent<RayPerception3D>();

            // Overrides the max step set in the inspector.
            // Max 5000 steps if training, infinity if playing.
            agentParameters.maxStep = area.trainingMode ? 5000 : 0;
        }
        
        /// <summary>
        /// Reas action inputs from vectorAction.
        /// </summary>
        /// <param name="vectorAction">The chosen actions.</param>
        /// <param name="textAction">The chosen text action (not used).</param>
        public override void AgentAction(float[] vectorAction, string textAction)
        {
            // Read values for pitch and yaw.
            pitchChange = vectorAction[0]; // up or none
            if (pitchChange == 2)
                pitchChange = -1f; // down

            yawChange = vectorAction[1]; // turn right or none
            if (yawChange == 2)
                yawChange = -1f; // Turn left

            // Read value for boost and enable/disable trail renderer
            boost = vectorAction[2] == 1;
            if (boost && !trail.emitting)
                trail.Clear();

            trail.emitting = boost;

            if (frozen)
                return;

            ProcessMovement();

            if (area.trainingMode)
            {
                // Small negative reward every step.
                AddReward(-1f / agentParameters.maxStep);

                // Make sure we haven't run out of time if training.
                if (GetStepCount() > nextStepTimeout)
                {
                    AddReward(-.5f);
                    Done();
                }

                Vector3 localCheckpointDir = VectorToNextCheckpoint();
                if (localCheckpointDir.magnitude < area.aircraftAcademy.resetParameters["checkpoint_radius"])
                {
                    GotCehckpoint();
                }
            }
        }

        /// <summary>
        /// Collects observations used by agent to make decisions.
        /// </summary>
        public override void CollectObservations()
        {
            // Observe aircraft velocity (1 vector3 = 3 values)
            AddVectorObs(transform.InverseTransformDirection(rigidbody.velocity));

            // Where is the next checkpoint ? (1 vector3 = 3 values)
            AddVectorObs(VectorToNextCheckpoint());

            // Orientation of the next checkpoint (1 vector3 = 3 values)
            Vector3 nextCheckpointForward = area.checkpoints[nextCheckpointIndex].transform.forward;
            AddVectorObs(transform.InverseTransformDirection(nextCheckpointForward));

            // Observe ray perception results
            string[] detectableObjects = { "Untagged", "checkpoint" };

            // Look ahead and upward
            // (2tags + 1hit/not + 1distance to obj * 3 ray angles = 12 values)
            AddVectorObs(rayPerception.Perceive(
                rayDistance: 250f,
                rayAngles: new float[] {60f, 90f, 120f},
                detectableObjects: detectableObjects,
                startOffset: 0f,
                endOffset: 75f
            ));

            // Look center at several angles along the horizon
            // (2tags + 1hit/not + 1distance to obj * 7 ray angles = 28 values)
            AddVectorObs(rayPerception.Perceive(
                rayDistance: 250f,
                rayAngles: new float[] { 60f, 70f, 80f ,90f, 100f, 110f, 120f },
                detectableObjects: detectableObjects,
                startOffset: 0f,
                endOffset: 75f
            ));

            // Look ahead and downward
            // (2tags + 1hit/not + 1distance to obj * 3 ray angles = 12 values)
            AddVectorObs(rayPerception.Perceive(
                rayDistance: 250f,
                rayAngles: new float[] { 60f, +0f, 120f },
                detectableObjects: detectableObjects,
                startOffset: 0f,
                endOffset: -75f
            ));

            // Total observations = 3 + 3 + 3 + 12 + 28 + 12 = 61
        }

        public override void AgentReset()
        {
            // Reset the velocity, position and orientation
            rigidbody.velocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
            trail.emitting = false;
            area.ResetAgentPosition(agent: this, randomize: area.trainingMode);

            // Update the step timeout if training
            if (area.trainingMode)
                nextStepTimeout = GetStepCount() + stepTimeOut;
        }
    
        /// <summary>
        /// Prevent the agent from moving and taking actions.
        /// </summary>
        public void FreezeAgent()
        {
            Debug.Assert(area.trainingMode == false, "Freeze/Thaw not supported in training.");
            frozen = true;
            rigidbody.Sleep();
            trail.emitting = false;
        }

        /// <summary>
        /// Resume agent movements and actions.
        /// </summary>
        public void ThawAgent()
        {
            Debug.Assert(area.trainingMode == false, "Freeze/Thaw not supported in training.");
            frozen = false;
            rigidbody.WakeUp();
        }

        /// <summary>
        /// Called when the agent flies through the correct checkpoint.
        /// </summary>
        private void GotCehckpoint()
        {
            // Next chackpoint reached, update
            nextCheckpointIndex = (nextCheckpointIndex + 1) % area.checkpoints.Count;

            if (area.trainingMode)
            {
                AddReward(.5f);
                nextStepTimeout = GetStepCount() + stepTimeOut;
            }
        }

        /// <summary>
        /// Gets a vector to the next checkpoint the agent needs to fly through;
        /// </summary>
        /// <returns>A local-space vector.</returns>
        private Vector3 VectorToNextCheckpoint()
        {
            Vector3 nextChecpointDir = area.checkpoints[nextCheckpointIndex].transform.position - transform.position;
            Vector3 localCheckpointDir = transform.InverseTransformDirection(nextChecpointDir);
            return localCheckpointDir;
        }

        /// <summary>
        /// Calculate and apply movement.
        /// </summary>
        private void ProcessMovement()
        {
            // Calculate boost.
            float boostModifier = boost ? boostMulitplier : 1f;

            // Apply forward thrust.
            rigidbody.AddForce(transform.forward * thrust * boostModifier, ForceMode.Force);

            // Get the current rotation.
            Vector3 currentRotation = transform.rotation.eulerAngles;

            // Calculate the roll angle (between -180 and 180)
            float rollAngle = currentRotation.z > 180 ? currentRotation.z - 360f : currentRotation.z;

            if (yawChange == 0)
            {
                // Not turning; smoothly roll toward center.
                rollChange = -rollAngle / maxRollAngle;
            }
            else
            {
                // Turning; roll in opposite direction of turn.
                rollChange = -yawChange;
            }

            // Calculate smooth deltas
            smoothPitchChange = Mathf.MoveTowards(smoothPitchChange, pitchChange, 2f * Time.fixedDeltaTime);
            smoothYawChange = Mathf.MoveTowards(smoothYawChange, yawChange, 2f * Time.fixedDeltaTime);
            smoothRollChange = Mathf.MoveTowards(smoothRollChange, rollChange, 2f * Time.fixedDeltaTime);

            // Calculate new pitch, yaw and roll. Clamp pitch and roll.
            float pitch = ClampAngle(currentRotation.x + smoothPitchChange * Time.fixedDeltaTime * pitchSpeed, -maxPitchAngle, maxPitchAngle);
            float yaw = currentRotation.y + smoothYawChange * Time.fixedDeltaTime * yawSpeed;
            float roll = ClampAngle(currentRotation.z + smoothRollChange * Time.fixedDeltaTime * rollSpeed, -maxRollAngle, maxRollAngle );

            // Set the new rotation.
            transform.rotation = Quaternion.Euler(pitch, yaw, roll);

        }

        /// <summary>
        /// Clamps an angle between two values.
        /// </summary>
        /// <param name="angle">The input angle.</param>
        /// <param name="from">The lower limit.</param>
        /// <param name="to">The upper limit.</param>
        /// <returns></returns>
        private static float ClampAngle(float angle, float from, float to)
        {
            if (angle < 0f) angle += 360f;
            if (angle > 180f) return Mathf.Max(angle, 360f + from);
            return Mathf.Min(angle, to);
        }

        /// <summary>
        /// React too entering a trigger.
        /// </summary>
        /// <param name="other">The collider entered.</param>
        private void OnTriggerEnter(Collider other)
        {
            if (other.transform.CompareTag("checkpoint") && other.gameObject == area.checkpoints[nextCheckpointIndex])
            {
                GotCehckpoint();
            }
        }

        /// <summary>
        /// React to collisions.
        /// </summary>
        /// <param name="collision">Collision info.</param>
        private void OnCollisionEnter(Collision collision)
        {
            if (!collision.transform.CompareTag("agent"))
            {
                if (area.trainingMode)
                {
                    AddReward(-1f);
                    Done();
                    return;
                }
                else
                {
                    StartCoroutine(ExplosionReset());
                }

            }
        }

        /// <summary>
        /// Resets the aircraft to the most recent complete checkpoint.
        /// </summary>
        /// <returns></returns>
        private IEnumerator ExplosionReset()
        {
            FreezeAgent();
             
            // Disable aircraft mesh object, enable explosion
            meshObject.SetActive(false);
            explosionEffect.SetActive(true);
            yield return new WaitForSeconds(2f);

            // Disable explosion, re-enable aircraft mesh
            meshObject.SetActive(true);
            explosionEffect.SetActive(false);
            area.ResetAgentPosition(agent: this);
            yield return new WaitForSeconds(1f);
        }
    }
}
