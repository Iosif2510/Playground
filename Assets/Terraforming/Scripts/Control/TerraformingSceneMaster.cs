using UnityEngine;

namespace Terraforming
{
    public class TerraformingSceneMaster : MonoBehaviour
    {
        [SerializeField] private TerrainRenderer terrainRenderer;
        [SerializeField] private ChunkedDensityField densityField;
        [SerializeField] private GameObject player;

        private void Start()
        {
            Initialize();
        }
        
        private void Initialize()
        {
            terrainRenderer.Initialize();
            densityField.InitializeField();
            player.SetActive(true);
        }
    }
}