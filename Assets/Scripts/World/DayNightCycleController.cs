using UnityEngine;
using UnityEngine.Rendering;

namespace UnDeadHotel.World
{
    public class DayNightCycleController : MonoBehaviour
    {
        [Header("Dependencies")]
        public GameTimeSystem gameTimeSystem;

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

        private const float SecondsPerDay = 86400f;

        private void Awake()
        {
            EnsureDefaultGradients();
            ResolveSunLightIfNeeded();
            ResolveTimeSystem();
            ApplyDayNightVisuals();
        }

        private void Update()
        {
            ResolveTimeSystem();
            ApplyDayNightVisuals();
        }

        private void ResolveTimeSystem()
        {
            if (gameTimeSystem == null)
            {
                gameTimeSystem = GameTimeSystem.Instance != null
                    ? GameTimeSystem.Instance
                    : FindAnyObjectByType<GameTimeSystem>();
            }
        }

        private void ApplyDayNightVisuals()
        {
            if (gameTimeSystem == null)
            {
                return;
            }

            ResolveSunLightIfNeeded();
            float time01 = gameTimeSystem.CurrentTimeSeconds / SecondsPerDay;

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
    }
}
