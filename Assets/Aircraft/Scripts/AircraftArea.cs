using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Aircraft
{
    public class AircraftArea : MonoBehaviour
    {
        [Tooltip("The path the race will take.")]
        public CinemachineSmoothPath racePath;

        [Tooltip("The prefab to use for checkpoints.")]
        public GameObject checkPointPrefab;

        [Tooltip("The prefab to use for the start/end checkpoint.")]
        public GameObject finishCheckPointPrefab;

        [Tooltip("If true, enable training mode.")]
        public bool trainingMode;
        
        public List<AircraftAgent> aircraftAgents { get; private set; }
        public List<GameObject> checkpoints { get; private set; }
        public AircraftAcademy aircraftAcademy { get; private set; }

        private void Awake()
        {
            // Find all aircraft agents in the area.
            aircraftAgents = transform.GetComponentsInChildren<AircraftAgent>().ToList();
            Debug.Assert(aircraftAgents.Count > 0, "No aircraftAgent found.");

            aircraftAcademy = FindObjectOfType<AircraftAcademy>();
        }

        private void Start()
        {
            // Create checkpoints along the race path.
            Debug.Assert(racePath != null, "Race path was not set.");
            checkpoints = new List<GameObject>();
            int numCheckpoints = (int)racePath.MaxUnit(CinemachinePathBase.PositionUnits.PathUnits);
            Debug.Log(racePath.m_Waypoints.Count());

            GameObject checkPoint;
            for (int i = 0; i < numCheckpoints; i++)
            {
                // Instantiate weither a checkpoint or the finish line checkpoint.
                if (i == numCheckpoints - 1)
                    checkPoint = Instantiate<GameObject>(finishCheckPointPrefab);
                else
                    checkPoint = Instantiate<GameObject>(checkPointPrefab);

                // Set the parent, position and rotation.
                checkPoint.transform.SetParent(racePath.transform);
                checkPoint.transform.localPosition = racePath.m_Waypoints[i].position;
                checkPoint.transform.rotation = racePath.EvaluateOrientationAtUnit(i, CinemachinePathBase.PositionUnits.PathUnits);

                checkpoints.Add(checkPoint);
            }
        }

        /// <summary>
        /// Resets the position of an agent using its current <see cref="NextCheckpointIndex"/>, unless randomize
        /// is true, then will pick a new random checkpoint.
        /// </summary>
        /// <param name="agent">The agent to reset.</param>
        /// <param name="randomize">If true, will pick a new <see cref="NextCheckpointIndex"/> before reset.</param>
        public void ResetAgentPosition(AircraftAgent agent, bool randomize = true)
        {
            if (randomize)
            {
                // Pick a new next checkpoint at random.
                agent.nextCheckpointIndex = Random.Range(0, checkpoints.Count);
            }

            // Set start position to the previous checkpoint;
            int previousCheckpointIndex = agent.nextCheckpointIndex - 1;
            if (previousCheckpointIndex == -1) previousCheckpointIndex = checkpoints.Count - 1;

            float startPosition = racePath.FromPathNativeUnits(previousCheckpointIndex, CinemachinePathBase.PositionUnits.PathUnits);

            // Convert the position on the race path to a position in 3D space.
            Vector3 basePosition = racePath.EvaluatePosition(startPosition);

            // Get the orientation  at that position on the race path.
            Quaternion orientation = racePath.EvaluateOrientation(startPosition);

            // Calculate a horizontal offset so that agents are spread out.
            Vector3 positionOffset = Vector3.right * (aircraftAgents.IndexOf(agent) - aircraftAgents.Count / 2f) * 10f;

            // Set the aircraft position and rotation
            agent.transform.position = basePosition + orientation * positionOffset;
            agent.transform.rotation = orientation;
        }
    }
}