using UnityEngine.SceneManagement;
using Esri.ArcGISMapsSDK.Utils.GeoCoord;
using Esri.GameEngine.Geometry;

/// <summary>
/// Keeps GPS/MSL heights in LV2 explicit so ArcGIS can convert them to the
/// same vertical datum used by the terrain. Other levels keep their existing
/// WGS84-without-VCS behavior.
/// </summary>
public static class ArcGISAltitudeReference
{
    const string HillsideLevelName = "LV2";

    public static bool UsesEgm96ForActiveLevel =>
        SceneManager.GetActiveScene().name == HillsideLevelName;

    public static ArcGISSpatialReference ForActiveLevel()
    {
        return UsesEgm96ForActiveLevel
            ? new ArcGISSpatialReference(
                SpatialReferenceWkid.WGS84,
                SpatialReferenceVerticalWkid.EGM1996
            )
            : ArcGISSpatialReference.WGS84();
    }
}
