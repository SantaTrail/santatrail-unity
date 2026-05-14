#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SITLManager
{
    // Legacy class intentionally disabled to avoid duplicate process launches.
    static SITLManager()
    {
        Debug.Log("SITLManager: disabled (AutoArduPilotOnPlay is the active launcher).");
    }
}
#endif
