[System.Serializable]
public class Features
{
    
    public float 
    
    elevation_range;
    public float building_density;
    public bool water_present;
    public float vegetation_density;
}

[System.Serializable]
public class GPSWaypoint
{
    public float lat;
    public float lon;
    public float alt;
}
[System.Serializable]
public class GPSPoint
{
    public float lat;
    public float lon;
}

[System.Serializable]
public class Building
{
    public GPSPoint[] points;
}

[System.Serializable]
public class Result
{
    public string scene;
    public Features features;
    public GPSWaypoint[] waypoints;
    public GPSWaypoint[][] rivers;
    public float[] elevation_data;
    public int grid_size;

    public Building[] buildings;
}