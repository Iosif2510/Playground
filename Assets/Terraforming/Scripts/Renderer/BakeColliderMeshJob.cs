using Unity.Jobs;
using UnityEngine;

namespace Terraforming
{
    public struct BakeColliderMeshJob : IJob
    {
        public EntityId MeshId;
        public bool Convex;

        public void Execute()
        {
            Physics.BakeMesh(MeshId, Convex);
        }
    }
}