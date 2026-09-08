using System;
using UnityEditor;

[InitializeOnLoad]
internal static class GeneratedObjectSelectionGuard
{
    static GeneratedObjectSelectionGuard()
    {
        AssemblyReloadEvents.beforeAssemblyReload += ClearSelection;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.quitting += ClearSelection;
    }

    private static void OnPlayModeStateChanged(
        PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode ||
            state == PlayModeStateChange.ExitingPlayMode)
        {
            ClearSelection();
        }
    }

    private static void ClearSelection()
    {
        // ArcGIS custom inspectors can receive OnEnable/OnDisable after their
        // runtime HPTransform has already been destroyed during a domain reload.
        Selection.objects = Array.Empty<UnityEngine.Object>();
    }
}
