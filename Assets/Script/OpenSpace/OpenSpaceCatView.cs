using UnityEngine;

public sealed class OpenSpaceCatView : MonoBehaviour
{
    public Animator animator;
    public WitchCatAppearance appearance;
    public Transform plane,head;
    public Renderer[] bodyRenderers;
    public SkinnedMeshRenderer eyes;
    public int closedEyes = -1;
    public AudioSource purr;
    public OpenSpaceSettings settings;
    public CatCompanionTable animations;
    private Vector3 home;
    private Quaternion originalRotation;
    private bool stroking,wasMotion;
    private string state;
    private int motion=-1;
    private float started;
    private Quaternion headPose;
    private bool headAdjusted;
    public void Apply(int body,int eye)
    {
        home=transform.localPosition;originalRotation=transform.localRotation;
        appearance.ApplyAccountIndices(body,eye);
        purr.outputAudioMixerGroup=GameAudioSettings.Group(GameAudioCategory.SFX);
        purr.clip=settings.purr;purr.loop=true;purr.volume=0;purr.spatialBlend=1;purr.minDistance=1;purr.maxDistance=9;
        Play(CatAnimation.SittingIdle);
    }
    private void Update(){if(headAdjusted&&head!=null){head.localRotation=headPose;headAdjusted=false;}}
    private void Play(CatAnimation animation)
    {
        var entry=animations.GetAnimation(animation);
        if(entry==null || state==entry.stateName)return;
        state=entry.stateName;animator.CrossFadeInFixedTime(state,.15f);
    }
    public void PlayMotion(int index)
    {
        if(index<0 || index>=settings.motions.Length)return;
        motion=index;started=Time.time;Play(settings.motions[index].animation);
    }
    public void Tick(OpenSpaceActivity activity,bool holding,bool stroke,bool runningMotion,bool blocked)
    {
        stroking=stroke;
        if(runningMotion)
        {
            plane.gameObject.SetActive(false);
            transform.localPosition=new Vector3(home.x,0,home.z);
            if(motion==3)transform.localRotation=originalRotation*Quaternion.Euler(0,(Time.time-started)*150,0);
        }
        else
        {
            if(wasMotion){motion=-1;state=null;}
            plane.gameObject.SetActive(true);transform.localRotation=originalRotation;
            transform.localPosition=home+Vector3.up*(holding?0:Mathf.Sin(Time.time*1.7f)*.045f);
            Play(stroke?CatAnimation.Knead:CatAnimation.SittingIdle);
        }
        wasMotion=runningMotion;
        float target=stroke&&!blocked?.28f:0;
        purr.volume=Mathf.MoveTowards(purr.volume,target,Time.deltaTime*.65f);
        if(purr.volume>0 && !purr.isPlaying && purr.clip!=null)purr.Play();
        if(purr.volume<=0 && purr.isPlaying)purr.Stop();
    }
    private void LateUpdate()
    {
        if(eyes!=null&&closedEyes>=0)eyes.SetBlendShapeWeight(closedEyes,stroking||motion==2?100:0);
        if(motion==4&&head!=null){headPose=head.localRotation;head.localRotation*=Quaternion.Euler(0,0,Mathf.Sin((Time.time-started)*2)*18);headAdjusted=true;}
    }
    public bool Intersects(Ray ray)
    {foreach(var r in bodyRenderers)if(r!=null&&r.enabled&&r.bounds.IntersectRay(ray))return true;return false;}
}
