using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

// Created only for the ManaBooster lesson. Search once, retain route and reuse its visuals.
internal sealed class TrainingManaRoute : IDisposable
{
    private readonly List<Vector3> route = new();
    private readonly List<Collider> zones = new();
    private readonly RaycastHit[] hits = new RaycastHit[48];
    private readonly List<Vector3> nodes = new();
    private readonly List<float> costs = new();
    private readonly List<int> parents = new();
    private readonly HashSet<int> closed = new();
    private readonly HashSet<Collider> expanded = new();
    private Collider target;
    private Camera camera;
    private Canvas canvas;
    private LineRenderer line;
    private TMP_Text label, direction;
    private Material lineMaterial;
    private Vector3 destination, exitPoint, plannedFrom;
    private Bounds flightBounds;
    private bool bounded, mustLeaveFirst;
    private int waypoint;
    private float clearance, nextRouteCheck, nextLabelUpdate;
    private const int NodeLimit = 64;
    private int queriesRemaining;
    public bool MustLeaveFirst => mustLeaveFirst;
    public bool HasRoute => route.Count > 1;
    public bool HasDestination => TargetValid;
    private bool TargetValid => target != null && target.enabled && target.gameObject.activeInHierarchy;

    public bool Begin(Player player, Canvas parentCanvas, TMP_Text style)
    {
        canvas = parentCanvas;
        camera = Camera.main; // Cached once, no per-frame camera/scene search.
        int layer = LayerMask.NameToLayer("ManaBuff");
        if (layer < 0 || camera == null || canvas == null) return false;
        foreach (Collider zone in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            if (zone.enabled && zone.isTrigger && zone.gameObject.activeInHierarchy &&
                zone.gameObject.layer == layer && zone.gameObject.scene == player.gameObject.scene) zones.Add(zone);
        if (zones.Count == 0) return false;
        Vector3 start = player.LockAimPoint;
        zones.Sort((a,b) => Score(a,start).CompareTo(Score(b,start)));
        target = zones[0];
        var controller = player.GetComponent<CharacterController>();
        clearance = controller != null ? Mathf.Max(1f, controller.bounds.extents.x, controller.bounds.extents.z) + .75f : 2f;
        var boundary = BattleManager.Instance != null ? BattleManager.Instance.MapBoundary : null;
        bounded = boundary != null && boundary.TryGetBotFlightBounds(out flightBounds);
        destination = ZoneCenter(target);
        mustLeaveFirst = Contains(target,start);
        exitPoint = target.bounds.center + Vector3.right * (target.bounds.extents.x + clearance + 12f);
        if (bounded) exitPoint = flightBounds.ClosestPoint(exitPoint);
        // The rare all-zones-overlap case still requires leaving before the arrival lesson can pass.
        if (mustLeaveFirst && Contains(target,exitPoint))
            exitPoint = target.bounds.center - Vector3.right * (target.bounds.extents.x + clearance + 12f);

        var lineObject = new GameObject("ManaBooster tutorial route", typeof(LineRenderer));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lineObject, player.gameObject.scene);
        lineObject.layer = 2;
        line = lineObject.GetComponent<LineRenderer>();
        lineMaterial = Resources.Load<Material>("TrainingManaRoute");
        line.sharedMaterial = lineMaterial;
        line.widthMultiplier = .7f;
        line.useWorldSpace = true;
        line.textureMode = LineTextureMode.Tile;
        line.numCornerVertices = 3;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        label = CreateText("ManaBooster destination", canvas.transform, style, 26f);
        label.rectTransform.sizeDelta = new Vector2(300f,80f);
        direction = CreateText("ManaBooster direction", label.transform, style, 35f);
        direction.text = "▲";
        direction.rectTransform.sizeDelta = new Vector2(50f,50f);
        direction.rectTransform.anchoredPosition = new Vector2(0,65f);
        Replan(start);
        return true;
    }

    private static float Score(Collider zone, Vector3 point) =>
        (Contains(zone,point) ? 100000000f : 0f) + (zone.ClosestPoint(point)-point).sqrMagnitude;
    private static Vector3 ZoneCenter(Collider zone) => zone is BoxCollider box
        ? box.transform.TransformPoint(box.center) : zone.ClosestPoint(zone.bounds.center);
    private static bool Contains(Collider zone, Vector3 point) => (zone.ClosestPoint(point)-point).sqrMagnitude < .001f;

