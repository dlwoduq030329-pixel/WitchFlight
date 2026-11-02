using UnityEngine;

public sealed class BladeRainVisual : MonoBehaviour
{
    private readonly GameObject[] circles = new GameObject[16];
    private Player owner;
    private MagicStatEntry entry;
    private int lastWave;
    public void Initialize(Player player)
    {
        owner=player;
        entry=player.MagicTable!=null?player.MagicTable.GetStats(MagicType.BladeRain):MagicStatTable.DefaultEntry(MagicType.BladeRain);
    }
    public void Refresh()
    {
        if(owner==null || owner.Object==null || !owner.Object.IsValid) { Hide(); return; }
        int count=owner.RainCircleCount;
        for(int i=0;i<circles.Length;i++)
        {
            if(i>=count)
            {
                if(circles[i]!=null) MagicVfxPool.Release(circles[i]);
                circles[i]=null;
                continue;
            }
            if(circles[i]==null)
            {
                circles[i]=MagicVfxPool.Rent(entry.rainCircleVfxPrefab,owner.RainCirclePosition(i),owner.transform.rotation,transform);
                TimeVfxSpeed.Bind(circles[i],owner.Runner,owner.TeamIndex,false);
                BattleAudio.PlayMagic(MagicType.BladeRain,MagicSound.AreaWarning,owner.RainCirclePosition(i));
            }
        }
        if(lastWave!=owner.RainVolleyStep)
        {
            if(count>0 && owner.RainVolleyStep>lastWave) BattleAudio.PlayMagic(MagicType.BladeRain,MagicSound.FollowUp,owner.LockAimPoint);
            lastWave=owner.RainVolleyStep;
        }
    }
    private void LateUpdate()
    {
        if(owner==null || owner.Object==null || !owner.Object.IsValid) return;
        bool visible=!owner.IsHiddenBySmokeFor(Player.LocalPlayer);
        for(int i=0;i<circles.Length;i++)
        {
            if(circles[i]==null) continue;
            circles[i].SetActive(visible);
            circles[i].transform.SetPositionAndRotation(owner.RainCirclePosition(i),owner.transform.rotation);
        }
    }
    public void Hide()
    {
        for(int i=0;i<circles.Length;i++)
        {
            if(circles[i]!=null) MagicVfxPool.Release(circles[i]);
            circles[i]=null;
        }
    }
    private void OnDisable()=>Hide();
    private void OnDestroy()=>Hide();
}
