using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Keeps Windows builds on the renderer used by the tested SantaTrail launcher.
/// This also protects users who open DronSim.exe directly instead of using the
/// PowerShell launcher, which already supplies -force-d3d11.
/// </summary>
[InitializeOnLoad]
public sealed class WindowsBuildCompatibility : IPreprocessBuildWithReport
{
    static WindowsBuildCompatibility()
    {
        EditorApplication.delayCall += EnsureDirect3D11Setting;
    }

    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneWindows &&
            report.summary.platform != BuildTarget.StandaloneWindows64)
        {
            return;
        }

        ApplyDirect3D11Setting();
    }

    [MenuItem("SantaTrail/Windows/Use Direct3D 11 for builds")]
    public static void ApplyDirect3D11Setting()
    {
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(
            BuildTarget.StandaloneWindows64,
            new[] { GraphicsDeviceType.Direct3D11 }
        );

        Debug.Log(
            "SantaTrail Windows compatibility: Direct3D 11 is now the only " +
            "graphics API for Windows standalone builds."
        );
    }

    private static void EnsureDirect3D11Setting()
    {
        GraphicsDeviceType[] graphicsApis =
            PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);

        bool alreadyConfigured =
            !PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64) &&
            graphicsApis != null &&
            graphicsApis.Length == 1 &&
            graphicsApis[0] == GraphicsDeviceType.Direct3D11;

        if (!alreadyConfigured)
        {
            ApplyDirect3D11Setting();
        }
    }
}
