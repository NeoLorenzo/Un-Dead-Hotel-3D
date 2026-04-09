using UnityEngine;
using UnityEngine.UI;

namespace UnDeadHotel.Actors
{
    public class BaseActor : MonoBehaviour
    {
        public const int UnknownTeamId = -1;

        [Header("Common Stats")]
        public float maxHealth = 100f;
        public float currentHealth;
        public float moveSpeed = 3.5f;

        [Header("Identification")]
        public int teamID; // 0 for Humans, 1 for Zombies
        public string actorName;

        [Header("Visual Feedback")]
        public Color flashColor = Color.red;
        public float flashDuration = 0.1f;
        private Renderer actorRenderer;
        private Color originalColor;
        
        // --- UI ---
        private Canvas healthCanvas;
        private Image healthFillImage;
        private bool healthBarInitialized = false;
        protected int lastDamageSourceTeamID = UnknownTeamId;

        protected virtual void Start()
        {
            currentHealth = maxHealth;
            lastDamageSourceTeamID = UnknownTeamId;
            actorRenderer = GetComponent<Renderer>();
            if (actorRenderer != null) originalColor = actorRenderer.material.color;
        }

        public virtual void TakeDamage(float amount)
        {
            TakeDamage(amount, UnknownTeamId);
        }

        public virtual void TakeDamage(float amount, int sourceTeamID)
        {
            if (!healthBarInitialized) InitializeHealthBar();

            lastDamageSourceTeamID = sourceTeamID;
            currentHealth -= amount;
            UpdateHealthBar();
            
            StartCoroutine(FlashRoutine());
            if (currentHealth <= 0)
            {
                Die();
            }
        }
        
        private void InitializeHealthBar()
        {
            if (healthBarInitialized) return;

            // 1. Create Canvas Container
            GameObject canvasObj = new GameObject("HealthCanvas");
            // Important: We NO LONGER set it as a parent mathematically, 
            // because Billboard will manually push its absolute position every single frame
            // based on the agent's absolute rotation vs the camera's up vector.
            // But we can put it in a hierarchy folder if we want. For now, detaching avoids weird scale inheritance!
            
            // Adjust to scale cleanly into World Space natively
            canvasObj.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
            
            healthCanvas = canvasObj.AddComponent<Canvas>();
            healthCanvas.renderMode = RenderMode.WorldSpace;
            healthCanvas.sortingOrder = 100;

            // Attach the lock script so it physically faces the camera and handles positioning natively
            UnDeadHotel.UI.Billboard billboardScript = canvasObj.AddComponent<UnDeadHotel.UI.Billboard>();
            billboardScript.parentTarget = this.transform;
            billboardScript.verticalOffset = 2.0f;     // Float roughly above their head
            billboardScript.screenUpOffset = 0.6f;     // Safely bleed them off the radius visual plane

            // 2. Create physical Background block
            GameObject bgObj = new GameObject("HealthBG");
            bgObj.transform.SetParent(canvasObj.transform, false);
            Image bgImg = bgObj.AddComponent<Image>();
            bgImg.color = new Color(0.15f, 0.0f, 0.0f, 0.8f);
            RectTransform bgRect = bgImg.GetComponent<RectTransform>();
            bgRect.sizeDelta = new Vector2(100f, 15f);

            // 3. Create active Fill block on top
            GameObject fillObj = new GameObject("HealthFill");
            fillObj.transform.SetParent(canvasObj.transform, false);
            healthFillImage = fillObj.AddComponent<Image>();
            healthFillImage.color = new Color(0.0f, 0.8f, 0.2f, 0.9f); // Bright Green
            
            RectTransform fillRect = healthFillImage.GetComponent<RectTransform>();
            fillRect.sizeDelta = new Vector2(100f, 15f);
            
            // Pin the pivot and position to the absolute left edge so scaling correctly drains to the left!
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = new Vector2(-50f, 0f);

            healthBarInitialized = true;
        }
        
        private void UpdateHealthBar()
        {
            if (!healthBarInitialized || healthFillImage == null) return;
            
            float percent = Mathf.Clamp01(currentHealth / maxHealth);
            RectTransform fillRect = healthFillImage.rectTransform;
            
            // Scale X-width down appropriately. Because pivot is 0 on Left, it physically shrinks towards the left edge!
            fillRect.sizeDelta = new Vector2(100f * percent, 15f);
            
            // Shift color from Green to Red based on health percentage remaining
            healthFillImage.color = Color.Lerp(Color.red, Color.green, percent);
        }

        private System.Collections.IEnumerator FlashRoutine()
        {
            if (actorRenderer == null) yield break;
            actorRenderer.material.color = flashColor;
            yield return new WaitForSeconds(flashDuration);
            actorRenderer.material.color = originalColor;
        }

        protected virtual void Die()
        {
            Debug.Log($"{gameObject.name} has died.");
            if (healthCanvas != null) Destroy(healthCanvas.gameObject);
            Destroy(gameObject);
        }
    }
}
