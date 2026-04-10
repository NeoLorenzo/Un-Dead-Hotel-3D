using UnityEngine;

namespace UnDeadHotel.World
{
    public class InfectionConversionService : MonoBehaviour
    {
        public static InfectionConversionService Instance { get; private set; }

        [Header("Dependencies")]
        public PopulationSpawner populationSpawner;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("Multiple InfectionConversionService instances found. Replacing previous instance.");
            }

            Instance = this;
            ResolvePopulationSpawner();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public bool TryConvertGuestToZombie(Vector3 position, Quaternion rotation)
        {
            ResolvePopulationSpawner();
            if (populationSpawner == null)
            {
                Debug.LogWarning("Infection conversion skipped: PopulationSpawner dependency is missing.");
                return false;
            }

            return populationSpawner.TryConvertGuestToZombie(position, rotation);
        }

        private void ResolvePopulationSpawner()
        {
            if (populationSpawner == null)
            {
                populationSpawner = FindAnyObjectByType<PopulationSpawner>();
            }
        }
    }
}
