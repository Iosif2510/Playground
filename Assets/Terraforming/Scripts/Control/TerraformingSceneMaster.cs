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
            if (player != null)
            {
                terrainRenderer.SetStreamingTarget(player.transform);
            }

            terrainRenderer.Initialize();
            densityField.InitializeField();
            if (player != null)
            {
                player.SetActive(true);
            }
        }
    }
}
