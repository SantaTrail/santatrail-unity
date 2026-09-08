using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(PauseMenuController))]
public sealed class PauseMenuControllerEditor : Editor
{
    private const string PrefabPath = "Assets/Resources/SantaTrailPauseMenu.prefab";

    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(
            "Edit the prefab outside Play Mode to save your changes. Button actions are connected " +
            "automatically. Edit child Image, TextMeshPro, Rect Transform, and Layout components " +
            "to change colors, labels, fonts, sizes, and spacing.", MessageType.Info);
        DrawDefaultInspector();

        var controller = (PauseMenuController)target;
        if (!controller.HasConfiguredUI)
            EditorGUILayout.HelpBox("Assign the required UI references, or use GameObject > UI > SantaTrail Pause Menu to add the configured prefab.", MessageType.Warning);

        EditorGUILayout.Space();
        if (GUILayout.Button("Open Default Pause Menu Prefab"))
            PrefabStageUtility.OpenPrefab(PrefabPath);

        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            GameObject popup = serializedObject.FindProperty("popup").objectReferenceValue as GameObject;
            if (popup != null && !EditorUtility.IsPersistent(popup) &&
                GUILayout.Button(popup.activeSelf ? "Hide Popup Preview" : "Show Popup Preview"))
            {
                Undo.RecordObject(popup, "Toggle Pause Menu Preview");
                popup.SetActive(!popup.activeSelf);
                PrefabUtility.RecordPrefabInstancePropertyModifications(popup);
                EditorUtility.SetDirty(popup);
            }
        }

        RectTransform panel = serializedObject.FindProperty("menuPanel").objectReferenceValue as RectTransform;
        if (panel != null && GUILayout.Button("Select Panel to Edit Layout and Color"))
            Selection.activeGameObject = panel.gameObject;
    }

    [MenuItem("GameObject/UI/SantaTrail Pause Menu", false, 2100)]
    private static void CreateInScene()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError("Pause menu prefab was not found at " + PrefabPath);
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(instance, "Create SantaTrail Pause Menu");
        Selection.activeGameObject = instance;
    }
}
