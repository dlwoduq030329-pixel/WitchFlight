using UnityEngine;
namespace Suimono.Core {
 public class SuimonoEffectsShowcase : MonoBehaviour {
  public SuimonoModule module;
  public Transform waterfall,boatwake,ripple;
  Vector3 boatOrigin; float elapsed;
  public void Focus(int view){
   var camera=module.setCameraComponent;var target=view==1?waterfall:view==2?boatwake:ripple;
   if(view==0){camera.transform.position=new Vector3(0,12,-16);camera.transform.LookAt(new Vector3(0,0,5));}
   else {camera.transform.position=target.position+new Vector3(0,4,-7);camera.transform.LookAt(target.position);}
  }
  void Start(){boatOrigin=boatwake.position;}
  void Update(){
   elapsed+=Time.deltaTime;
   if(module==null)return;
   var p=boatOrigin+new Vector3(Mathf.Sin(elapsed*.35f)*2,0,Mathf.Cos(elapsed*.35f)*1.5f);
   p.y=module.SuimonoGetHeight(p,"surfaceLevel");boatwake.position=p;
   boatwake.rotation=Quaternion.Euler(0,elapsed*.35f*Mathf.Rad2Deg,0);
   p=ripple.position;p.y=module.SuimonoGetHeight(p,"surfaceLevel");ripple.position=p;
  }
  void OnGUI(){var camera=module==null?null:module.setCameraComponent;if(camera==null)return;
   string[] views={"All effects","Waterfall","Boat wake","Ripple normals"};var button=new GUIStyle(GUI.skin.button){fontSize=24};for(int i=0;i<views.Length;i++)if(GUI.Button(new Rect(15+i*200,15,190,48),views[i],button))Focus(i);
   var style=new GUIStyle(GUI.skin.label){fontSize=28,alignment=TextAnchor.MiddleCenter};style.normal.textColor=Color.white;
   Label(camera,waterfall,"Waterfall",style);Label(camera,boatwake,"Boat wake",style);Label(camera,ripple,"Ripple normals",style);
  }
  void Label(Camera camera,Transform target,string text,GUIStyle style){if(target==null)return;var p=camera.WorldToScreenPoint(target.position+Vector3.up*2);if(p.z>0)GUI.Label(new Rect(p.x-130,Screen.height-p.y-25,260,50),text,style);}
 }
}
