using UnityEngine;

namespace UnDeadHotel.World
{
    public class ActorSpatialIndexRuntime : MonoBehaviour
    {
        [Header("AI Spatial Index")]
        public float perceptionCellSize = 12f;
        public float perceptionRebuildInterval = 0.2f;

        private ActorSpatialIndex actorSpatialIndex;

        private void Awake()
        {
            actorSpatialIndex = ActorSpatialIndex.CreateRuntimeInstance(perceptionCellSize, perceptionRebuildInterval);
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
    }
}
