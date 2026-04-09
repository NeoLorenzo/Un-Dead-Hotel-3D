using UnityEngine;
using UnityEngine.AI;
using UnDeadHotel.Player;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace UnDeadHotel.World
{
    public class GameManager : MonoBehaviour
    {
        private const float SecondsPerDay = 86400f;

        public static GameManager Instance { get; private set; }

        [Header("AI Spatial Index")]
        public float perceptionCellSize = 12f;
        public float perceptionRebuildInterval = 0.2f;

        [Header("Time")]
        [Range(0f, 24f)] public float startHour = 8f;
        public float gameSecondsPerRealSecond = 3600f;
        public bool pauseTime = false;

        [Header("Day/Night Visuals")]
        public Light sunLight;
        public float sunYaw = 170f;
        public float sunPitchOffset = -90f;
        public AnimationCurve sunIntensityByTime = new AnimationCurve(
            new Keyframe(0.00f, 0.00f),
            new Keyframe(0.20f, 0.10f),
            new Keyframe(0.25f, 0.90f),
            new Keyframe(0.50f, 1.10f),
            new Keyframe(0.75f, 0.90f),
            new Keyframe(0.80f, 0.10f),
            new Keyframe(1.00f, 0.00f)
        );
        public Gradient sunColorByTime;
        public Gradient ambientSkyColorByTime;
        public Gradient ambientEquatorColorByTime;
        public Gradient ambientGroundColorByTime;

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
        private ActorSpatialIndex actorSpatialIndex;
        private int conversionCount;
        private readonly List<Transform> roomSpawnPoints = new List<Transform>(256);
        private Transform reservedPlayerSpawnPoint;
        private Transform reservedPlayerRoomRoot;
        private float currentTimeSeconds;

        public float CurrentTimeHours => currentTimeSeconds / 3600f;
        public float CurrentTimeSeconds => currentTimeSeconds;
        public string CurrentTimeFormatted
        {
            get
            {
                int totalMinutes = Mathf.FloorToInt(currentTimeSeconds / 60f) % 1440;
                int hour = totalMinutes / 60;
                int minute = totalMinutes % 60;
                return $"{hour:00}:{minute:00}";
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("Multiple GameManager instances found. Replacing previous instance.");
            }
            Instance = this;
            actorSpatialIndex = ActorSpatialIndex.CreateRuntimeInstance(perceptionCellSize, perceptionRebuildInterval);
            currentTimeSeconds = Mathf.Repeat(startHour * 3600f, SecondsPerDay);

            EnsureDefaultGradients();
            ResolveSunLightIfNeeded();
            ApplyDayNightVisuals();
        }

        private void Start()
        {
            SpawnPlayer();
            InitializePopulationFromSpawnPoints();

            // Spawn Behavior Tree Debug UI
            new GameObject("BehaviorTreeUI_Manager").AddComponent<UnDeadHotel.UI.BehaviorTreeUI>();
            if (FindAnyObjectByType<UnDeadHotel.UI.TimeOfDayUI>() == null)
            {
                new GameObject("TimeOfDayUI_Manager").AddComponent<UnDeadHotel.UI.TimeOfDayUI>();
            }
        }

        private void Update()
        {
            actorSpatialIndex?.Tick(Time.time);
            UpdateInGameTime();
            ApplyDayNightVisuals();
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

            // Link to interactor
            PlayerInteractor interactor = FindFirstObjectByType<PlayerInteractor>();
            if (interactor != null)
            {
                interactor.selectedSurvivor = playerInstance.GetComponent<SurvivorController>();
                
                // Focus camera on spawn point correctly via CameraController
                var camCtrl = Camera.main.GetComponent<CameraController>();
                if (camCtrl != null)
                {
                    camCtrl.SetPosition(spawnPos);
                }
                else
                {
                    Camera.main.transform.position = new Vector3(spawnPos.x, 50f, spawnPos.z);
                }
            }
            
            Debug.Log($"Survivor spawned at {spawnPos}");
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

        private void UpdateInGameTime()
        {
            if (!pauseTime)
            {
                float deltaGameSeconds = Time.deltaTime * Mathf.Max(0f, gameSecondsPerRealSecond);
                currentTimeSeconds = Mathf.Repeat(currentTimeSeconds + deltaGameSeconds, SecondsPerDay);
            }
        }

        private void ApplyDayNightVisuals()
        {
            ResolveSunLightIfNeeded();
            float time01 = currentTimeSeconds / SecondsPerDay;

            if (sunLight != null)
            {
                float pitch = time01 * 360f + sunPitchOffset;
                sunLight.transform.rotation = Quaternion.Euler(pitch, sunYaw, 0f);
                sunLight.intensity = Mathf.Max(0f, sunIntensityByTime.Evaluate(time01));
                sunLight.color = sunColorByTime.Evaluate(time01);
                RenderSettings.sun = sunLight;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambientSkyColorByTime.Evaluate(time01);
            RenderSettings.ambientEquatorColor = ambientEquatorColorByTime.Evaluate(time01);
            RenderSettings.ambientGroundColor = ambientGroundColorByTime.Evaluate(time01);
        }

        private void ResolveSunLightIfNeeded()
        {
            if (sunLight != null) return;

            if (RenderSettings.sun != null)
            {
                sunLight = RenderSettings.sun;
                return;
            }

            GameObject directionalByName = GameObject.Find("Directional Light");
            if (directionalByName != null)
            {
                Light namedLight = directionalByName.GetComponent<Light>();
                if (namedLight != null && namedLight.type == LightType.Directional)
                {
                    sunLight = namedLight;
                    return;
                }
            }

            Light[] allLights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < allLights.Length; i++)
            {
                if (allLights[i] != null && allLights[i].type == LightType.Directional)
                {
                    sunLight = allLights[i];
                    return;
                }
            }
        }

        private void EnsureDefaultGradients()
        {
            if (sunColorByTime == null)
            {
                sunColorByTime = new Gradient();
            }
            if (ambientSkyColorByTime == null)
            {
                ambientSkyColorByTime = new Gradient();
            }
            if (ambientEquatorColorByTime == null)
            {
                ambientEquatorColorByTime = new Gradient();
            }
            if (ambientGroundColorByTime == null)
            {
                ambientGroundColorByTime = new Gradient();
            }

            if (sunColorByTime.colorKeys == null || sunColorByTime.colorKeys.Length == 0)
            {
                sunColorByTime.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(0.08f, 0.10f, 0.22f), 0f),
                        new GradientColorKey(new Color(1.00f, 0.68f, 0.45f), 0.23f),
                        new GradientColorKey(new Color(1.00f, 0.97f, 0.88f), 0.50f),
                        new GradientColorKey(new Color(1.00f, 0.62f, 0.40f), 0.77f),
                        new GradientColorKey(new Color(0.08f, 0.10f, 0.22f), 1f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, 1f)
                    }
                );
            }

            if (ambientSkyColorByTime.colorKeys == null || ambientSkyColorByTime.colorKeys.Length == 0)
            {
                ambientSkyColorByTime.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(0.03f, 0.04f, 0.10f), 0f),
                        new GradientColorKey(new Color(0.35f, 0.49f, 0.73f), 0.30f),
                        new GradientColorKey(new Color(0.55f, 0.72f, 0.95f), 0.50f),
                        new GradientColorKey(new Color(0.30f, 0.40f, 0.60f), 0.75f),
                        new GradientColorKey(new Color(0.03f, 0.04f, 0.10f), 1f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, 1f)
                    }
                );
            }

            if (ambientEquatorColorByTime.colorKeys == null || ambientEquatorColorByTime.colorKeys.Length == 0)
            {
                ambientEquatorColorByTime.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(0.02f, 0.02f, 0.05f), 0f),
                        new GradientColorKey(new Color(0.22f, 0.22f, 0.30f), 0.30f),
                        new GradientColorKey(new Color(0.35f, 0.35f, 0.38f), 0.50f),
                        new GradientColorKey(new Color(0.24f, 0.20f, 0.22f), 0.75f),
                        new GradientColorKey(new Color(0.02f, 0.02f, 0.05f), 1f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, 1f)
                    }
                );
            }

            if (ambientGroundColorByTime.colorKeys == null || ambientGroundColorByTime.colorKeys.Length == 0)
            {
                ambientGroundColorByTime.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(0.01f, 0.01f, 0.02f), 0f),
                        new GradientColorKey(new Color(0.09f, 0.08f, 0.08f), 0.30f),
                        new GradientColorKey(new Color(0.18f, 0.18f, 0.16f), 0.50f),
                        new GradientColorKey(new Color(0.10f, 0.07f, 0.07f), 0.75f),
                        new GradientColorKey(new Color(0.01f, 0.01f, 0.02f), 1f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, 1f)
                    }
                );
            }
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
