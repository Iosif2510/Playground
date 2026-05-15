using System;
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
        
        private LayerMask terrainLayerMask;
        [SerializeField] private SimpleMarchingCubeGenerator marchingCubeGenerator;

        [SerializeField] private float modifyRadius = 2f;

        private void Awake()
        {
            mainCamera = Camera.main;
            terrainLayerMask = LayerMask.GetMask("Terrain");
        }

        private void Update()
        {
            ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            isHit = Physics.Raycast(ray, out rayHitPoint);
            // isHit = Physics.Raycast(ray, out rayHitPoint, 100f, terrainLayerMask);
            if (isHit)
            {
                // var densityChunk = marchingCubeGenerator.GetChunk(rayHitPoint.point);
                var chunks = marchingCubeGenerator.GetChunksInRadius(rayHitPoint.point, modifyRadius);
                foreach (var densityChunk in chunks)
                {
                    if (Mouse.current.leftButton.isPressed) densityChunk.ModifySphereVolume(rayHitPoint.point, modifyRadius, SimpleDensityField.ModifyMethod.Fill);
                    else if (Mouse.current.rightButton.isPressed) densityChunk.ModifySphereVolume(rayHitPoint.point, modifyRadius, SimpleDensityField.ModifyMethod.Carve);
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
