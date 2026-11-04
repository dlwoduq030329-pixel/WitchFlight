from pathlib import Path
import json,re,html
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.platypus import BaseDocTemplate, Frame, PageTemplate, Paragraph, Spacer, PageBreak, Table, TableStyle, CondPageBreak, Flowable
from reportlab.platypus.tableofcontents import TableOfContents
from pypdf import PdfReader

WORK=Path(__file__).resolve().parent
ROOT=WORK.parents[2]
OUT=ROOT/'output/pdf/WitchFlight_Perks_XP_Design.pdf'
OUT.parent.mkdir(parents=True,exist_ok=True)
source=json.loads((WORK/'source.json').read_text(encoding='utf-8'))
pdfmetrics.registerFont(TTFont('Malgun','C:/Windows/Fonts/malgun.ttf'))
pdfmetrics.registerFont(TTFont('MalgunBold','C:/Windows/Fonts/malgunbd.ttf'))
pdfmetrics.registerFontFamily('Malgun',normal='Malgun',bold='MalgunBold',italic='Malgun',boldItalic='MalgunBold')
NAVY=colors.HexColor('#192C3C'); TEAL=colors.HexColor('#008E92'); LILAC=colors.HexColor('#746290')
INK=colors.HexColor('#263642'); MUTED=colors.HexColor('#617280'); PALE=colors.HexColor('#EFF6F6'); LINE=colors.HexColor('#CDDADB')
PW,PH=A4; M=45; W=PW-2*M
styles={}
for name,size,leading,before,after,font,color in [
 ('body',10,15,0,6,'Malgun',INK),('bullet',9.8,14.5,0,3.5,'Malgun',INK),
 ('h2',18,25,8,10,'MalgunBold',NAVY),('h3',12,18,14,8,'MalgunBold',TEAL),
 ('cell',9.3,13.5,0,0,'Malgun',INK),('th',9.3,13.5,0,0,'MalgunBold',colors.white),
 ('small',8.8,13.7,0,6,'Malgun',MUTED),('note',10,16,0,0,'Malgun',NAVY)]:
 styles[name]=ParagraphStyle(name,fontName=font,fontSize=size,leading=leading,textColor=color,
  spaceBefore=before,spaceAfter=after,wordWrap='CJK',keepWithNext=name in ('h2','h3'))
styles['bullet'].leftIndent=12;styles['bullet'].firstLineIndent=-10
def clean(t): return t.translate(str.maketrans({'−':'-','–':'-','—':'-','‑':'-','‒':'-'}))
def para(t,k='body'):
 t=html.escape(clean(t));t=re.sub(r'\*\*(.+?)\*\*',r'<b>\1</b>',t)
 return Paragraph(t,styles[k])
def box(t):
 obj=Table([[para(t,'note')]],colWidths=[W])
 obj.setStyle(TableStyle([('BACKGROUND',(0,0),(-1,-1),PALE),('BOX',(0,0),(-1,-1),.6,LINE),
 ('LEFTPADDING',(0,0),(-1,-1),14),('RIGHTPADDING',(0,0),(-1,-1),14),('TOPPADDING',(0,0),(-1,-1),12),('BOTTOMPADDING',(0,0),(-1,-1),12)]))
 return obj
