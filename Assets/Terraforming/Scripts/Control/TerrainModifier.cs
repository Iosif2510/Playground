using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Terraforming
{
    public class TerrainModifier : MonoBehaviour
    {
        [SerializeField] private Camera mainCamera;
        [SerializeField] private bool lockCursor = true;
        [SerializeField] private float minFillDistance = 1f;

        private RaycastHit rayHit;
        private Ray ray;
        private bool isHit;
        
        [SerializeField] private ChunkedDensityField densityField;
        [SerializeField] private float modifyRadius = 2f;
        [SerializeField] private float modifySpeed = 10f;
        private float timer;

        private readonly List<FieldChunk> hitChunks = new();

        private void Awake()
        {
            timer = 0f;
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
        }

        private void Update()
        {
            if (mainCamera == null || densityField == null) return;

            ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            isHit = Physics.Raycast(ray, out rayHit);
            
            if (!isHit) return;
            var timeSpan = 1 / modifySpeed;

            if ((Mouse.current.leftButton.isPressed && rayHit.distance >= minFillDistance || Mouse.current.rightButton.isPressed) && timer >= timeSpan)
            {
                timer -= timeSpan;
                densityField.GetChunksInRadius(rayHit.point, modifyRadius, hitChunks);
                bool fieldModified = false;
                foreach (var densityChunk in hitChunks)
                {
                    if (Mouse.current.leftButton.isPressed)
                        fieldModified |= densityChunk.ModifySphereVolume(rayHit.point, modifyRadius, FieldChunk.ModifyMethod.Fill);
                    else if (Mouse.current.rightButton.isPressed) 
                        fieldModified |= densityChunk.ModifySphereVolume(rayHit.point, modifyRadius, FieldChunk.ModifyMethod.Carve);
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
            var distance = isHit ? rayHit.distance : 100f;
            Gizmos.DrawRay(ray.origin, ray.direction * distance);
            if (isHit)
            {
                Gizmos.DrawWireSphere(rayHit.point, 0.1f);
            }
        }
    }
}
