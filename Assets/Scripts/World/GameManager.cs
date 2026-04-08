using UnityEngine;
using UnityEngine.AI;
using UnDeadHotel.Player;

namespace UnDeadHotel.World
{
    public class GameManager : MonoBehaviour
    {
        [Header("AI Spatial Index")]
        public float perceptionCellSize = 12f;
        public float perceptionRebuildInterval = 0.2f;

        [Header("Prefabs")]
        public GameObject survivorPrefab;

        [Header("Spawning")]
        public float spawnRadius = 150f;
        public Vector3 spawnCenter = new Vector3(176f, 0f, 176f);
        public float minSafeDistance = 30f;

        [Header("Zombies")]
        public GameObject zombiePrefab;
        public int initialZombieCount = 10;

        [Header("Guests")]
        public GameObject guestPrefab;
        public int initialGuestCount = 5;

        private GameObject playerInstance;
        private ActorSpatialIndex actorSpatialIndex;

        private void Awake()
        {
            actorSpatialIndex = ActorSpatialIndex.CreateRuntimeInstance(perceptionCellSize, perceptionRebuildInterval);
        }

        private void Start()
        {
            SpawnPlayer();
            SpawnZombies(initialZombieCount);
            SpawnGuests(initialGuestCount);

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

        private GameObject InstantiateAgentOnNavMesh(GameObject prefab, Vector3 spawnPos)
        {
            GameObject instance = Instantiate(prefab, spawnPos, Quaternion.identity);
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
