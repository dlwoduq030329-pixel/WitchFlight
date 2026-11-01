using System;
using System.IO;
using System.Linq;
using Fusion;
using Fusion.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

// Explicit editor authoring only. Runtime uses saved scene/prefab references.
public static class OpenSpaceSetup
{
    const string Folder="Assets/OpenSpace";
    const string AvatarPath="Assets/Ch/ChprefabOpenSpace.prefab";
    static TMP_FontAsset font;
    static OpenSpaceSettings settings;
    static Material stone,grass,wood,teal,white,water;
    [MenuItem("Tools/WitchFlight/OpenSpace/Create initial assigned scene")]
    public static void Create()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Play 모드를 종료해주세요.");
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("열려 있는 씬을 저장한 뒤 실행해주세요.");
        if(File.Exists(Folder+"/AuthoringComplete.txt"))throw new InvalidOperationException("이미 생성된 광장이 있습니다. 기존 씬과 프리팹에서 조정해주세요.");
        Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
        font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/UI/Font/Pretendard-SemiBold SDF.asset");
        settings=ScriptableObject.CreateInstance<OpenSpaceSettings>();
        settings.idle=BattleResultsTable.Default.standingClip;
        settings.walk=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/Art/BroomAnimation/Ground/Skill1/Walk/AS_Skill1_Walk_Forward.anim");
        var audioTable=AssetDatabase.LoadAssetAtPath<BattleAudioTable>("Assets/Resources/BattleAudioTable.asset");
        settings.purr=audioTable.catPetPurr.ChooseClip();
        settings.bite=audioTable.catVictory.ChooseClip();
        AssetDatabase.CreateAsset(settings,Folder+"/OpenSpaceSettings.asset");
        stone=Material("Warm stone",new Color(.85f,.83f,.73f));grass=Material("Grass",new Color(.43f,.66f,.43f));
        wood=Material("Wood",new Color(.48f,.3f,.21f));teal=Material("Teal",new Color(.22f,.68f,.65f));
        white=Material("Clouds",new Color(.92f,.96f,1));water=Material("Emerald water",new Color(.12f,.65f,.7f));
        CreateRunner();CreateAvatar();BuildScene();BindMain();
        var scenes=EditorBuildSettings.scenes.ToList();
        if(!scenes.Any(s=>s.path==OpenSpaceSession.ScenePath))scenes.Add(new EditorBuildSettingsScene(OpenSpaceSession.ScenePath,true));
        EditorBuildSettings.scenes=scenes.ToArray();
        EditorUtility.SetDirty(settings);AssetDatabase.SaveAssets();
        File.WriteAllText(Folder+"/AuthoringComplete.txt","OpenSpace first playable scene. Edit assigned scene/prefab fields; this builder does not run during gameplay.\nOriginal Assets/Ch/ChPrefab.prefab is untouched.\n");
        AssetDatabase.Refresh();Debug.Log("OPENSPACE AUTHORING COMPLETE");
    }
    private static Material Material(string name,Color color)
    {var m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.15f);AssetDatabase.CreateAsset(m,Folder+"/"+name+".mat");return m;}
    private static void Set(Object target,string name,Object value)
    {var s=new SerializedObject(target);s.FindProperty(name).objectReferenceValue=value;s.ApplyModifiedPropertiesWithoutUndo();}
    private static void Array(Object target,string name,Object[] values)
    {var s=new SerializedObject(target);var p=s.FindProperty(name);p.arraySize=values.Length;for(int i=0;i<values.Length;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=values[i];s.ApplyModifiedPropertiesWithoutUndo();}
    private static void CreateRunner()
    {
        var go=new GameObject("OpenSpaceRunner",typeof(NetworkRunner),typeof(NetworkSceneManagerDefault));
        settings.runnerPrefab=PrefabUtility.SaveAsPrefabAsset(go,Folder+"/OpenSpaceRunner.prefab").GetComponent<NetworkRunner>();Object.DestroyImmediate(go);
    }
    private static void CreateAvatar()
    {
        if(File.Exists(AvatarPath))throw new InvalidOperationException("ChprefabOpenSpace가 이미 있습니다. 덮어쓰지 않았습니다.");
        if(!AssetDatabase.CopyAsset("Assets/Ch/ChPrefab.prefab",AvatarPath))throw new IOException("광장 캐릭터 복사 실패");
        var root=PrefabUtility.LoadPrefabContents(AvatarPath);
        try
        {
            // Strip only the COPY. Appearance/equipment retain all authored mesh references.
            foreach(var component in root.GetComponentsInChildren<MonoBehaviour>(true).Reverse())
                if(component!=null && !(component is PlayerEquipment) && !(component is PlayerAppearance) && !(component is WitchCatAppearance))
                    Object.DestroyImmediate(component);
            foreach(var camera in root.GetComponentsInChildren<Camera>(true))Object.DestroyImmediate(camera.gameObject);
            foreach(var audio in root.GetComponentsInChildren<AudioSource>(true))Object.DestroyImmediate(audio);
            foreach(var rigid in root.GetComponentsInChildren<Rigidbody>(true))Object.DestroyImmediate(rigid);
            foreach(var canvas in root.GetComponentsInChildren<Canvas>(true))Object.DestroyImmediate(canvas.gameObject);
            foreach(var c in root.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
            root.name="ChprefabOpenSpace";root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=28;
            var controller=root.AddComponent<CharacterController>();controller.height=1.7f;controller.radius=.28f;controller.center=Vector3.up*.85f;controller.stepOffset=.3f;
            root.AddComponent<NetworkObject>();var motor=root.AddComponent<NetworkCharacterController>();motor.maxSpeed=settings.walkSpeed;motor.acceleration=20;motor.braking=20;
            var avatar=root.AddComponent<OpenSpaceAvatar>();var view=root.AddComponent<OpenSpaceCharacterView>();
            view.settings=settings;view.appearance=root.GetComponentInChildren<PlayerAppearance>(true);view.equipment=root.GetComponentInChildren<PlayerEquipment>(true);
            view.animator=root.GetComponentsInChildren<Animator>(true).First(a=>a.isHuman);
            view.animator.runtimeAnimatorController=null;view.animator.applyRootMotion=false;
            view.equipment.SetFlightEquipmentVisible(false);view.equipment.SetWeaponVisible(false);
            view.Cat=CreateCat(root.transform);
            var rod=Primitive("Fishing rod",PrimitiveType.Cylinder,root.transform,new Vector3(.45f,.95f,.5f),new Vector3(.025f,.8f,.025f),wood,false);
            rod.transform.localRotation=Quaternion.Euler(35,0,-25);view.fishingRod=rod;rod.SetActive(false);
            view.emoteLabel=WorldText("Emote",root.transform,new Vector3(0,2.5f,0),"",.11f);view.emoteLabel.fontSize=8;
            view.nicknameLabel=WorldText("Nickname",root.transform,new Vector3(0,2.1f,0),"",.085f);view.nicknameLabel.fontSize=6;
            view.titleLabel=WorldText("Chingho",root.transform,new Vector3(0,2.32f,0),"",.075f);view.titleLabel.fontSize=5;
            Set(avatar,"view",view);Set(avatar,"settings",settings);
            new NetworkObjectBakerEditTime().Bake(root);
            settings.avatarPrefab=PrefabUtility.SaveAsPrefabAsset(root,AvatarPath).GetComponent<NetworkObject>();
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    private static OpenSpaceCatView CreateCat(Transform parent)
    {
        var table=CatCompanionTable.Default;
        var holder=new GameObject("Companion").transform;holder.SetParent(parent,false);holder.localPosition=new Vector3(1.1f,.8f,.2f);
        var cat=Object.Instantiate(table.catPrefab,holder);cat.name="Cat";
        var animator=cat.GetComponentInChildren<Animator>();
        animator.Play(table.GetAnimation(CatAnimation.SittingIdle).stateName,0,0);animator.Update(0);
        foreach(var c in cat.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
        if(CatSeatGeometry.TryBounds(cat.transform,out var bounds))
        {float scale=table.catHeight/Mathf.Max(.01f,bounds.size.y);cat.transform.localScale*=scale;cat.transform.localPosition=Vector3.up*(-bounds.min.y*scale+.06f);}
        var plane=Object.Instantiate(table.paperAirplanePrefab,holder);plane.name="Paper airplane";plane.transform.localScale=table.planeScale;
        foreach(var c in plane.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
        var view=holder.gameObject.AddComponent<OpenSpaceCatView>();view.animator=animator;view.appearance=cat.GetComponent<WitchCatAppearance>();
        view.plane=plane.transform;view.bodyRenderers=cat.GetComponentsInChildren<Renderer>(true);view.settings=settings;view.animations=table;
        view.head=cat.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name.Equals("head",StringComparison.OrdinalIgnoreCase));
        foreach(var skin in cat.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            for(int i=0;i<skin.sharedMesh.blendShapeCount;i++)
            {string shape=skin.sharedMesh.GetBlendShapeName(i).ToLowerInvariant();if(shape.Contains("close")||shape.Contains("blink")){view.eyes=skin;view.closedEyes=i;break;}}
        view.purr=holder.gameObject.AddComponent<AudioSource>();view.purr.playOnAwake=false;
        foreach(var t in holder.GetComponentsInChildren<Transform>(true))t.gameObject.layer=28;
        return view;
    }
    private static GameObject Primitive(string name,PrimitiveType type,Transform parent,Vector3 position,Vector3 scale,Material material,bool collider=true)
    {
        var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;
        var renderer=go.GetComponent<Renderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
        if(!collider)Object.DestroyImmediate(go.GetComponent<Collider>());return go;
    }
    private static TMP_Text WorldText(string name,Transform parent,Vector3 position,string text,float scale)
    {
        var go=new GameObject(name,typeof(TextMeshPro));go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=Vector3.one*scale;
        var label=go.GetComponent<TextMeshPro>();label.font=font;label.text=text;label.fontSize=12;label.alignment=TextAlignmentOptions.Center;label.richText=false;label.color=new Color(.08f,.22f,.25f);
        label.rectTransform.sizeDelta=new Vector2(40,8);return label;
    }
    private static void BuildScene()
    {
        var scene=EditorSceneManager.OpenScene(OpenSpaceSession.ScenePath,OpenSceneMode.Single);
        var root=new GameObject("OpenSpace authored world");var world=root.AddComponent<OpenSpaceWorld>();
        var camera=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Camera>(true)).First();
        camera.transform.position=new Vector3(0,4,-21);camera.transform.rotation=Quaternion.Euler(18,0,0);camera.farClipPlane=800;
        camera.backgroundColor=new Color(.55f,.78f,.9f);camera.clearFlags=CameraClearFlags.Skybox;
        if(camera.GetComponent<AudioListener>()==null)camera.gameObject.AddComponent<AudioListener>();
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=new Color(.6f,.8f,.88f);RenderSettings.fogStartDistance=80;RenderSettings.fogEndDistance=650;
        RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.7f,.83f,1);RenderSettings.ambientEquatorColor=new Color(.65f,.7f,.7f);RenderSettings.ambientGroundColor=new Color(.35f,.4f,.4f);
        Primitive("Seaside plaza",PrimitiveType.Cube,root.transform,new Vector3(0,-.5f,0),new Vector3(70,1,55),stone);
        Primitive("Central lawn",PrimitiveType.Cube,root.transform,new Vector3(-14,.03f,-1),new Vector3(14,.08f,16),grass);
        Primitive("Sea",PrimitiveType.Plane,root.transform,new Vector3(0,-1.1f,120),new Vector3(150,1,150),water,false);
        for(int i=0;i<6;i++)
        {
            float x=-24+i*9.6f;
            Primitive("Shade tree trunk",PrimitiveType.Cylinder,root.transform,new Vector3(x,1.3f,-23),new Vector3(.5f,1.3f,.5f),wood);
            Primitive("Shade tree crown",PrimitiveType.Sphere,root.transform,new Vector3(x,3.7f,-23),new Vector3(4,4,4),grass,false);
            Primitive("Rest bench",PrimitiveType.Cube,root.transform,new Vector3(x,.5f,-19),new Vector3(3,.2f,.7f),wood);
        }
        for(int i=0;i<3;i++)
        {
            Primitive("Cat shelter post",PrimitiveType.Cylinder,root.transform,new Vector3(-18+i*4,1.6f,-2),new Vector3(.25f,1.6f,.25f),stone);
            Primitive("Cat shelter canopy",PrimitiveType.Sphere,root.transform,new Vector3(-18+i*4,3.3f,-2),new Vector3(4,.5f,4),teal,false);
        }
        Primitive("West safety wall",PrimitiveType.Cube,root.transform,new Vector3(-34,1,0),new Vector3(.4f,2,55),stone);
        Primitive("East safety wall",PrimitiveType.Cube,root.transform,new Vector3(34,1,0),new Vector3(.4f,2,55),stone);
        Primitive("Rear safety wall",PrimitiveType.Cube,root.transform,new Vector3(0,1,-27),new Vector3(68,2,.4f),stone);
        Primitive("Coastal railing",PrimitiveType.Cube,root.transform,new Vector3(0,.7f,26),new Vector3(68,1.4f,.25f),wood);
        var points=new Transform[4];
        for(int i=0;i<4;i++){points[i]=new GameObject("Spawn "+(i+1)).transform;points[i].SetParent(root.transform);points[i].position=new Vector3((i%2==0?-1:1)*3,.15f,-16-(i/2)*3);}
        var spots=new Transform[22];
        for(int i=0;i<22;i++)
        {
            var seat=new GameObject("Fishing spot "+(i+1));seat.transform.SetParent(root.transform);seat.transform.position=new Vector3(-28.35f+i*2.7f,.1f,24);
            spots[i]=seat.transform;var view=seat.AddComponent<OpenSpaceSeatView>();view.index=i;
            view.marker=Primitive("Place marker",PrimitiveType.Cylinder,seat.transform,Vector3.zero,new Vector3(.9f,.025f,.9f),teal,false).GetComponent<Renderer>();
            view.bobber=Primitive("Bobber",PrimitiveType.Sphere,seat.transform,new Vector3(0,-1.1f,5),Vector3.one*.22f,white,false).transform;
            view.label=WorldText("Place label",seat.transform,new Vector3(0,.1f,.7f),(i+1)+" · 빈 낚시 자리",.055f);view.label.transform.rotation=Quaternion.Euler(70,180,0);
        }
        var hub=root.AddComponent<NetworkObject>();var authority=root.AddComponent<OpenSpaceHub>();Set(authority,"settings",settings);Array(authority,"seats",spots);
        var preview=Object.Instantiate(settings.avatarPrefab.gameObject);preview.name="Assigned outfit preview";preview.transform.position=new Vector3(0,-50,0);
        Object.DestroyImmediate(preview.GetComponent<OpenSpaceAvatar>());
        foreach(var behaviour in preview.GetComponentsInChildren<NetworkBehaviour>(true))Object.DestroyImmediate(behaviour);
        Object.DestroyImmediate(preview.GetComponent<NetworkObject>());Object.DestroyImmediate(preview.GetComponent<CharacterController>());
        foreach(var t in preview.GetComponentsInChildren<Transform>(true))t.gameObject.layer=29;
        Primitive("Preview floor",PrimitiveType.Cube,preview.transform,new Vector3(0,-.1f,0),new Vector3(16,.2f,16),stone,false);
        preview.SetActive(false);
        var ui=BuildUI();Set(world,"settings",settings);Set(world,"viewCamera",camera);Set(world,"ui",ui);Set(world,"outfitPreview",preview.GetComponent<OpenSpaceCharacterView>());
        Array(world,"spawns",points);Array(world,"fishingSeats",spots);Set(ui,"world",world);
        new GameObject("EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
        new NetworkObjectBakerEditTime().Bake(root);
        EditorSceneManager.SaveScene(scene);
    }
    private static RectTransform Rect(string name,Transform parent,Vector2 anchor,Vector2 pivot,Vector2 position,Vector2 size)
    {var rt=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rt.SetParent(parent,false);rt.anchorMin=rt.anchorMax=anchor;rt.pivot=pivot;rt.anchoredPosition=position;rt.sizeDelta=size;return rt;}
    private static TMP_Text Label(string name,Transform parent,Vector2 position,Vector2 size,string value,int fontSize=22)
    {var rt=Rect(name,parent,new Vector2(.5f,.5f),new Vector2(.5f,.5f),position,size);var text=rt.gameObject.AddComponent<TextMeshProUGUI>();text.font=font;text.text=value;text.fontSize=fontSize;text.color=Color.white;text.richText=false;text.raycastTarget=false;return text;}
    private static Button Button(string name,Transform parent,Vector2 position,Vector2 size,string value,UnityEngine.Events.UnityAction action,out TMP_Text text)
    {
        var rt=Rect(name,parent,new Vector2(.5f,.5f),new Vector2(.5f,.5f),position,size);var image=rt.gameObject.AddComponent<Image>();image.color=new Color(.1f,.52f,.52f,.96f);
        var b=rt.gameObject.AddComponent<Button>();b.targetGraphic=image;text=Label("Text",rt,Vector2.zero,size-new Vector2(12,4),value,21);text.alignment=TextAlignmentOptions.Center;
        if(action!=null)UnityEventTools.AddPersistentListener(b.onClick,action);return b;
    }
    private static OpenSpaceUI BuildUI()
    {
        var root=new GameObject("OpenSpaceUI",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster),typeof(OpenSpaceUI));
        root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
        var ui=root.GetComponent<OpenSpaceUI>();Set(ui,"settings",settings);
        var top=Rect("OS_HUD",root.transform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-15),new Vector2(1880,100));
        Set(ui,"status",Label("Session",top,new Vector2(-580,25),new Vector2(700,36),"광장 접속 준비",22));
        Set(ui,"money",Label("Money",top,new Vector2(760,25),new Vector2(260,36),"",24));
        var actions=new UnityEngine.Events.UnityAction[]{ui.OpenOwnOutfit,ui.OpenCat,ui.OpenBag,ui.OpenCollection,ui.OpenHelp,ui.OpenMenu,ui.Recover,ui.RetryConnection,ui.ReturnToMain};
        string[] names={"내 코디","내 고양이","생선 가방","도감","도움말","메뉴","결과 확인 / 재시도","재입장","로비로"};
        for(int i=0;i<actions.Length;i++)Button(names[i],top,new Vector2(-830+i*205,-22),new Vector2(195,44),names[i],actions[i],out _);
        var prompt=Label("OS_PROMPT",root.transform,Vector2.zero,new Vector2(800,45),"",26);prompt.rectTransform.anchorMin=prompt.rectTransform.anchorMax=new Vector2(.5f,0);prompt.rectTransform.anchoredPosition=new Vector2(0,240);prompt.alignment=TextAlignmentOptions.Center;Set(ui,"prompt",prompt);
        var notice=Label("OS_NOTICE",root.transform,Vector2.zero,new Vector2(1100,100),"",24);notice.rectTransform.anchorMin=notice.rectTransform.anchorMax=new Vector2(.5f,1);notice.rectTransform.anchoredPosition=new Vector2(0,-155);notice.alignment=TextAlignmentOptions.Center;Set(ui,"notice",notice);
        var panel=Rect("ActivityPanel",root.transform,new Vector2(1,.5f),new Vector2(1,.5f),new Vector2(-25,-20),new Vector2(520,780));panel.gameObject.AddComponent<Image>().color=new Color(.04f,.1f,.16f,.94f);
        Set(ui,"panelRoot",panel.gameObject);Set(ui,"heading",Label("Heading",panel,new Vector2(0,345),new Vector2(470,50),"",30));Set(ui,"body",Label("Body",panel,new Vector2(0,190),new Vector2(470,240),"",22));
        var rowData=new OpenSpaceRow[6];
        for(int i=0;i<6;i++)
        {
            var button=Button("Row "+i,panel,new Vector2(0,40-i*55),new Vector2(470,48),"",null,out var text);UnityEventTools.AddIntPersistentListener(button.onClick,ui.SelectRow,i);
            var icon=Rect("Icon",button.transform,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(8,0),new Vector2(40,40)).gameObject.AddComponent<Image>();icon.raycastTarget=false;
            rowData[i]=new OpenSpaceRow{root=button.gameObject,button=button,text=text,icon=icon};
        }
        var serialized=new SerializedObject(ui);var rows=serialized.FindProperty("rows");rows.arraySize=6;
        for(int i=0;i<6;i++){var r=rows.GetArrayElementAtIndex(i);r.FindPropertyRelative("root").objectReferenceValue=rowData[i].root;r.FindPropertyRelative("button").objectReferenceValue=rowData[i].button;r.FindPropertyRelative("text").objectReferenceValue=rowData[i].text;r.FindPropertyRelative("icon").objectReferenceValue=rowData[i].icon;}serialized.ApplyModifiedPropertiesWithoutUndo();
        var first=Button("Primary",panel,new Vector2(-120,-310),new Vector2(230,55),"",ui.Primary,out var firstText);Set(ui,"primary",first);Set(ui,"primaryText",firstText);
        var second=Button("Secondary",panel,new Vector2(120,-310),new Vector2(230,55),"",ui.Secondary,out var secondText);Set(ui,"secondary",second);Set(ui,"secondaryText",secondText);
        Button("Close",panel,new Vector2(0,-365),new Vector2(470,40),"닫기",ui.ClosePanel,out _);
        var modal=Rect("OS_CONFIRM",root.transform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,Vector2.zero);
        modal.anchorMin=Vector2.zero;modal.anchorMax=Vector2.one;modal.offsetMin=modal.offsetMax=Vector2.zero;
        modal.gameObject.AddComponent<Image>().color=new Color(0,0,0,.5f);
        var confirm=Rect("DialogPanel",modal,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(680,340));confirm.gameObject.AddComponent<Image>().color=new Color(.04f,.1f,.16f,.98f);
        Set(ui,"confirmation",modal.gameObject);Set(ui,"confirmationText",Label("Dialog",confirm,new Vector2(0,40),new Vector2(620,210),"",25));
        Button("Confirm",confirm,new Vector2(-155,-115),new Vector2(280,55),"확인",ui.ConfirmYes,out _);Button("Cancel",confirm,new Vector2(155,-115),new Vector2(280,55),"취소",ui.ConfirmNo,out _);
        var fish=Rect("OS_FISH",root.transform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,20),new Vector2(720,170));fish.gameObject.AddComponent<Image>().color=new Color(.04f,.1f,.16f,.85f);
        Set(ui,"fishingHUD",fish.gameObject);Set(ui,"fishStatus",Label("Status",fish,new Vector2(0,32),new Vector2(680,75),"",24));
        var fishingActions=new UnityEngine.Events.UnityAction[]{ui.StartFishing,ui.Hook,ui.CancelFishing,ui.LeaveFishing};string[] fishingNames={"낚시 시작","챔질","중단","자리 떠나기"};string[] fields={"fishStart","fishHook","fishCancel","fishLeave"};
        for(int i=0;i<4;i++)Set(ui,fields[i],Button(fields[i],fish,new Vector2(-270+i*180,-52),new Vector2(170,50),fishingNames[i],fishingActions[i],out _));
        CreateChat(root.transform,ui);
        Set(ui,"activityAudio",root.AddComponent<AudioSource>());root.GetComponent<AudioSource>().playOnAwake=false;
        modal.SetAsLastSibling();panel.gameObject.SetActive(false);modal.gameObject.SetActive(false);fish.gameObject.SetActive(false);
        return ui;
    }
    private static void CreateChat(Transform canvas,OpenSpaceUI ui)
    {
        var chat=Rect("OS_CHAT",canvas,new Vector2(0,0),new Vector2(0,0),new Vector2(20,20),new Vector2(550,280));chat.gameObject.AddComponent<Image>().color=new Color(.04f,.1f,.16f,.72f);
        var viewport=Rect("Viewport",chat,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-10),new Vector2(530,210));viewport.gameObject.AddComponent<RectMask2D>();
        var content=Rect("History",viewport,new Vector2(0,1),new Vector2(0,1),Vector2.zero,new Vector2(530,210));
        var history=content.gameObject.AddComponent<TextMeshProUGUI>();history.font=font;history.fontSize=20;history.richText=false;history.raycastTarget=false;
        var fitter=content.gameObject.AddComponent<ContentSizeFitter>();fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.content=content;scroll.viewport=viewport;scroll.horizontal=false;Set(ui,"chatHistory",history);Set(ui,"chatScroll",scroll);
        var inputRect=Rect("Input",chat,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(-45,10),new Vector2(430,44));inputRect.gameObject.AddComponent<Image>().color=new Color(.94f,.97f,.97f);
        var input=inputRect.gameObject.AddComponent<TMP_InputField>();var text=Label("Text",inputRect,Vector2.zero,new Vector2(410,40),"",20);text.color=Color.black;input.textComponent=(TextMeshProUGUI)text;input.textViewport=inputRect;input.characterLimit=100;
        var placeholder=Label("Placeholder",inputRect,Vector2.zero,new Vector2(410,40),"Enter로 채팅 · 100자",18);placeholder.color=Color.gray;input.placeholder=placeholder;
        Set(ui,"chatInput",input);Button("Send",chat,new Vector2(225,-108),new Vector2(90,44),"전송",ui.SendChat,out _);
    }
    private static void BindMain()
    {
        var scene=EditorSceneManager.OpenScene(OpenSpaceSession.MainPath,OpenSceneMode.Single);
        var button=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Button>(true)).Single(b=>b.name=="OpenSpace");
        var root=new GameObject("OpenSpace Session");var session=root.AddComponent<OpenSpaceSession>();Set(session,"settings",settings);
        var entry=button.gameObject.AddComponent<OpenSpaceEntry>();Set(entry,"session",session);
        var status=Label("OpenSpaceStatus",button.transform,new Vector2(0,-60),new Vector2(550,80),"",18);Set(entry,"status",status);
        EditorSceneManager.SaveScene(scene);
    }
}
