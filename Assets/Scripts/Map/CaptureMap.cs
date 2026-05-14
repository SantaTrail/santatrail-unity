using UnityEngine;
using System.IO;

public class CaptureMap : MonoBehaviour
{
    public Camera mapCamera;
    public RenderTexture rt;

    void Start()
    {
        RenderTexture currentRT = RenderTexture.active;
        RenderTexture.active = rt;

        mapCamera.Render();

        Texture2D image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        image.Apply();

        byte[] bytes = image.EncodeToPNG();
        File.WriteAllBytes(Application.dataPath + "/map.png", bytes);

        RenderTexture.active = currentRT;

        Debug.Log("Map saved!");
    }
}