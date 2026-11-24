using UnityEngine;

// Local presentation of replicated book count; no extra network objects or RPCs.
public sealed class VisionBookOrbit : MonoBehaviour
{
    private Player player;
    private Transform[] books, hinges;
    private GameObject[] closedViews, openViews;
    private Material cover, pages;
    private bool[] wasOpen;
    private float[] openingTime;
    private Quaternion[] openingStart;
    private void Awake() { player = GetComponent<Player>(); }
    private void LateUpdate()
    {
        bool show = player != null && player.Object != null && player.Object.IsValid && player.IsAlive && player.HasVisionEquipped;
        if (!show) { SetVisible(false); return; }
        var s = VisionBookSettings.Default;
        int count = Mathf.Clamp(s.bookCount, 1, 12);
        if (books == null || books.Length != count) Build(s, count);
        SetVisible(true);
        for (int i = 0; i < count; i++)
        {
            // Fixed left/right shoulder positions. Extra books form vertical pairs.
            float side = i % 2 == 0 ? -1f : 1f;
            float bob = Mathf.Sin(Time.time * s.bobSpeed + i * Mathf.PI) * s.bobHeight;
            books[i].localPosition = new Vector3(side * s.shoulderDistance,
                s.shoulderHeight + (i / 2) * s.bookScale * 1.5f + bob, s.shoulderForwardOffset);
            books[i].localScale = Vector3.one * s.bookScale;
            bool open = i < player.VisionOpenBooks;
            if (open && !wasOpen[i])
            {
                openingTime[i] = 0f;
                openingStart[i] = books[i].rotation;
            }
            wasOpen[i] = open;
            if (open) openingTime[i] += Time.deltaTime;
            float progress = Mathf.Clamp01(openingTime[i] / Mathf.Max(.1f, s.openingSeconds));
            float unfolding = open ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress * 2f - 1f)) : 0f;
            if (hinges[i] != null) hinges[i].localRotation = Quaternion.Slerp(hinges[i].localRotation,
                Quaternion.Euler(0, 0, 155f * unfolding), open ? 1f : 1f - Mathf.Exp(-12f * Time.deltaTime));
            else
            {
                bool unfolded = unfolding > .5f;
                if (closedViews[i].activeSelf == unfolded) closedViews[i].SetActive(!unfolded);
                if (openViews[i].activeSelf != unfolded) openViews[i].SetActive(unfolded);
            }
            // Built-in spine runs along Z; the free edge is +X and the top is -Z.
            // Aim the opening (not the page normal) forwards. Counter half the
            // cover angle so both halves open symmetrically around that direction.
            Quaternion openingForward;
            if (hinges[i] != null)
            {
                float opening = Mathf.DeltaAngle(0f, hinges[i].localEulerAngles.z);
                openingForward = Quaternion.Euler(0, -90f, 0) * Quaternion.Euler(90f, 0, 0) *
                    Quaternion.AngleAxis(-opening * .5f, Vector3.forward);
            }
            else openingForward = Quaternion.Euler(s.prefabRotationCorrection);
            Quaternion closedRotation = transform.rotation * Quaternion.Euler(-s.backwardTiltDegrees, 0, 0) * openingForward;
            if (open)
            {
                Vector3 towardsReader = transform.TransformPoint(Vector3.up * s.shoulderHeight) - books[i].position;
                if (towardsReader.sqrMagnitude < .0001f) towardsReader = -transform.forward;
                // The exposed page normals, rather than the free edge, face the reader.
                Quaternion pagesForward = hinges[i] != null ? Quaternion.Euler(90f, 0, 0) *
                    Quaternion.AngleAxis((180f - 155f * unfolding) * .5f, Vector3.forward) :
                    Quaternion.Euler(s.prefabRotationCorrection) * Quaternion.Euler(0, 90f, 0);
                Quaternion reading = Quaternion.LookRotation(towardsReader, Vector3.up) *
                    Quaternion.Euler(-s.backwardTiltDegrees, 0, 0) * pagesForward;
                Quaternion pose = Quaternion.Slerp(openingStart[i], reading, unfolding);
                // Pre-multiply: spin around WORLD Y, independent of pilot banking.
                books[i].rotation = Quaternion.AngleAxis(720f * Mathf.SmoothStep(0f, 1f, progress), Vector3.up) * pose;
            }
            else books[i].rotation = Quaternion.Slerp(books[i].rotation, closedRotation, 1f - Mathf.Exp(-12f * Time.deltaTime));
        }
    }
    private void SetVisible(bool value)
    {
        if (books == null) return;
        foreach (var b in books) if (b != null && b.gameObject.activeSelf != value) b.gameObject.SetActive(value);
    }
    private void Build(VisionBookSettings s, int count)
    {
        if (books != null) foreach (var b in books) if (b != null) Destroy(b.gameObject);
        books = new Transform[count]; hinges = new Transform[count];
        wasOpen = new bool[count]; openingTime = new float[count]; openingStart = new Quaternion[count];
        closedViews = new GameObject[count]; openViews = new GameObject[count];
        if (cover == null)
        {
            cover = new Material(Shader.Find("Universal Render Pipeline/Lit")); cover.color = new Color(.18f, .06f, .36f);
            pages = new Material(cover); pages.color = new Color(.95f, .86f, .63f);
        }
        for (int i = 0; i < count; i++)
        {
            var root = new GameObject("Vision Book " + (i + 1)).transform;
            root.SetParent(transform, false); books[i] = root;
            if (s.closedBookPrefab != null && s.openBookPrefab != null)
            {
                closedViews[i] = Instantiate(s.closedBookPrefab, root);
                openViews[i] = Instantiate(s.openBookPrefab, root);
                foreach (var c in root.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                continue;
            }
            Part(root, new Vector3(.45f, -.1f, 0), new Vector3(.95f, .09f, 1.25f), cover);
            Part(root, new Vector3(.45f, 0, 0), new Vector3(.85f, .16f, 1.15f), pages);
            Part(root, new Vector3(0, 0, 0), new Vector3(.1f, .3f, 1.25f), cover);
            var hinge = new GameObject("Cover hinge").transform; hinge.SetParent(root, false);
            hinge.localPosition = new Vector3(0, .12f, 0); hinges[i] = hinge;
            Part(hinge, new Vector3(.45f, 0, 0), new Vector3(.95f, .08f, 1.25f), cover);
            Part(hinge, new Vector3(.45f, -.05f, 0), new Vector3(.85f, .02f, 1.15f), pages);
        }
    }
    private static void Part(Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.GetComponent<Collider>().enabled = false;
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false); part.transform.localPosition = position; part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
    }
    private void OnDisable() { SetVisible(false); }
    private void OnDestroy() { if (cover != null) Destroy(cover); if (pages != null) Destroy(pages); }
}
