using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static double homeLat = 13.8455;
    public static double homeLon = 100.5688;
    public static double homeAlt = 10;

    void Start()
    {
        Debug.Log($"HOME LOCKED: {homeLat}, {homeLon}");
    }
    
}