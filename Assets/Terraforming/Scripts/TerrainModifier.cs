using System.Collections.Generic;
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
        [SerializeField] private float modifySpeed = 10f;
        private float timer;

        private readonly List<FieldChunk> hitChunks = new();

        private void Awake()
        {
            mainCamera = Camera.main;
            timer = 0f;
        }

        private void Update()
        {
            if (mainCamera == null || densityField == null) return;

            ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            isHit = Physics.Raycast(ray, out rayHitPoint);
            
            if (!isHit) return;
            var timeSpan = 1 / modifySpeed;
            if (timer >= timeSpan)
            {
                timer -= timeSpan;
                densityField.GetChunksInRadius(rayHitPoint.point, modifyRadius, hitChunks);
                bool fieldModified = false;
                foreach (var densityChunk in hitChunks)
                {
                    if (Mouse.current.leftButton.isPressed) 
                        fieldModified |= densityChunk.ModifySphereVolume(rayHitPoint.point, modifyRadius, FieldChunk.ModifyMethod.Fill);
                    else if (Mouse.current.rightButton.isPressed) 
                        fieldModified |= densityChunk.ModifySphereVolume(rayHitPoint.point, modifyRadius, FieldChunk.ModifyMethod.Carve);
                }
                
                if (fieldModified)
                {
                    densityField.NotifyChunksUpdated(hitChunks);
                }
            }
            timer += Time.deltaTime;
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
