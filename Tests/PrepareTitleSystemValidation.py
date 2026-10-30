"""Prepare production title/UI code in the existing isolated Unity project, never Main."""
from pathlib import Path
import re
root=Path(__file__).resolve().parent.parent
editor=root/'.utmp/battle-sky-unity/Assets/Editor/TitleValidation'
out=root/'.utmp/battle-sky-unity/Assets/TitleValidation'
out.mkdir(parents=True,exist_ok=True)
editor.mkdir(parents=True,exist_ok=True)
for old in ['TitleCatalog','TitleProgression','AccountProfileUI','AccountTitleUI','Doubles']:
    for suffix in ['.cs','.cs.meta']:
        (editor/(old+suffix)).unlink(missing_ok=True)
for name in ['TitleCatalog','TitleProgression','AccountProfileUI','AccountTitleUI']:
    source=(root/'Assets/Script'/f'{name}.cs').read_text(encoding='utf-8-sig')
    usings='\n'.join(re.findall(r'^using .*;',source,re.M))
    body=re.sub(r'^using .*;\s*','',source,flags=re.M)
    (out/f'{name}.cs').write_text(usings+'\nnamespace TitleValidationRuntime {\n'+body+'\n}',encoding='utf-8')
(editor/'TitleSystemValidation.cs').write_bytes((root/'Tests/TitleSystemValidation.cs').read_bytes())
(out/'Doubles.cs').write_text('''
using System;using UnityEngine;
namespace DG.Tweening {
 public class DOTweenAnimation:MonoBehaviour {public bool isActive,isValid;public Tween tween;public void CreateTween(bool a,bool b){}}
 public class Tween {public bool IsActive()=>false;public Tween SetUpdate(bool x)=>this;public Tween SetAutoKill(bool x)=>this;public void Restart(){}}
}
namespace TitleValidationRuntime {
 public enum MagicType {None,Fire,Ice,Vision,Thunder,Healing,Binding,Dark,Curse,Mine,Razier,Flare,Smoke}
 public class DatabaseManager {public static DatabaseManager Instance=new();public bool HasLoadedProfile=true,IsDataConfigReady=true;}
 public class LoginManager {public static LoginManager Instance;public void LogoutAndReloadScene(){}}
 public static class DataConfig {
  public static event Action Changed;public static int equippedTitleId,playerprofile;public static string titleProgressJson="{}",playerName;
  public static IDisposable BeginChangeBatch()=>new Batch();class Batch:IDisposable {public void Dispose()=>Changed?.Invoke();}
 }
 public class ProfileImageTable:ScriptableObject {
  private static ProfileImageTable table;public static ProfileImageTable Default=>table!=null?table:table=CreateInstance<ProfileImageTable>();
  public Sprite GetSprite(int id)=>Sprite.Create(Texture2D.whiteTexture,new Rect(0,0,1,1),Vector2.one*.5f);
  public bool TryGetSprite(int id,out Sprite s){s=GetSprite(id);return true;}
 }
 public class NetObject {public bool IsValid=true,HasInputAuthority=true;}
 public class PlayerData {public static PlayerData Local;public NetObject Object=new();public bool IsMatchBot,TitleMatchCompleted;public string TitleMatchId="test";public int playerprofile;
  public TitleMatchSnapshot snapshot=new(){id="test"};public void SetPlayerProfile(int id){playerprofile=id;} public void SetEquippedTitle(int id){} public TitleMatchSnapshot GetTitleSnapshot()=>snapshot;}
}
''',encoding='utf-8')
print(out)
