using Unity.Mathematics;

namespace Terraforming
{
    public class PerlinLandscapeGenerator : DensityFieldGenerator
    {
        public float2 Period { get; private set; }
        
        public PerlinLandscapeGenerator(float2 period)
        {
            Period = period;
        }
        
        public override void GenerateField(FieldData[] field, float3 position, int resolution, float unitSize)
        {
            for (var i = 0; i < resolution * resolution * resolution; i++)
            {
                var x = i % resolution;
                var y = (i / resolution) % resolution;
                var z = i / (resolution * resolution);
                var center = new float3(resolution / 2f, resolution / 2f, resolution / 2f);
                var pos = (new float3(x, y, z) - center) * unitSize + position;
                
                field[i].position = pos;
                field[i].density = noise.pnoise(pos.xz, Period);
            }
        }
    }
}