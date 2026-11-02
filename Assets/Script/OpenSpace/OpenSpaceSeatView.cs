using UnityEngine;

public sealed class OpenSpaceSeatView : MonoBehaviour
{
    public int index;
    public Transform bobber;
    public Renderer marker;
    public TMPro.TMP_Text label;
    private MaterialPropertyBlock block;
    private OpenSpaceFishingPhase shown=(OpenSpaceFishingPhase)(-1);
    private Vector3 home;
    private void Awake(){block=new MaterialPropertyBlock();home=bobber.localPosition;}
    private void Update()
    {
        var hub=OpenSpaceHub.Instance;if(hub==null)return;
        var seat=hub.Seats[index];
        bool floating=seat.phase==OpenSpaceFishingPhase.Waiting||seat.phase==OpenSpaceFishingPhase.Bite;
        if(bobber.gameObject.activeSelf!=floating)bobber.gameObject.SetActive(floating);
        if(floating)bobber.localPosition=home+Vector3.up*(Mathf.Sin(Time.time*(seat.phase==OpenSpaceFishingPhase.Bite?12:2))*.08f);
        if(shown==seat.phase)return;shown=seat.phase;
        marker.GetPropertyBlock(block);block.SetColor("_BaseColor",seat.owner==Fusion.PlayerRef.None?new Color(.25f,.85f,.75f):new Color(1,.7f,.25f));marker.SetPropertyBlock(block);
        label.text=(index+1)+" · "+(seat.owner==Fusion.PlayerRef.None?"빈 낚시 자리":seat.phase==OpenSpaceFishingPhase.Bite?"입질!":"사용 중");
    }
}