class Cover(Flowable):
 def __init__(self):
  super().__init__();self.width=W;self.height=695
 def draw(self):
  c=self.canv;c.setFillColor(TEAL);c.rect(0,640,48,4,fill=1,stroke=0)
  c.setFont('MalgunBold',11);c.drawString(0,660,'WITCHFLIGHT  /  GAME DESIGN')
  c.setFillColor(NAVY);c.setFont('MalgunBold',30);c.drawString(0,579,'전투 특전 34종 기획안')
  c.setFont('Malgun',18);c.drawString(0,540,'경험치 획득과 선택 UX')
  c.setFillColor(MUTED);c.setFont('Malgun',10);c.drawString(0,506,'2026.10.12  |  고정 단계 강화와 Tab 비교 UI  |  구현 전')
  p=para(source['blocks'][1]);_,h=p.wrap(W,150);p.drawOn(c,0,460-h)
  for i,(v,label) in enumerate([('34','마법 24 + 공용 10'),('4','최대 강화 선택 횟수'),('1','최대 강화 가능 슬롯')]):
   x=i*(W+12)/3;c.setFillColor(PALE);c.roundRect(x,265,(W-24)/3,104,7,fill=1,stroke=0)
   c.setFillColor(TEAL if i!=2 else LILAC);c.setFont('MalgunBold',29);c.drawString(x+16,322,v)
   c.setFillColor(NAVY);c.setFont('Malgun',9.1);c.drawString(x+16,289,label)
  c.setFillColor(NAVY);c.setFont('MalgunBold',15);c.drawString(0,220,'보통은 3회, 장기전에서는 4회')
  p=para('라운드당 160초, 최대 3라운드의 성장 흐름을 기준으로 한다. 마법마다 정해진 순서로 강화하며 전투 중에는 투자할 슬롯만 고른다. 마법을 교체하면 보존된 단계까지 자동 적용한다.')
  _,h=p.wrap(W,100);p.drawOn(c,0,194-h)
  c.setStrokeColor(LINE);c.line(0,101,W,101)
  p=para('확정된 설계와 제안 수치를 구분했다. 경험치 지급량, 강화 효과의 수치, 카드 크기 및 연출 시간은 플레이테스트를 통해 조정한다. 이 문서는 게임 코드의 적용 완료를 의미하지 않는다.','small')
  _,h=p.wrap(W,80);p.drawOn(c,0,80-h)
class HudSketch(Flowable):
 def __init__(self): super().__init__();self.width=W;self.height=248
 def draw(self):
  c=self.canv;c.setFillColor(colors.HexColor('#F4F7F8'));c.setStrokeColor(LINE);c.roundRect(0,20,W,215,5,stroke=1,fill=1)
  c.setFont('MalgunBold',9.5);c.setFillColor(MUTED);c.drawCentredString(W/2,211,'커서 없이 선택 · 중앙 전투 시야 유지')
  c.setStrokeColor(colors.HexColor('#9CABB5'));c.circle(W/2,157,12,stroke=1,fill=0)
  c.line(W/2-3,157,W/2+3,157);c.line(W/2,154,W/2,160)
  c.setFont('Malgun',8);c.drawCentredString(W/2,134,'카메라 회전 유지')
  c.setStrokeColor(TEAL);c.setLineWidth(3);c.circle(W/2,78,25,stroke=1,fill=0)
  c.setFillColor(TEAL);c.setFont('MalgunBold',10);c.drawCentredString(W/2,74,'MP')
  for x,label,button in [(73,'왼쪽 후보','좌클릭'),(W-203,'오른쪽 후보','우클릭')]:
   c.setFillColor(NAVY);c.setStrokeColor(TEAL);c.setLineWidth(1);c.roundRect(x,53,130,53,5,fill=1,stroke=1)
   c.setFillColor(colors.HexColor('#BAE1E1'));c.roundRect(x+10,65,25,29,3,fill=1,stroke=0)
   c.setFillColor(NAVY);c.setFont('Malgun',6.5);c.drawCentredString(x+22.5,76,'아이콘')
   c.setFillColor(colors.white);c.setFont('MalgunBold',8.8);c.drawString(x+44,88,label)
   c.setFont('Malgun',8);c.drawString(x+44,73,'설명 · '+button)
  c.setFillColor(MUTED);c.setFont('Malgun',7.5);c.drawString(14,73,'HP')
  c.setFillColor(colors.HexColor('#99AAB6'));c.rect(14,60,42,6,fill=1,stroke=0)
  c.setFillColor(TEAL);c.rect(14,48,28,4,fill=1,stroke=0);c.setFont('Malgun',7.5);c.drawString(14,34,'XP')
  c.setFillColor(MUTED);c.setFont('Malgun',8);c.drawRightString(W-12,34,'Space 유지 중에만 특전 입력')
  c.drawString(0,3,'배치 개념도. 실제 해상도 비율 및 카드 크기를 재현한 화면은 아니다.')
