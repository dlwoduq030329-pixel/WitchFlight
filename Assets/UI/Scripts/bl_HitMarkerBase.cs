using System.Collections.Generic;
using UnityEngine;

public abstract class bl_HitMarkerBase : MonoBehaviour
{

    /// <summary>
    /// Do the hit effect
    /// </summary>
    public abstract void OnHit(string markerName = "default", Color tint = default(Color));

    /// <summary>
    /// Static helper to call the hit marker from anywhere without needing a reference. Will do nothing if no hit marker is present in the scene.
    /// </summary>
    /// <param name="markerName">The name of the hit marker to use.</param>
    /// <param name="tint">The color tint to apply to the hit marker.</param>
    public static void Hit(string markerName = "default", Color tint = default(Color))
    {
        if (Instance != null)
        {
            Instance.OnHit(markerName, tint);
        }
    }

    // Singleton
    private static bl_HitMarkerBase _instance;
    public static bl_HitMarkerBase Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<bl_HitMarkerBase>();
            }
            return _instance;
        }
        set
        {
            _instance = value;
        }
    }

}