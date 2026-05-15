using UnityEngine;
using UnityEngine.InputSystem;

namespace Terraforming
{
    public class TerrainModifier : MonoBehaviour
    {
        private Camera mainCamera;

        private RaycastHit rayHitPoint;
        private Ray ray;
        private bool isHit;
        
        [SerializeField] private ChunkedDensityField densityField;
        [SerializeField] private float modifyRadius = 2f;

        private void Awake()
        {
            mainCamera = Camera.main;
        }

        private void Update()
        {
            if (mainCamera == null || densityField == null) return;

            ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            isHit = Physics.Raycast(ray, out rayHitPoint);

            if (isHit)
            {
                var chunks = densityField.GetChunksInRadius(rayHitPoint.point, modifyRadius);
                foreach (var densityChunk in chunks)
                {
                    if (Mouse.current.leftButton.isPressed) 
                        densityChunk.ModifySphereVolume(rayHitPoint.point, modifyRadius, FieldChunk.ModifyMethod.Fill);
                    else if (Mouse.current.rightButton.isPressed) 
                        densityChunk.ModifySphereVolume(rayHitPoint.point, modifyRadius, FieldChunk.ModifyMethod.Carve);
                }
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            var distance = isHit ? rayHitPoint.distance : 100f;
            Gizmos.DrawRay(ray.origin, ray.direction * distance);
            if (isHit)
            {
                Gizmos.DrawWireSphere(rayHitPoint.point, 0.1f);
            }
        }
    }
}
