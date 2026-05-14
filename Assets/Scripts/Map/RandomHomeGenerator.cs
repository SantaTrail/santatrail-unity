using UnityEngine;

public class RandomHomeGenerator : MonoBehaviour
{
    public double minLat = 13.5;
    public double maxLat = 14.0;
    public double minLon = 100.3;
    public double maxLon = 100.8;

    public double homeLat;
    public double homeLon;
    public double homeAlt = 10;

    void Start()
    {
        homeLat = Random.Range((float)minLat, (float)maxLat);
        homeLon = Random.Range((float)minLon, (float)maxLon);

        Debug.Log($"HOME: {homeLat}, {homeLon}");
    }
}