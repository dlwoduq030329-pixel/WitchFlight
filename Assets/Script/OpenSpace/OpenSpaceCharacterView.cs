using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// All renderers, rig, equipment, companion and audio source are authored in the prefab.
public sealed class OpenSpaceCharacterView : MonoBehaviour
{
    public PlayerAppearance appearance;
    public PlayerEquipment equipment;
    public Animator animator;
    public OpenSpaceCatView Cat;
    public OpenSpaceSettings settings;
    public GameObject fishingRod;
    public TMPro.TMP_Text emoteLabel;
    public TMPro.TMP_Text nicknameLabel, titleLabel;
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private bool ready;
    public void Apply(PlayerConfig config,int body,int eyes)
    {
        appearance.ApplyPlayerConfig(config);
        equipment.ApplyLoadout((HatType)config.hatIndex,(BroomType)config.broomIndex,MagicType.None,MagicType.None);
        equipment.SetFlightEquipmentVisible(false);equipment.SetWeaponVisible(false);
        Cat.Apply(body,eyes);
        if(!ready && animator!=null && settings.idle!=null && settings.walk!=null)
        {
            animator.runtimeAnimatorController=null;animator.applyRootMotion=false;
            graph=PlayableGraph.Create("OpenSpace locomotion");graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer=AnimationMixerPlayable.Create(graph,2);
            graph.Connect(AnimationClipPlayable.Create(graph,settings.idle),0,mixer,0);
            graph.Connect(AnimationClipPlayable.Create(graph,settings.walk),0,mixer,1);
            AnimationPlayableOutput.Create(graph,"Body",animator).SetSourcePlayable(mixer);graph.Play();
        }
        ready=true;
    }
    public void Tick(float speed,OpenSpaceActivity activity,bool holding,bool stroking,bool motion,bool blocked,int emote)
    {
        if(mixer.IsValid()){float t=Mathf.Clamp01(speed/settings.walkSpeed);mixer.SetInputWeight(0,1-t);mixer.SetInputWeight(1,t);}
        if(fishingRod!=null)fishingRod.SetActive(activity==OpenSpaceActivity.Fishing);
        Cat.Tick(activity,holding,stroking,motion,blocked);
        if(emoteLabel!=null)
        {
            string value=emote==1?"안녕!":emote==2?"신나요!":emote==3?"아쉬워요":emote==4?"으으!":"";
            if(blocked)value="";if(emoteLabel.text!=value)emoteLabel.text=value;
        }
    }
    private void OnDestroy(){if(graph.IsValid())graph.Destroy();}
}