class Doc(BaseDocTemplate):
 def __init__(self,path):
  super().__init__(path,pagesize=A4,leftMargin=M,rightMargin=M,topMargin=55,bottomMargin=47,
   title=source['title'],author='WitchFlight',subject='34 perks, XP acquisition and cursorless selection UX')
  self.addPageTemplates(PageTemplate(id='normal',frames=[Frame(M,47,W,PH-102,leftPadding=0,rightPadding=0,topPadding=0,bottomPadding=0)],onPage=self.decorate))
 def decorate(self,c,doc):
  if doc.page==1:return
  c.saveState();c.setStrokeColor(LINE);c.setLineWidth(.5);c.line(M,PH-35,PW-M,PH-35)
  c.setFont('MalgunBold',8);c.setFillColor(TEAL);c.drawString(M,PH-26,'WITCHFLIGHT')
  c.setFont('Malgun',8);c.setFillColor(MUTED);c.drawRightString(PW-M,PH-26,'특전 · 경험치 · 선택 UX')
  c.line(M,34,PW-M,34);c.setFont('Malgun',7.7);c.drawString(M,21,'2026.10.12  |  기획 초안')
  c.drawRightString(PW-M,21,f'{doc.page:02d}');c.restoreState()
 def afterFlowable(self,f):
  if getattr(f,'isChapter',False):
   self.canv.bookmarkPage(f.bookmark);self.canv.addOutlineEntry(f.getPlainText(),f.bookmark,0,False)
   self.notify('TOCEntry',(0,f.getPlainText(),self.page,f.bookmark))
def table(md):
 raw=[x.strip() for x in md.splitlines() if x.strip()]
 rows=[[x.strip() for x in r.strip('|').split('|')] for r in raw]
 rows=[r for r in rows if not all(re.fullmatch(r':?-+:?',x.replace(' ','')) for x in r)]
 n=len(rows[0])
 if rows[0][0]=='번호': f=[.065,.25,.13,.555] if n==4 else [.065,.235,.7]
 elif '이번 선택권에 필요한' in ' '.join(rows[0]): f=[.18,.43,.39]
 elif rows[0][0]=='획득 경로': f=[.18,.27,.55]
 elif rows[0][0]=='마법 강화 상태': f=[.24,.38,.38]
 elif n==2:f=[.34,.66]
 elif n==5:f=[.25,.2,.2,.2,.15]
 elif n==3:f=[.22,.39,.39]
 else:f=[1/n]*n
 t=Table([[para(x,'th' if i==0 else 'cell') for x in row] for i,row in enumerate(rows)],colWidths=[W*v for v in f],repeatRows=1,hAlign='LEFT',spaceBefore=2,spaceAfter=7)
 cmd=[('BACKGROUND',(0,0),(-1,0),NAVY),('VALIGN',(0,0),(-1,-1),'TOP'),
 ('LEFTPADDING',(0,0),(-1,-1),8),('RIGHTPADDING',(0,0),(-1,-1),8),
 ('TOPPADDING',(0,0),(-1,-1),5.5),('BOTTOMPADDING',(0,0),(-1,-1),5.5),
 ('LINEBELOW',(0,0),(-1,0),.7,NAVY),('LINEBELOW',(0,1),(-1,-1),.4,LINE)]
 for i in range(1,len(rows)):
  if i%2:cmd.append(('BACKGROUND',(0,i),(-1,i),PALE))
 if rows[0][0]=='번호':cmd += [('LEFTPADDING',(0,0),(0,-1),5),('RIGHTPADDING',(0,0),(0,-1),3)]
 t.setStyle(TableStyle(cmd));return t
