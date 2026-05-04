using UnityEngine;

namespace Terraforming
{
    public class SimpleMarchingCubeGenerator : MonoBehaviour
    {
        // [SerializeField] private SDFGenerator sdfGenerator;
        [SerializeField] private SimpleDensityField densityField;
        [SerializeField, Range(-5f, 5f)] private float isoLevel = 0.0f; // 등고선 레벨
        

        
        private void Start()
        {
            densityField.InitializeField();
            // ShowDots();
        }


    }
}