    public bool IsInside(Player player) => TargetValid && !mustLeaveFirst &&
        (Contains(target,player.LockAimPoint) || Contains(target,player.transform.position));

    public void SetVisible(bool visible)
    {
        if (line != null) line.enabled = visible && route.Count > 1;
        if (label != null && label.gameObject.activeSelf != visible) label.gameObject.SetActive(visible);
    }

    public void Tick(Player player)
    {
        if (camera == null || label == null) return;
        if (!TargetValid)
        {
            if(Time.unscaledTime<nextRouteCheck){SetVisible(false);return;}
            nextRouteCheck=Time.unscaledTime+1f;
            // Reuse the cached zone inventory if a zone is disabled, without rescanning the scene.
            target = null;
            float best = float.PositiveInfinity;
            foreach (var zone in zones)
                if (zone != null && zone.enabled && zone.gameObject.activeInHierarchy && Score(zone,player.LockAimPoint)<best)
                { target=zone;best=Score(zone,player.LockAimPoint); }
            if (target == null) { SetVisible(false); return; }
            destination=ZoneCenter(target);mustLeaveFirst=false;nextRouteCheck=0;route.Clear();
        }
        SetVisible(true);
        Vector3 position = player.LockAimPoint;
        if (mustLeaveFirst && !Contains(target,position) && !Contains(target,player.transform.position))
        { mustLeaveFirst=false;Replan(position); }
        if (route.Count > 1)
        {
            float reach = Mathf.Max(4f,clearance*2);
            while (waypoint < route.Count-1 && (position-route[waypoint]).sqrMagnitude < reach*reach)
                waypoint++;
            // Only move the first line vertex; static route vertices are uploaded on waypoint changes.
            int count = route.Count-waypoint+1;
            if (line.positionCount != count)
            {
                line.positionCount=count;
                for(int i=waypoint;i<route.Count;i++) line.SetPosition(i-waypoint+1,route[i]);
            }
            line.SetPosition(0,position);
        }
        if (Time.unscaledTime >= nextRouteCheck)
        {
            nextRouteCheck=Time.unscaledTime+1f;
            Vector3 goal=mustLeaveFirst?exitPoint:ZoneCenter(target);
            bool moved=(goal-destination).sqrMagnitude>4f && !mustLeaveFirst;
            bool offRoute=route.Count<2 || SegmentDistance(position,route[Mathf.Max(0,waypoint-1)],route[waypoint])>15f;
            if ((moved || offRoute) && (position-plannedFrom).sqrMagnitude>16f)
            { destination=ZoneCenter(target);Replan(position); }
        }
        Vector3 next = route.Count > 1 ? route[waypoint] : (mustLeaveFirst?exitPoint:destination);
        Vector3 view = camera.WorldToViewportPoint(next);
        Vector2 offset = new Vector2(view.x-.5f,view.y-.5f);
        if(view.z<=0) offset=-offset;
        if(offset.sqrMagnitude<.0001f && view.z<=0) offset=Vector2.down;
        bool outside=view.z<=0 || view.x<.1f || view.x>.9f || view.y<.2f || view.y>.8f;
        if(outside)
        {
            float scale=Mathf.Max(Mathf.Abs(offset.x)/.4f,Mathf.Abs(offset.y)/.3f);
            offset/=Mathf.Max(scale,.001f);
        }
        var canvasRect=(RectTransform)canvas.transform;
        label.rectTransform.anchoredPosition=Vector2.Scale(offset,canvasRect.rect.size);
        direction.rectTransform.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(offset.y,offset.x)*Mathf.Rad2Deg-90);
        if (Time.unscaledTime >= nextLabelUpdate)
        {
            nextLabelUpdate=Time.unscaledTime+.25f;
            label.SetText(mustLeaveFirst ? "영역 밖으로 이동\n{0:0}m" : waypoint<route.Count-1 ? "ManaBooster 경유 지점\n{0:0}m" : "ManaBooster\n{0:0}m",
                Vector3.Distance(position,next));
        }
    }

    private void Replan(Vector3 start)
    {
        plannedFrom=start;nextRouteCheck=Time.unscaledTime+1f;
        Vector3 goal=mustLeaveFirst?exitPoint:destination;
        route.Clear();nodes.Clear();costs.Clear();parents.Clear();closed.Clear();expanded.Clear();
        queriesRemaining=160;
        AddNode(start);costs[0]=0;
        int end=-1;
        for(int iteration=0;iteration<NodeLimit && queriesRemaining>0;iteration++)
        {
            int current=-1;float score=float.PositiveInfinity;
            for(int i=0;i<nodes.Count;i++)
                if(!closed.Contains(i) && costs[i]+Vector3.Distance(nodes[i],goal)<score)
                { current=i;score=costs[i]+Vector3.Distance(nodes[i],goal); }
            if(current<0) break;
            closed.Add(current);
            if(Clear(nodes[current],goal,out var blocker)){end=current;break;}
            if(blocker!=null && expanded.Add(blocker))
            {
                Bounds box=blocker.bounds;box.Expand((clearance+3f)*2);
                for(int corner=0;corner<8;corner++)
                    AddNode(box.center+Vector3.Scale(box.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1)));
            }
            for(int i=1;i<nodes.Count;i++)
            {
                if(closed.Contains(i))continue;
                float cost=costs[current]+Vector3.Distance(nodes[current],nodes[i]);
                if(cost>=costs[i] || !Clear(nodes[current],nodes[i],out _))continue;
                costs[i]=cost;parents[i]=current;
            }
        }
        if(end>=0)
        {
            route.Add(goal);
            for(int i=end;i>=0;i=parents[i])route.Add(nodes[i]);
            route.Reverse();
        }
        waypoint=1;
        line.positionCount=route.Count;
        for(int i=0;i<route.Count;i++)line.SetPosition(i,route[i]);
        line.enabled=route.Count>1;
    }

    private void AddNode(Vector3 point)
    {
        if(nodes.Count>=NodeLimit || (nodes.Count>0 && bounded && !flightBounds.Contains(point))) return;
        for(int i=0;i<nodes.Count;i++)if((nodes[i]-point).sqrMagnitude<1f)return;
        nodes.Add(point);costs.Add(float.PositiveInfinity);parents.Add(-1);
    }
    private bool Clear(Vector3 start,Vector3 end,out Collider blocker)
    {
        blocker=null;Vector3 delta=end-start;float length=delta.magnitude;
        if(length<.01f)return true;
        if(queriesRemaining--<=0)return false;
        int mask=~((1<<2)|(1<<4)|(1<<5)|(1<<6)|(1<<9)|(1<<10)|(1<<11)|(1<<12)|(1<<31));
        int count=Physics.SphereCastNonAlloc(start,clearance,delta/length,hits,length,mask,QueryTriggerInteraction.Ignore);
        float nearest=float.PositiveInfinity;
        for(int i=0;i<count;i++)
            if(hits[i].collider.GetComponentInParent<Player>()==null && hits[i].distance<nearest)
            { nearest=hits[i].distance;blocker=hits[i].collider; }
        return blocker==null && count<hits.Length;
    }
    internal static float SegmentDistance(Vector3 point,Vector3 start,Vector3 end)
    {
        Vector3 delta=end-start;
        return Vector3.Distance(point,start+delta*Mathf.Clamp01(Vector3.Dot(point-start,delta)/Mathf.Max(.001f,delta.sqrMagnitude)));
    }
    private static TMP_Text CreateText(string name,Transform parent,TMP_Text style,float size)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);
        var text=go.GetComponent<TextMeshProUGUI>();text.font=style.font;text.fontSize=size;text.color=new Color(.25f,.95f,1f);
        text.alignment=TextAlignmentOptions.Center;text.raycastTarget=false;
        text.rectTransform.anchorMin=text.rectTransform.anchorMax=new Vector2(.5f,.5f);
        return text;
    }
    public void Dispose()
    {
        if(line!=null)UnityEngine.Object.Destroy(line.gameObject);
        if(label!=null)UnityEngine.Object.Destroy(label.gameObject);
        line=null;label=null;target=null;zones.Clear();route.Clear();
    }
}