toc=TableOfContents()
toc.levelStyles=[ParagraphStyle('toc',fontName='Malgun',fontSize=11,leading=24,textColor=INK,spaceBefore=3)]
toc.dotsMinLevel=0
story=[Cover(),PageBreak(),para('목차','h2'),toc,Spacer(1,26),
 box('읽는 기준  |  최대 4회는 강화 선택의 횟수다. 일반 강화에서 최대 강화로 올리는 데에도 1회를 사용한다. 표의 특전 번호는 MagicType enum 값이 아니라 기획 식별 번호다.'),
 Spacer(1,16),para('수정 가능한 원본 기획안','h3'),
 Paragraph(f'<link href="{source["source_url"]}" color="#008E92">WitchFlight 전투 특전 34종 기획안 열기</link>',styles['body']),
 para('마법별 고정 1단계와 2단계, 교체 시 자동 이전, Tab 유지 중 특전 비교를 반영했다. Space 입력 규칙과 기존 경험치 설계를 유지하며, 수치는 플레이테스트용 초안이다.','small'),PageBreak()]
chapter=0;in_spells=False;first_spells=True
for md in source['blocks'][2:]:
 md=md.strip()
 if not md:continue
 if md.startswith('## '):
  chapter+=1
  story.append(PageBreak() if chapter in {2,3,4,5,6,7,8,9,10,11} else CondPageBreak(135))
  p=para(f'{chapter:02d}  {md[3:]}','h2');p.isChapter=True;p.bookmark=f'chapter{chapter}';story.append(p)
  in_spells=md=='## 마법 전용 특전 24종'
 elif md.startswith('### '):
  name=md[4:]
  if in_spells:
   if not first_spells:story.append(PageBreak())
   first_spells=False
  else:story.append(CondPageBreak(110))
  story.append(para(name,'h3'))
  if name=='HUD 배치와 시야 보호':story.append(HudSketch())
 elif md.startswith('|'):story.append(table(md))
 elif md.startswith('- '):
  for line in md.splitlines():story.append(para('• '+line[2:] if line.startswith('- ') else line,'bullet'))
 elif re.match(r'\d+\. ',md):story.append(para(md,'bullet'))
 else:story.append(para(md))
Doc(str(OUT)).multiBuild(story)
reader=PdfReader(OUT);text='\n'.join(p.extract_text() for p in reader.pages)
def norm(s):return re.sub(r'\s+','',clean(s)).replace('**','')
missing=[]
for i,block in enumerate(source['blocks'][1:],1):
 for line in block.splitlines():
  chunks=[x.strip() for x in line.strip('|').split('|') if not re.fullmatch(r':?-+:?',x.strip())] if line.startswith('|') else [re.sub(r'^(#+ |\- )','',line)]
  missing += [(i,x) for x in chunks if norm(x) not in norm(text)]
assert not missing,missing
perks=re.findall(r'^\| (\d{2}) \|','\n'.join(source['blocks']),re.M)
assert [int(n) for n in perks]==list(range(1,35))
for old in ['커서를 표시하고 잠금을','표시 완료 후 새 클릭만','패링 또는 플레어','두 경로 공통 10초','추가 선택 차단']:
 assert old not in text,old
for new in ['0 XP','4 XP','좌클릭','우클릭','연출','Space 해제']:
 assert new in text,new
for old in ['새 마법의 A 특전을','A/B 선택 묶음','특전 효과 3개','일반 강화 수치를 대체','특전만 다시 선택한다']:
 assert old not in text,old
for new in ['Tab','고정 1단계','정화','자동 적용','Tab을 떼면 즉시 사라진다']:
 assert norm(new) in norm(text),new
font=pdfmetrics.getFont('Malgun')
glyphs=sorted({c for c in norm('\n'.join(source['blocks'])) if ord(c)>32 and ord(c) not in font.face.charToGlyph})
assert not glyphs,glyphs
print(json.dumps({'path':str(OUT),'pages':len(reader.pages),'perks':len(perks),'missing_source_fragments':len(missing),'bytes':OUT.stat().st_size},ensure_ascii=False))
print('\n'.join(f'{i+1}: {len(p.extract_text())} chars | '+p.extract_text()[60:115].replace('\n',' ') for i,p in enumerate(reader.pages)))

