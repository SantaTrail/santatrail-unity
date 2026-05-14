using UnityEngine;
using Esri.ArcGISMapsSDK.Components;
using Esri.GameEngine.Geometry;

public class GPSConverter : MonoBehaviour
{
    private ArcGISMapComponent arcGISMap;

    void Awake()
    {
        arcGISMap = Object.FindFirstObjectByType<ArcGISMapComponent>();

        if (arcGISMap == null)
        {
            Debug.LogError("❌ ArcGISMapComponent NOT found in scene");
        }
    }

    public Vector3 ConvertGPSToUnity(double lat, double lon, double alt)
    {
        if (arcGISMap == null || arcGISMap.View == null)
        {
            Debug.LogError("❌ ArcGISMap not ready");
            return Vector3.zero;
        }

        ArcGISPoint point = new ArcGISPoint(
            lon,
            lat,
            alt,
            ArcGISSpatialReference.WGS84()
        );

        var worldPos = arcGISMap.View.GeographicToWorld(point);

        return new Vector3(
            (float)worldPos.x,
            (float)worldPos.y,
            (float)worldPos.z
        );
    }
}
