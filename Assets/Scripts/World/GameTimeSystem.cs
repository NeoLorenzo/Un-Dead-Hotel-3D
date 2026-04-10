using UnityEngine;

namespace UnDeadHotel.World
{
    public class GameTimeSystem : MonoBehaviour
    {
        private const float SecondsPerDay = 86400f;

        public static GameTimeSystem Instance { get; private set; }

        [Header("Time")]
        [Range(0f, 24f)] public float startHour = 8f;
        public float gameSecondsPerRealSecond = 3600f;
        public bool pauseTime = false;

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
                Debug.LogWarning("Multiple GameTimeSystem instances found. Replacing previous instance.");
            }

            Instance = this;
            currentTimeSeconds = Mathf.Repeat(startHour * 3600f, SecondsPerDay);
        }

        private void Update()
        {
            if (!pauseTime)
            {
                float deltaGameSeconds = Time.deltaTime * Mathf.Max(0f, gameSecondsPerRealSecond);
                currentTimeSeconds = Mathf.Repeat(currentTimeSeconds + deltaGameSeconds, SecondsPerDay);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void SetCurrentTimeFromHours(float hours)
        {
            currentTimeSeconds = Mathf.Repeat(hours * 3600f, SecondsPerDay);
        }
    }
}
