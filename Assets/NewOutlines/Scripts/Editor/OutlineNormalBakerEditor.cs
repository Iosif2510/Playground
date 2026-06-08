using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace NewOutlines.Editor
{
    public class OutlineNormalBakerEditor : AssetPostprocessor
    {
        private const string BAKE_FLAG = "BakeSmoothNormal_UV8";

        // ==========================================================
        // 1. Context Menu: 에셋 우클릭으로 베이크 상태 토글
        // ==========================================================
        [MenuItem("Assets/Toggle Bake Smooth Normal (UV8)")]
        private static void ToggleBakeNormal()
        {
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return;

            bool isBaked = importer.userData.Contains(BAKE_FLAG);
            
            // 플래그 토글
            if (isBaked)
                importer.userData = importer.userData.Replace(BAKE_FLAG, "");
            else
                importer.userData += BAKE_FLAG;

            // 변경사항 저장 및 모델 재임포트 (이때 OnPostprocessMesh가 호출됨)
            importer.SaveAndReimport();
        }

        // 모델 에셋(FBX, OBJ 등)을 선택했을 때만 메뉴 활성화
        [MenuItem("Assets/Toggle Bake Smooth Normal (UV8)", true)]
        private static bool ToggleBakeNormalValidate()
        {
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return AssetImporter.GetAtPath(path) is ModelImporter;
        }

        // ==========================================================
        // 2. AssetPostprocessor: 모델 임포트 시 자동 베이크 개입
        // ==========================================================
        private void OnPostprocessModel(GameObject g)
        {
            // userData에 플래그가 없으면 원본 그대로 패스
            if (!assetImporter.userData.Contains(BAKE_FLAG)) return;

            // 모델 하위에 있는 모든 MeshFilter의 메시를 찾아서 베이크
            foreach (var mf in g.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh != null)
                {
                    BakeSmoothNormalToTangentSpace(mf.sharedMesh);
                }
            }
    
            // SkinnedMeshRenderer (애니메이션이 있는 캐릭터 등)의 메시도 찾아서 베이크
            foreach (var smr in g.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr.sharedMesh != null)
                {
                    BakeSmoothNormalToTangentSpace(smr.sharedMesh);
                }
            }
        }

        private void BakeSmoothNormalToTangentSpace(Mesh mesh)
        {
            // 이전 답변에서 최적화한 ComputeSmoothNormals 및 Tangent Space 변환 로직 삽입
            // 변환 후 mesh.SetUVs(7, bakedData); 적용
            OutlineBaker.BakeSmoothNormalsToUV(mesh, 7);
        }

        // ==========================================================
        // 3. Project Window GUI: 베이크 여부 에셋에 표시
        // ==========================================================
        [InitializeOnLoadMethod]
        private static void DrawBakedLabelInProjectWindow()
        {
            EditorApplication.projectWindowItemOnGUI += (guid, rect) =>
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                
                // 퍼포먼스를 위해 모델 파일 확장자인지 가볍게 선별
                if (path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) || 
                    path.EndsWith(".obj", System.StringComparison.OrdinalIgnoreCase))
                {
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (importer != null && importer.userData.Contains(BAKE_FLAG))
                    {
                        // 우측에 초록색 글씨로 [Baked] 라벨 그리기
                        var labelRect = new Rect(rect.x + rect.width - 45, rect.y, 45, rect.height);
                        
                        var style = new GUIStyle(EditorStyles.miniLabel);
                        style.normal.textColor = new Color(0.2f, 0.8f, 0.2f); // 초록색
                        style.alignment = TextAnchor.MiddleRight;

                        GUI.Label(labelRect, "Baked", style);
                    }
                }
            };
        }
    }
}