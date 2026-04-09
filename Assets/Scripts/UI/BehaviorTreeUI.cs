using UnityEngine;
using UnityEngine.UI;
using TMPro; // Assuming TMPro is in the project, it's standard.
using UnDeadHotel.Player;
using UnDeadHotel.Actors;

namespace UnDeadHotel.UI
{
    public class BehaviorTreeUI : MonoBehaviour
    {
        private PlayerInteractor interactor;
        private CameraController cameraController;
        private TextMeshProUGUI headerText;
        private TextMeshProUGUI treeText;
        private Canvas uiCanvas;
        private GameObject panelRoot;

        private void Start()
        {
            interactor = FindAnyObjectByType<PlayerInteractor>();
            cameraController = FindAnyObjectByType<CameraController>();

            // Create Canvas Programmatically
            GameObject canvasGO = new GameObject("BehaviorTree_Canvas");
            uiCanvas = canvasGO.AddComponent<Canvas>();
            uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            uiCanvas.sortingOrder = 100; // Top layer
            
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGO.AddComponent<GraphicRaycaster>();

            // Create Root Card
            GameObject bgGO = new GameObject("Tree_Background");
            bgGO.transform.SetParent(canvasGO.transform, false);
            panelRoot = bgGO;
            var bgImage = bgGO.AddComponent<Image>();
            bgImage.color = new Color(0.055f, 0.07f, 0.09f, 0.92f);

            var panelShadow = bgGO.AddComponent<Shadow>();
            panelShadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            panelShadow.effectDistance = new Vector2(2f, -2f);
             
            RectTransform bgRect = bgImage.rectTransform;
            bgRect.anchorMin = new Vector2(0, 1);
            bgRect.anchorMax = new Vector2(0, 1);
            bgRect.pivot = new Vector2(0, 1);
            bgRect.anchoredPosition = new Vector2(18, -18);
            bgRect.sizeDelta = new Vector2(520, 420);

            // Header Bar
            GameObject headerGO = new GameObject("Tree_Header");
            headerGO.transform.SetParent(bgGO.transform, false);
            var headerImage = headerGO.AddComponent<Image>();
            headerImage.color = new Color(0.11f, 0.30f, 0.37f, 0.96f);

            RectTransform headerRect = headerImage.rectTransform;
            headerRect.anchorMin = new Vector2(0, 1);
            headerRect.anchorMax = new Vector2(1, 1);
            headerRect.pivot = new Vector2(0.5f, 1);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0, 56);

            GameObject headerTextGO = new GameObject("Tree_HeaderText");
            headerTextGO.transform.SetParent(headerGO.transform, false);
            headerText = headerTextGO.AddComponent<TextMeshProUGUI>();
            headerText.fontSize = 24;
            headerText.fontStyle = FontStyles.Bold;
            headerText.color = new Color(0.95f, 0.98f, 1f, 1f);
            headerText.alignment = TextAlignmentOptions.MidlineLeft;
            headerText.text = "GUEST BEHAVIOR";

            RectTransform headerTextRect = headerText.rectTransform;
            headerTextRect.anchorMin = new Vector2(0, 0);
            headerTextRect.anchorMax = new Vector2(1, 1);
            headerTextRect.pivot = new Vector2(0.5f, 0.5f);
            headerTextRect.offsetMin = new Vector2(16, 0);
            headerTextRect.offsetMax = new Vector2(-16, 0);

            // Content container
            GameObject contentGO = new GameObject("Tree_Content");
            contentGO.transform.SetParent(bgGO.transform, false);
            var contentImage = contentGO.AddComponent<Image>();
            contentImage.color = new Color(0.07f, 0.09f, 0.12f, 0.94f);

            RectTransform contentRect = contentImage.rectTransform;
            contentRect.anchorMin = new Vector2(0, 0);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 0.5f);
            contentRect.offsetMin = new Vector2(8, 8);
            contentRect.offsetMax = new Vector2(-8, -64);

            // Create Text Node
            GameObject textGO = new GameObject("Tree_Text");
            textGO.transform.SetParent(contentGO.transform, false);
            treeText = textGO.AddComponent<TextMeshProUGUI>();
            treeText.fontSize = 19;
            treeText.fontStyle = FontStyles.Normal;
            treeText.color = new Color(0.88f, 0.94f, 1f, 1f);
            treeText.alignment = TextAlignmentOptions.TopLeft;
            treeText.enableWordWrapping = true;
            treeText.lineSpacing = 5f;
            treeText.richText = true;

            // Set anchoring to fill content body with padding
            RectTransform rect = treeText.rectTransform;
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(14, 14);
            rect.offsetMax = new Vector2(-14, -14);

            // Keep alive
            DontDestroyOnLoad(canvasGO);
            DontDestroyOnLoad(this.gameObject);
        }

        private void Update()
        {
            if (interactor == null)
            {
                SetPanelVisible(false);
                return;
            }

            GuestController selectedGuest = interactor.selectedGuest;
            bool cameraFocusedOnGuest = selectedGuest != null
                && cameraController != null
                && cameraController.followTarget == selectedGuest.transform;

            if (!cameraFocusedOnGuest)
            {
                SetPanelVisible(false);
                return;
            }

            SetPanelVisible(true);

            if (headerText != null)
            {
                headerText.text = $"GUEST BEHAVIOR  •  {selectedGuest.gameObject.name.ToUpperInvariant()}";
            }

            // Fetch the updated tree state every frame
            string behaviorLog = $"<b><color=#B7F3FF>Health</color></b>: {selectedGuest.currentHealth:0.0}\n";
            behaviorLog += $"<b><color=#B7F3FF>Outside Comfort Left</color></b>: {selectedGuest.GetOutsideRoomComfortTimeRemaining():0.0}s\n\n";
            behaviorLog += selectedGuest.GetBehaviorTreeStatus();

            treeText.text = behaviorLog;
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
