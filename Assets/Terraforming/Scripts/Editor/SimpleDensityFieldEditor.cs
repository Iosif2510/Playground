using UnityEditor;
using UnityEngine;
using Terraforming;

namespace Terraforming.Editor
{
    [CustomEditor(typeof(SimpleDensityField))]
    public class SimpleDensityFieldEditor : UnityEditor.Editor
    {
        private UnityEditor.Editor cachedGeneratorEditor;
        private bool isGeneratorExpanded = true;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Draw default properties excluding "fieldGenerator" to handle it manually,
            // or just draw default and handle the inline editor below.
            DrawPropertiesExcluding(serializedObject, "fieldGenerator");

            // Handle fieldGenerator
            SerializedProperty generatorProp = serializedObject.FindProperty("fieldGenerator");
            
            EditorGUILayout.PropertyField(generatorProp);

            if (generatorProp.objectReferenceValue != null)
            {
                isGeneratorExpanded = EditorGUILayout.Foldout(isGeneratorExpanded, "Generator Settings", true, EditorStyles.foldoutHeader);
                if (isGeneratorExpanded)
                {
                    EditorGUI.indentLevel++;
                    CreateCachedEditor(generatorProp.objectReferenceValue, null, ref cachedGeneratorEditor);
                    
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    cachedGeneratorEditor.OnInspectorGUI();
                    EditorGUILayout.EndVertical();
                    EditorGUI.indentLevel--;
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
