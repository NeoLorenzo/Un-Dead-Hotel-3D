using UnityEngine;
using UnDeadHotel.UI;

namespace UnDeadHotel.World
{
    public class DebugUIBootstrapper : MonoBehaviour
    {
        [Header("UI Bootstrap")]
        public bool spawnBehaviorTreeUI = true;
        public bool spawnTimeOfDayUI = true;

        private void Start()
        {
            if (spawnBehaviorTreeUI && FindAnyObjectByType<BehaviorTreeUI>() == null)
            {
                new GameObject("BehaviorTreeUI_Manager").AddComponent<BehaviorTreeUI>();
            }

            if (spawnTimeOfDayUI && FindAnyObjectByType<TimeOfDayUI>() == null)
            {
                new GameObject("TimeOfDayUI_Manager").AddComponent<TimeOfDayUI>();
            }
        }
    }
}
