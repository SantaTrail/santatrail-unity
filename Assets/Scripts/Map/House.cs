using UnityEngine;

public class House : MonoBehaviour
{
    public Transform model;

    public bool delivered;
    public string styleName;
    public int styleIndex = -1;
    public string deliveryGroupName;

    public Bounds GetBounds()
    {
        Renderer[] renderers =
            model.GetComponentsInChildren<Renderer>();

        Bounds bounds = renderers[0].bounds;

        foreach (Renderer r in renderers)
        {
            bounds.Encapsulate(r.bounds);
        }

        return bounds;
    }
}
