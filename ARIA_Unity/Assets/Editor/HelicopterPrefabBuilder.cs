using UnityEditor;
using UnityEngine;

public static class HelicopterPrefabBuilder
{
    private const string SourcePath = "Assets/Models/scene.gltf";
    private const string OutputPath = "Assets/Resources/HelicopterHazard.prefab";

    public static void BuildPrefab()
    {
        var source = AssetDatabase.LoadMainAssetAtPath(SourcePath) as GameObject;
        if (source == null)
        {
            Debug.LogError($"[HelicopterPrefabBuilder] Could not load a GameObject from '{SourcePath}' " +
                "-- glTFast may not have finished importing it yet.");
            EditorApplication.Exit(1);
            return;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        if (instance == null)
        {
            // Fallback for the (unexpected) case where the glTF asset isn't itself
            // recognised as a prefab-like asset by InstantiatePrefab.
            instance = Object.Instantiate(source);
        }
        instance.name = "HelicopterHazard";

        var anim = instance.GetComponentInChildren<Animation>();
        var animator = instance.GetComponentInChildren<Animator>();
        Debug.Log($"[HelicopterPrefabBuilder] Source has Animation={(anim != null)}, Animator={(animator != null)}.");

        PrefabUtility.SaveAsPrefabAsset(instance, OutputPath, out bool success);
        Object.DestroyImmediate(instance);

        if (!success)
        {
            Debug.LogError($"[HelicopterPrefabBuilder] SaveAsPrefabAsset failed for '{OutputPath}'.");
            EditorApplication.Exit(1);
            return;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[HelicopterPrefabBuilder] Saved '{OutputPath}' successfully.");
        EditorApplication.Exit(0);
    }
}
