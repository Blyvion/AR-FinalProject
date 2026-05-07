using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Editor utility: creates a StarSkybox material and assigns it to the scene's
/// RenderSettings.  Run via  Tools > Apply Star Skybox.
/// </summary>
public static class StarSkyboxSetup
{
    private const string ShaderPath   = "Assets/Materials/StarSkybox.shader";
    private const string MaterialPath = "Assets/Materials/StarSkyboxMat.mat";

    [MenuItem("Tools/Apply Star Skybox")]
    public static void Apply()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null)
        {
            Debug.LogError(
                $"[StarSkybox] Shader not found at '{ShaderPath}'. " +
                "Make sure StarSkybox.shader has been imported.");
            return;
        }

        // Re-use existing material, or create a new one.
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            mat      = new Material(shader);
            mat.name = "StarSkyboxMat";
            AssetDatabase.CreateAsset(mat, MaterialPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        RenderSettings.skybox = mat;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log(
            "[StarSkybox] Applied to scene skybox. " +
            "Save the scene (Ctrl+S) to persist the change. " +
            "Adjust parameters in Assets/Materials/StarSkyboxMat.mat.");
    }
}
