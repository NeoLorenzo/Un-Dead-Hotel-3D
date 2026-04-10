using System.Collections.Generic;
using UnDeadHotel.Player;
using UnityEngine;
using UnityEngine.AI;

namespace UnDeadHotel.World
{
    public class PopulationSpawner : MonoBehaviour
    {
        [Header("Prefabs")]
        public GameObject survivorPrefab;

        [Header("Population")]
        public GameObject zombiePrefab;
        public GameObject guestPrefab;
        [Range(0f, 1f)] public float guestOccupancyRate = 0.6f;
        [Range(0f, 1f)] public float initialZombificationRate = 0.1f;
        public bool logPopulationSummary = true;
        public bool logConversions = false;

        private GameObject playerInstance;
        private int conversionCount;
        private readonly List<Transform> roomSpawnPoints = new List<Transform>(256);
        private Transform reservedPlayerSpawnPoint;
        private Transform reservedPlayerRoomRoot;

        public void SpawnPlayer()
        {
            if (survivorPrefab == null)
            {
                Debug.LogError("No survivor prefab assigned to PopulationSpawner.");
                return;
            }

            roomSpawnPoints.Clear();
            DiscoverRoomSpawnPoints(roomSpawnPoints);

            if (roomSpawnPoints.Count == 0)
            {
                Debug.LogError("No active room spawn points found (expected transforms named 'SP_Guest_*'). Survivor will not spawn.");
                return;
            }

            int randomIndex = Random.Range(0, roomSpawnPoints.Count);
            Transform selectedSpawnPoint = roomSpawnPoints[randomIndex];
            if (selectedSpawnPoint == null)
            {
                Debug.LogError("Selected room spawn point is null. Survivor will not spawn.");
                return;
            }

            reservedPlayerSpawnPoint = selectedSpawnPoint;
            reservedPlayerRoomRoot = GetRoomRoot(selectedSpawnPoint);

            Vector3 spawnPos = selectedSpawnPoint.position;
            Quaternion spawnRotation = selectedSpawnPoint.rotation;
            if (NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                spawnPos = hit.position;
            }

            playerInstance = Instantiate(survivorPrefab, spawnPos, spawnRotation);
            playerInstance.name = "Player_Survivor_Dynamic";

            PlayerInteractor interactor = FindFirstObjectByType<PlayerInteractor>();
            if (interactor != null)
            {
                interactor.selectedSurvivor = playerInstance.GetComponent<SurvivorController>();

                Camera mainCamera = Camera.main;
                CameraController camCtrl = mainCamera != null ? mainCamera.GetComponent<CameraController>() : null;
                if (camCtrl != null)
                {
                    camCtrl.SetPosition(spawnPos);
                }
                else if (mainCamera != null)
                {
                    mainCamera.transform.position = new Vector3(spawnPos.x, 50f, spawnPos.z);
                }
            }

            Debug.Log($"Survivor spawned at {spawnPos}");
        }

        public void InitializePopulationFromSpawnPoints()
        {
            if (guestPrefab == null || zombiePrefab == null)
            {
                Debug.LogWarning("Population initialization skipped: guest or zombie prefab is not assigned.");
                return;
            }

            roomSpawnPoints.Clear();
            DiscoverRoomSpawnPoints(roomSpawnPoints);

            int occupiedCount = 0;
            int spawnedGuests = 0;
            int spawnedZombies = 0;

            for (int i = 0; i < roomSpawnPoints.Count; i++)
            {
                Transform spawnPoint = roomSpawnPoints[i];
                if (spawnPoint == null) continue;
                if (IsReservedForPlayerRoom(spawnPoint)) continue;
                if (Random.value > guestOccupancyRate) continue;

                occupiedCount++;

                bool startsAsZombie = Random.value < initialZombificationRate;
                GameObject prefab = startsAsZombie ? zombiePrefab : guestPrefab;

                if (InstantiateAgentOnNavMesh(prefab, spawnPoint.position, spawnPoint.rotation) != null)
                {
                    if (startsAsZombie) spawnedZombies++;
                    else spawnedGuests++;
                }
            }

            if (logPopulationSummary)
            {
                Debug.Log($"Population init: totalSpawnPoints={roomSpawnPoints.Count}, occupied={occupiedCount}, guests={spawnedGuests}, zombies={spawnedZombies}");
            }
        }

        public bool TryConvertGuestToZombie(Vector3 position, Quaternion rotation)
        {
            if (zombiePrefab == null) return false;

            GameObject spawned = InstantiateAgentOnNavMesh(zombiePrefab, position, rotation);
            if (spawned == null) return false;

            conversionCount++;
            if (logConversions)
            {
                Debug.Log($"Guest converted to zombie. Total conversions: {conversionCount}");
            }

            return true;
        }

        private void DiscoverRoomSpawnPoints(List<Transform> buffer)
        {
            Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            for (int i = 0; i < allTransforms.Length; i++)
            {
                Transform t = allTransforms[i];
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                if (t.name.StartsWith("SP_Guest_"))
                {
                    buffer.Add(t);
                }
            }
        }

        private Transform GetRoomRoot(Transform spawnPoint)
        {
            if (spawnPoint == null) return null;
            return spawnPoint.parent != null ? spawnPoint.parent : spawnPoint;
        }

        private bool IsReservedForPlayerRoom(Transform spawnPoint)
        {
            if (spawnPoint == null) return false;
            if (reservedPlayerSpawnPoint != null && spawnPoint == reservedPlayerSpawnPoint) return true;
            if (reservedPlayerRoomRoot == null) return false;
            return GetRoomRoot(spawnPoint) == reservedPlayerRoomRoot;
        }

        private GameObject InstantiateAgentOnNavMesh(GameObject prefab, Vector3 spawnPos, Quaternion? spawnRotation = null)
        {
            if (prefab == null) return null;

            Quaternion rotation = spawnRotation ?? Quaternion.identity;
            GameObject instance = Instantiate(prefab, spawnPos, rotation);
            if (instance == null) return null;

            NavMeshAgent agent = instance.GetComponent<NavMeshAgent>();
            if (agent == null)
            {
                return instance;
            }

            if (agent.isOnNavMesh)
            {
                return instance;
            }

            if (NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                if (!agent.Warp(hit.position))
                {
                    instance.transform.position = hit.position;
                }

                if (agent.isOnNavMesh)
                {
                    return instance;
                }
            }

            Debug.LogWarning($"Failed to place '{prefab.name}' on NavMesh near {spawnPos}. Destroying instance.");
            Destroy(instance);
            return null;
        }
    }
}
