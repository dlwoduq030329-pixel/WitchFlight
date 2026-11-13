using UnityEngine;

/// <summary>Only for the isolated preview scene; analytic ray/sphere hits require no physics colliders.</summary>
public sealed class ReactiveShieldPreview : MonoBehaviour
{
    [SerializeField] private ReactiveShieldVfx shield;
    [SerializeField] private Camera previewCamera;
    [SerializeField] private bool autoFire = true;
    [SerializeField, Min(.08f)] private float fireInterval = .65f;
    [SerializeField, Range(.1f, 2f)] private float hitStrength = 1f;
    private float nextShot;
    private int shot;

    private void Update()
    {
        if (shield == null || previewCamera == null) return;
        if (Input.GetKeyDown(KeyCode.Space)) autoFire = !autoFire;
        if (Input.GetMouseButtonDown(0)) HitRay(previewCamera.ScreenPointToRay(Input.mousePosition));
        if (!autoFire || Time.time < nextShot) return;
        nextShot = Time.time + Mathf.Max(.08f, fireInterval);
        float angle = shot++ * 2.399963f;
        Vector3 local = new Vector3(Mathf.Sin(angle)*.58f,Mathf.Cos(angle)*.58f,-.815f).normalized;
        shield.ReportHit(shield.transform.TransformPoint(local*shield.Radius),hitStrength);
    }

    private void HitRay(Ray ray)
    {
        Vector3 origin = shield.transform.InverseTransformPoint(ray.origin);
        Vector3 direction = shield.transform.InverseTransformVector(ray.direction).normalized;
        float b = Vector3.Dot(origin,direction), c = origin.sqrMagnitude-shield.Radius*shield.Radius;
        float discriminant = b*b-c;
        if(discriminant<0) return;
        float t = -b-Mathf.Sqrt(discriminant);
        if(t<0) t=-b+Mathf.Sqrt(discriminant);
        if(t>=0) shield.ReportHit(shield.transform.TransformPoint(origin+direction*t),hitStrength);
    }
}
