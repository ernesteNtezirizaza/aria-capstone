using UnityEditor;
using UnityEngine;

/// Converts the imported "More Realistic Trees Free!" glTF (2 tree variants
/// bundled in one scene, Assets/Models/RealTrees/scene.gltf) into two
/// standalone Resources prefabs, the same technique already used for the
/// helicopter hazard model: instantiate the whole glTF, re-parent each named
/// tree node onto a fresh root with worldPositionStays=true (baking in every
/// inherited transform from the glTF's own node chain), then save that as a
/// real prefab asset.
public static class RealTreePrefabBuilder
{
    private const string SourcePath = "Assets/Models/RealTrees/scene.gltf";
    private static readonly string[] TreeNodeNames = { "Tree_0", "Tree.001_1" };
    private static readonly string[] OutputNames   = { "RealTree0", "RealTree1" };

    public static void BuildPrefabs()
    {
        var source = AssetDatabase.LoadMainAssetAtPath(SourcePath) as GameObject;
        if (source == null)
        {
            Debug.LogError($"[RealTreePrefabBuilder] Could not load a GameObject from '{SourcePath}'.");
            EditorApplication.Exit(1);
            return;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        if (instance == null) instance = Object.Instantiate(source);

        // InstantiatePrefab keeps a live prefab-instance connection, which
        // restricts structural changes like detaching a child onto a new
        // root -- SetParent silently no-ops on the connected instance
        // instead of erroring, which is why the first attempt at this
        // produced prefabs with zero children. Unpacking fully severs that
        // connection so normal reparenting actually takes effect.
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        bool anyFailed = false;
        for (int i = 0; i < TreeNodeNames.Length; i++)
        {
            var treeNode = FindChildRecursive(instance.transform, TreeNodeNames[i]);
            if (treeNode == null)
            {
                Debug.LogError($"[RealTreePrefabBuilder] Could not find node '{TreeNodeNames[i]}' in the imported scene.");
                anyFailed = true;
                continue;
            }

            var root = new GameObject(OutputNames[i]);
            treeNode.SetParent(root.transform, worldPositionStays: true);

            foreach (var col in root.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(col);

            string outPath = $"Assets/Resources/{OutputNames[i]}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, outPath, out bool success);
            Object.DestroyImmediate(root);

            if (!success)
            {
                Debug.LogError($"[RealTreePrefabBuilder] SaveAsPrefabAsset failed for '{outPath}'.");
                anyFailed = true;
                continue;
            }
            Debug.Log($"[RealTreePrefabBuilder] Saved '{outPath}'.");
        }

        Object.DestroyImmediate(instance);
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(anyFailed ? 1 : 0);
    }

    private static Transform FindChildRecursive(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            var found = FindChildRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
