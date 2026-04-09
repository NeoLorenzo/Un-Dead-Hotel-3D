using UnityEngine;
using UnityEngine.AI;
using UnDeadHotel.Player;
using System.Collections.Generic;

namespace UnDeadHotel.World
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("AI Spatial Index")]
        public float perceptionCellSize = 12f;
        public float perceptionRebuildInterval = 0.2f;

        [Header("Prefabs")]
        public GameObject survivorPrefab;

        [Header("Spawning")]
        public float spawnRadius = 150f;
        public Vector3 spawnCenter = new Vector3(176f, 0f, 176f);
        public float minSafeDistance = 30f;

        [Header("Population")]
        public GameObject zombiePrefab;
        public GameObject guestPrefab;
        [Range(0f, 1f)] public float guestOccupancyRate = 0.6f;
        [Range(0f, 1f)] public float initialZombificationRate = 0.1f;
        public bool logPopulationSummary = true;
        public bool logConversions = false;

        private GameObject playerInstance;
        private ActorSpatialIndex actorSpatialIndex;
        private int conversionCount;
        private readonly List<Transform> roomSpawnPoints = new List<Transform>(256);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("Multiple GameManager instances found. Replacing previous instance.");
            }
            Instance = this;
            actorSpatialIndex = ActorSpatialIndex.CreateRuntimeInstance(perceptionCellSize, perceptionRebuildInterval);
        }

        private void Start()
        {
            SpawnPlayer();
            InitializePopulationFromSpawnPoints();

            // Spawn Behavior Tree Debug UI
            new GameObject("BehaviorTreeUI_Manager").AddComponent<UnDeadHotel.UI.BehaviorTreeUI>();
        }

        private void Update()
        {
            actorSpatialIndex?.Tick(Time.time);
        }

        private void OnDestroy()
        {
            if (ActorSpatialIndex.Instance == actorSpatialIndex)
            {
                ActorSpatialIndex.ClearRuntimeInstance();
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void SpawnPlayer()
        {
            if (survivorPrefab == null)
            {
                Debug.LogError("No survivor prefab assigned to GameManager.");
                return;
            }

            Vector3 randomPos = GetRandomNavMeshPoint();
            playerInstance = Instantiate(survivorPrefab, randomPos, Quaternion.identity);
            playerInstance.name = "Player_Survivor_Dynamic";

            // Link to interactor
            PlayerInteractor interactor = FindFirstObjectByType<PlayerInteractor>();
            if (interactor != null)
            {
                interactor.selectedSurvivor = playerInstance.GetComponent<SurvivorController>();
                
                // Focus camera on spawn point correctly via CameraController
                var camCtrl = Camera.main.GetComponent<CameraController>();
                if (camCtrl != null)
                {
                    camCtrl.SetPosition(randomPos);
                }
                else
                {
                    Camera.main.transform.position = new Vector3(randomPos.x, 50f, randomPos.z);
                }
            }
            
            Debug.Log($"Survivor spawned at {randomPos}");
        }

        public void SpawnZombies(int count)
        {
            if (zombiePrefab == null) return;

            int spawnedCount = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 spawnPos = GetRandomNavMeshPoint(true);
                if (InstantiateAgentOnNavMesh(zombiePrefab, spawnPos) != null)
                {
                    spawnedCount++;
                }
            }
            Debug.Log($"Spawned {spawnedCount}/{count} zombies.");
        }

        public void SpawnGuests(int count)
        {
            if (guestPrefab == null) return;

            int spawnedCount = 0;
            for (int i = 0; i < count; i++)
            {
                // Try to spawn guests in rooms initially if possible, or anywhere safe
                Vector3 spawnPos = GetRandomNavMeshPoint(true);
                if (InstantiateAgentOnNavMesh(guestPrefab, spawnPos) != null)
                {
                    spawnedCount++;
                }
            }
            Debug.Log($"Spawned {spawnedCount}/{count} guests.");
        }

        private void InitializePopulationFromSpawnPoints()
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

        private Vector3 GetRandomNavMeshPoint(bool checkSafeDist = false)
        {
            for (int i = 0; i < 50; i++)
            {
                Vector3 randomDir = Random.insideUnitSphere * spawnRadius;
                randomDir += spawnCenter;
                NavMeshHit hit;
                if (NavMesh.SamplePosition(randomDir, out hit, 15f, NavMesh.AllAreas))
                {
                    if (checkSafeDist && playerInstance != null)
                    {
                        if (Vector3.Distance(hit.position, playerInstance.transform.position) < minSafeDistance)
                            continue;
                    }
                    return hit.position;
                }
            }

            if (NavMesh.SamplePosition(spawnCenter, out NavMeshHit fallbackHit, 30f, NavMesh.AllAreas))
            {
                return fallbackHit.position;
            }

            return spawnCenter; // Hard fallback if no NavMesh nearby
        }

        private GameObject InstantiateAgentOnNavMesh(GameObject prefab, Vector3 spawnPos, Quaternion? spawnRotation = null)
        {
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
