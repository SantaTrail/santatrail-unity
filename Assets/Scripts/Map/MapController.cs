using UnityEngine;

public class MapController : MonoBehaviour
{
    public double topLeftLat = 13.846778;
    public double topLeftLon = 100.567024;

    public double bottomRightLat = 13.844237;
    public double bottomRightLon = 100.570725;

    public double homeLat = 13.8455075;
    public double homeLon = 100.5688745;

    void Start()
    {
        AlignMap();
    }

    void AlignMap()
    {
        Vector3 topLeft = GPSToUnity(topLeftLat, topLeftLon);
        Vector3 bottomRight = GPSToUnity(bottomRightLat, bottomRightLon);

        float width = Mathf.Abs(bottomRight.x - topLeft.x);
        float height = Mathf.Abs(topLeft.z - bottomRight.z);

        transform.localScale = new Vector3(width / 10f, 1, height / 10f);

        Vector3 center = (topLeft + bottomRight) / 2f;

        transform.position = new Vector3(center.x, -0.1f, center.z);

        Debug.Log($"Map width: {width} m, height: {height} m");
        Debug.Log($"Center offset: {center}");
    }

    Vector3 GPSToUnity(double lat, double lon)
    {
        double R = 6378137;

        double dLat = (lat - homeLat) * Mathf.Deg2Rad;
        double dLon = (lon - homeLon) * Mathf.Deg2Rad;

        double x = dLon * R * System.Math.Cos(homeLat * Mathf.Deg2Rad);
        double z = dLat * R;

        return new Vector3((float)x, 0, (float)z);
    }
}