using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// Turns this object around the vertical axis so text on it faces the player's camera.
    /// </summary>
    public class Billboard : MonoBehaviour
    {
        private Transform cameraTransform;

        private void LateUpdate()
        {
            if (cameraTransform == null)
            {
                Camera main = Camera.main;
                if (main == null)
                {
                    return;
                }
                cameraTransform = main.transform;
            }

            Vector3 fromCamera = transform.position - cameraTransform.position;
            fromCamera.y = 0f;
            if (fromCamera.sqrMagnitude > 1e-4f)
            {
                // TextMeshPro reads correctly when the camera looks along the text's +Z.
                transform.rotation = Quaternion.LookRotation(fromCamera);
            }
        }
    }
}
