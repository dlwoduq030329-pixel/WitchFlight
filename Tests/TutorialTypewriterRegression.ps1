# Execute production text reveal and click handling with a TMP/input test double.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$director=Get-Content Assets/Script/CatTutorialDirector.cs -Raw
function Method($name){
 $m=[regex]::Match($director,'(?ms)^    (?:public|private) void '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"};$m.Value
}
$code=@'
using System;
public static class Time {public static float unscaledTime;}
public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);public static int FloorToInt(float x)=>(int)Math.Floor(x);}
public class GO {public bool active;public void SetActive(bool v){active=v;}}
public class TextInfo {public int characterCount;}
public class Label {
 public string text;public int maxVisibleCharacters=int.MaxValue,parses;
 public GO gameObject=new();public TextInfo textInfo=new();
 public void ForceMeshUpdate(bool includeInactive){parses++;textInfo.characterCount=System.Text.RegularExpressions.Regex.Replace(text,"<[^>]+>","").Length;}
}
public class Graphic {public bool raycastTarget;}
public class Button {public bool interactable;public Graphic targetGraphic=new();}
public class Avatar {public int plays;public void Play(int animation){plays++;}}
public enum CatTrainingAction {None,Boost}
public class Step {public bool waitForTargetClick;public CatTrainingAction practice;public int animation;}
public class Table {public float tutorialCharactersPerSecond=32;}
public class Harness {
 public string stepDialogue="<b>안녕하세요, 반갑다냥!</b>";
 public bool dialogueWaiting,dialogueTyping,running=true,greeting,trainingScene,practiceStarted,advanceQueued;
 public int dialogueCharacterCount,step,practiceCalls,greetingCloses,stepShows;
 public float dialogueStartedAt,showAt,clickAfter,greetingAt;
 public Label explanationText=new(),nextHint=new();public Button next=new();public Avatar avatar=new();public Table table=new();
 public Step[] Steps={new(),new(),new()};
 void BeginPractice(){practiceCalls++;practiceStarted=true;}
 void CloseGreeting(){greetingCloses++;}
 void ShowStep(){stepShows++;StartDialogueTyping();RefreshDialogueHint();}
 public void Start(float delay=0){showAt=Time.unscaledTime+delay;dialogueWaiting=delay>0;if(!dialogueWaiting)StartDialogueTyping();RefreshDialogueHint();}
 public void Tick(float now){Time.unscaledTime=now;UpdateDialogueTyping();}
 public void ClickTarget()=>TargetClicked();
 __METHODS__
}
public static class Checks {
 static int n;static void Check(bool ok,string name){n++;if(!ok)throw new Exception(name);}
 static Harness New(){Time.unscaledTime=0;return new();}
 public static string Run(){
  var h=New();h.Start();var full=h.explanationText.text;
  Check(h.dialogueTyping&&h.explanationText.maxVisibleCharacters==0,"Text starts hidden");
  h.Tick(.1f);Check(h.explanationText.maxVisibleCharacters==3&&h.explanationText.text==full,"Reveal glyphs without rebuilding rich text");
  Check(h.explanationText.parses==1,"Parse text once");
  h.Next();Check(!h.dialogueTyping&&h.step==0&&h.explanationText.maxVisibleCharacters==int.MaxValue,"First click completes only this line");
  h.Next();Check(h.step==0,"Duplicate click in same frame cannot advance");
  h.Tick(.21f);h.Next();Check(h.step==1&&h.stepShows==1&&h.dialogueTyping,"Second click advances and starts new line");
  h=New();h.Start();h.Tick(2);Check(!h.dialogueTyping,"Natural completion");h.Next();Check(h.step==1,"Completed line needs only one click");
  h=New();h.Start(1);h.Tick(.1f);Check(h.dialogueWaiting,"Dreamy delay retained");h.Next();
  Check(!h.dialogueWaiting&&!h.dialogueTyping&&h.step==0&&h.avatar.plays==1,"Click during delay reveals full dialogue");
  h=New();h.Start(1);h.Tick(1);Check(h.dialogueTyping&&h.explanationText.maxVisibleCharacters==0,"Delay naturally starts typing");
  h.Tick(1.25f);Check(h.explanationText.maxVisibleCharacters==8,"Unscaled reveal timing");
  h=New();h.trainingScene=true;h.Steps[0].practice=CatTrainingAction.Boost;h.Start();h.Tick(.1f);h.Next();
  Check(h.practiceCalls==0,"Reveal click must not start practice");h.Tick(.21f);h.Next();
  Check(h.practiceCalls==1&&h.step==0,"Next click starts existing practice");h.Next();Check(h.practiceCalls==1,"No duplicate practice start");
  h=New();h.Steps[0].waitForTargetClick=true;h.Start();Check(h.next.interactable,"Guided dialogue can be skipped");h.ClickTarget();
  Check(!h.advanceQueued,"Guided target cannot advance while text is typing");h.Tick(.1f);h.Next();
  Check(!h.next.interactable&&!h.advanceQueued,"After reveal, wait for highlighted target");h.Tick(.21f);h.ClickTarget();Check(h.advanceQueued,"Target click advances guided step");
  h=New();h.table.tutorialCharactersPerSecond=10;h.Start();h.Tick(.3f);Check(h.explanationText.maxVisibleCharacters==3,"Inspector speed is respected");
  h=New();h.stepDialogue="";h.Start();Check(!h.dialogueTyping,"Empty line cannot stall");
  return n+" tutorial reveal/click checks passed";
 }
}
'@
$methods=@('StartDialogueTyping','RefreshDialogueHint','FinishDialogueTyping','UpdateDialogueTyping','Next','TargetClicked')|ForEach-Object {Method $_}
$code=$code.Replace('__METHODS__',($methods -join "`n"))
Add-Type -TypeDefinition $code
[Checks]::Run()
