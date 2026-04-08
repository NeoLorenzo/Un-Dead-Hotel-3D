using UnityEngine;

namespace UnDeadHotel.UI
{
    public class Billboard : MonoBehaviour
    {
        private Camera mainCamera;
        
        [HideInInspector]
        public Transform parentTarget;
        
        [HideInInspector]
        public float verticalOffset = 2.0f;
        
        [HideInInspector]
        public float screenUpOffset = 0.6f; // Offset based on typical capsule radius

        private void Start()
        {
            mainCamera = Camera.main;
        }

        private void LateUpdate()
        {
            if (mainCamera != null && parentTarget != null)
            {
                // Forces the UI to perfectly face the camera frame continuously
                transform.rotation = mainCamera.transform.rotation;
                
                // Natively drive the actual position:
                // Start exactly at the agent's feet, go straight up to their head,
                // then mathematically pad visually "UP" natively in screen-space based on the camera angle!
                // This guarantees the bar clears their body circumference perfectly even in top-down Orthographic perspectives.
                transform.position = parentTarget.position + (Vector3.up * verticalOffset) + (mainCamera.transform.up * screenUpOffset);
            }
            else if (mainCamera == null)
            {
                // Fallback attempt to find camera if destroyed
                mainCamera = Camera.main;
            }
        }
    }
}
