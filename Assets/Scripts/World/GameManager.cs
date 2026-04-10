using UnityEngine;

namespace UnDeadHotel.World
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Runtime Systems")]
        [SerializeField] private ActorSpatialIndexRuntime actorSpatialIndexRuntime;
        [SerializeField] private GameTimeSystem gameTimeSystem;
        [SerializeField] private DayNightCycleController dayNightCycleController;
        [SerializeField] private PopulationSpawner populationSpawner;
        [SerializeField] private InfectionConversionService infectionConversionService;
        [SerializeField] private DebugUIBootstrapper debugUIBootstrapper;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("Multiple GameManager instances found. Replacing previous instance.");
            }

            Instance = this;
            ResolveDependencies();
            WireDependencies();
        }

        private void Start()
        {
            ResolveDependencies();
            WireDependencies();

            if (populationSpawner != null)
            {
                populationSpawner.SpawnPlayer();
                populationSpawner.InitializePopulationFromSpawnPoints();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void ResolveDependencies()
        {
            actorSpatialIndexRuntime = ResolveComponent(actorSpatialIndexRuntime);
            gameTimeSystem = ResolveComponent(gameTimeSystem);
            dayNightCycleController = ResolveComponent(dayNightCycleController);
            populationSpawner = ResolveComponent(populationSpawner);
            infectionConversionService = ResolveComponent(infectionConversionService);
            debugUIBootstrapper = ResolveComponent(debugUIBootstrapper);
        }

        private T ResolveComponent<T>(T existing) where T : Component
        {
            if (existing != null) return existing;

            T component = GetComponent<T>();
            if (component != null) return component;
            return FindAnyObjectByType<T>();
        }

        private void WireDependencies()
        {
            if (dayNightCycleController != null && dayNightCycleController.gameTimeSystem == null)
            {
                dayNightCycleController.gameTimeSystem = gameTimeSystem;
            }

            if (infectionConversionService != null && infectionConversionService.populationSpawner == null)
            {
                infectionConversionService.populationSpawner = populationSpawner;
            }
        }
    }
}
