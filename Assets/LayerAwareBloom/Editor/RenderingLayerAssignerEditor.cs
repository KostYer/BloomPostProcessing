using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LayerAwareBloom.Editor
{
    [CustomEditor(typeof(RenderingLayerAssigner))]
    public sealed class RenderingLayerAssignerEditor : UnityEditor.Editor
    {
        private string result;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox(
                "Apply replaces the Rendering Layer Mask on all renderers in this hierarchy, including " +
                "the root and inactive children. Supports particle, VFX Graph and mesh renderers. " +
                "Nothing is applied automatically; press Apply again after adding children.", MessageType.Info);

            if (GUILayout.Button("Apply to Root and Children"))
            {
                var assigner = (RenderingLayerAssigner)target;
                uint mask = assigner.renderingLayers;
                var renderers = assigner.GetComponentsInChildren<Renderer>(true);
                var changed = new List<Renderer>();
                foreach (var renderer in renderers)
                    if (renderer.renderingLayerMask != mask)
                        changed.Add(renderer);

                if (changed.Count > 0)
                {
                    const string action = "Assign Hierarchy Rendering Layers";
                    Undo.IncrementCurrentGroup();
                    int undoGroup = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName(action);
                    Undo.RecordObjects(changed.ToArray(), action);
                    foreach (var renderer in changed)
                    {
                        renderer.renderingLayerMask = mask;
                        if (PrefabUtility.IsPartOfPrefabInstance(renderer))
                            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                        EditorUtility.SetDirty(renderer);
                    }
                    Undo.CollapseUndoOperations(undoGroup);
                    SceneView.RepaintAll();
                    EditorApplication.QueuePlayerLoopUpdate();
                }

                result = renderers.Length == 0
                    ? "No renderers found in this hierarchy."
                    : $"Updated {changed.Count} of {renderers.Length} renderers; the rest already matched.";
            }

            if (!string.IsNullOrEmpty(result))
                EditorGUILayout.HelpBox(result, MessageType.Info);
        }
    }
}
