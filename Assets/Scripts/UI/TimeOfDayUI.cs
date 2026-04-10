using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnDeadHotel.World;

namespace UnDeadHotel.UI
{
    public class TimeOfDayUI : MonoBehaviour
    {
        private Canvas uiCanvas;
        private GameObject panelRoot;
        private TextMeshProUGUI timeText;

        private void Start()
        {
            BuildUi();
        }

        private void Update()
        {
            if (GameTimeSystem.Instance == null)
            {
                SetPanelVisible(false);
                return;
            }

            SetPanelVisible(true);
            timeText.text = GameTimeSystem.Instance.CurrentTimeFormatted;
        }

        private void BuildUi()
        {
            GameObject canvasGO = new GameObject("TimeOfDay_Canvas");
            uiCanvas = canvasGO.AddComponent<Canvas>();
            uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            uiCanvas.sortingOrder = 110;

            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGO.AddComponent<GraphicRaycaster>();

            panelRoot = new GameObject("TimeOfDay_Panel");
            panelRoot.transform.SetParent(canvasGO.transform, false);
            Image panelImage = panelRoot.AddComponent<Image>();
            panelImage.color = new Color(0.06f, 0.08f, 0.12f, 0.9f);

            Shadow panelShadow = panelRoot.AddComponent<Shadow>();
            panelShadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
            panelShadow.effectDistance = new Vector2(2f, -2f);

            RectTransform panelRect = panelImage.rectTransform;
            panelRect.anchorMin = new Vector2(1f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = new Vector2(-18f, -18f);
            panelRect.sizeDelta = new Vector2(180f, 58f);

            GameObject textGO = new GameObject("TimeOfDay_Text");
            textGO.transform.SetParent(panelRoot.transform, false);
            timeText = textGO.AddComponent<TextMeshProUGUI>();
            timeText.fontSize = 30f;
            timeText.fontStyle = FontStyles.Bold;
            timeText.alignment = TextAlignmentOptions.MidlineRight;
            timeText.color = new Color(0.88f, 0.96f, 1f, 1f);
            timeText.text = "--:--";

            RectTransform textRect = timeText.rectTransform;
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.offsetMin = new Vector2(12f, 8f);
            textRect.offsetMax = new Vector2(-12f, -8f);

            DontDestroyOnLoad(canvasGO);
            DontDestroyOnLoad(gameObject);
        }

        private void SetPanelVisible(bool isVisible)
        {
            if (panelRoot != null && panelRoot.activeSelf != isVisible)
            {
                panelRoot.SetActive(isVisible);
            }
        }
    }
}
