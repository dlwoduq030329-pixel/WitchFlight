import fs from 'node:fs/promises';
import {createRequire} from 'node:module';
import {pathToFileURL} from 'node:url';
const require = createRequire('C:/Program Files/WindowsApps/OpenAI.CodexPrimaryRuntime.v26-1104-709-0_26.1104.709.0_x64__3k8sg7r9htsxt/dependencies/node/node_modules/entry.js');
const {Workbook, SpreadsheetFile} = await import(pathToFileURL(require.resolve('@oai/artifact-tool')).href);
const out = new URL('./', import.meta.url).pathname.replace(/^\/(\w:)/,'$1');
const root='C:/Users/USER/Desktop/git/WitchFlight/';
function entries(text, section) {
  const sectionText=text.split(`  ${section}:`)[1].split(/\n  [a-zA-Z]+:/)[0];
  return sectionText.split(/\r?\n  - /).slice(1).map(block=> {
    const row={};
    for(const line of block.split(/\r?\n/)) {
      const match=line.match(/^\s*(\w+):\s*(.*?)\s*$/);
      if(!match) continue;
      const [,key,value]=match;
      if (/^-?\d+(\.\d+)?$/.test(value)) row[key]=Number(value);
      else if(value.startsWith('"')&&value.endsWith('"')) row[key]=JSON.parse(value);
    }
    return row;
  });
}
const magic=entries(await fs.readFile(root+'Assets/Resources/MagicStatTable.asset','utf8'),'magics').sort((a,b)=>a.magic-b.magic);
const equipment=await fs.readFile(root+'Assets/Resources/EquipmentStatTable.asset','utf8');
const brooms=entries(equipment,'brooms'), hats=entries(equipment,'hats');
if(magic.length!==17||brooms.length!==3||hats.length!==3) throw Error('Unexpected data count');
const wb=Workbook.create();
function sheet(name,title,context,headers,rows,widths,source) {
  const s=wb.worksheets.add(name); s.showGridLines=false;
  const end=String.fromCharCode(64+headers.length), last=5+rows.length;
  s.getRange(`A1:${end}${last+4}`).format.font={name:'Arial',size:11,color:'#223044'};
  s.getRange('A2').values=[[title]]; s.getRange('A2').format.font={size:16,bold:true}; s.getRange('A2').format.rowHeight=30;
  s.getRange(`A1:${end}${last+4}`).format.verticalAlignment='center';
s.getRange('A3').values=[[context]];
  s.getRange(`A5:${end}5`).values=[headers];
  s.getRange(`A6:${end}${last}`).values=rows;
  s.getRange(`A5:${end}5`).format={fill:'#283D56',font:{bold:true,color:'#FFFFFF'},rowHeight:30,horizontalAlignment:'center',verticalAlignment:'center'};
  s.getRange(`A6:${end}${last}`).format.rowHeight=30;
  s.getRange(`B6:${end}${last}`).setNumberFormat('0');
  for(let i=0;i<widths.length;i++) s.getRange(`${String.fromCharCode(65+i)}1:${String.fromCharCode(65+i)}${last+4}`).format.columnWidth=widths[i];
  for(let r=6;r<=last;r+=2) s.getRange(`A${r}:${end}${r}`).format.fill='#F0F4F8';
  s.getRange(`A${last+2}`).values=[[source]];
  s.getRange(`A${last+2}`).format.font={size:10,color:'#64748B'};
  if(rows.length>10) s.freezePanes.freezeRows(5);
  return s;
}
const notes={5:'피해 없음. 기본 체력 회복 40',6:'피해 없음. 속박',7:'마나 대신 체력 소모',9:'소환체 공격 1회 기준',11:'회피·이동 보조',12:'연막·록온 거리 감소',13:'시전 후 검기 3회 사용',14:'2초 동안 30발 발사',15:'이동·투사체 속도 변경',16:'회복 보조. 기본 초당 체력 회복 5',17:'총 실소모 마나 ÷ 2를 전체 칼날에 분배'};
const rows=magic.map(m=>[m.displayName,m.apCost,m.magic===17?m.rainManaPerSecond:m.apPerSecond,m.healthCost,m.magic===17?'가변':m.damagePerSecond||m.damage,m.magic===17?'전체 칼날 총합':(!m.damage && !m.damagePerSecond)?'피해 없음':m.damagePerSecond?'1초당':m.magic===13||m.magic===14?'투사체 1발당':m.magic===9?'공격 1회당':'적중 1회당',notes[m.magic]||'기본 피해 기준']);
sheet('마법','마법 소모량과 데미지','2026-11-05 저장 데이터 기준 · 특전·버프·모드별 배율 제외',
 ['마법','시전 마나','추가 마나/초','체력 소모','데미지','피해 기준','비고'],rows,[23,14,17,14,14,22,55],
 '출처: MagicStatTable.asset / Player.BladeRain.cs. 칼날비는 차징 중 초당 5, 발사 시 50 소모.');
const b=sheet('빗자루','빗자루 이동과 체력','일반 비행 기준 · 속도 단위 m/s · 선회력 단위 °/s · 특전·상태 효과 제외',
 ['빗자루','기본 이동속도 (2단계)','최고 이동속도 (3단계)','선회력','체력'],brooms.map(x=>[x.displayName,null,x.maxSpeed,x.turnSpeed,x.maxHp]),[24,27,27,18,16],
 '출처: EquipmentStatTable.asset / Player.cs. 기본 속도 = 최고 속도 × 2 ÷ 3.');
b.getRange('C6').setNumberFormat('0.0'); b.getRange('D8').setNumberFormat('0.000');
brooms.forEach((_,i)=>b.getRange(`B${i+6}`).formulas=[[`=C${i+6}*2/3`]]);
sheet('모자','모자 마나 능력치','기본 능력치 · 특전·버프 제외 · 초당 회복량은 자연 회복 기준',
 ['모자','최대 마나','초당 마나 회복량'],hats.map(x=>[x.displayName,x.maxAp,x.apRecoveryPerSecond]),[26,24,30],
 '출처: EquipmentStatTable.asset. 전투 상태에 따른 회복 중단·배율은 제외.');
wb.recalculate();
console.log((await wb.inspect({kind:'table',range:'마법!A5:G22',include:'values,formulas',tableMaxRows:18,tableMaxCols:7,maxChars:5500})).ndjson);
console.log((await wb.inspect({kind:'match',searchTerm:'#REF!|#DIV/0!|#VALUE!|#NAME\\?|#NUM!',options:{useRegex:true,maxResults:20}})).ndjson);
for(const name of ['마법','빗자루','모자']) {
 const image=await wb.render({sheetName:name,range:name==='마법'?'A1:G24':name==='빗자루'?'A1:E11':'A1:C11',scale:1,format:'png'});
 await fs.writeFile(out+name+'.png',new Uint8Array(await image.arrayBuffer()));
}
await (await SpreadsheetFile.exportXlsx(wb)).save(out+'WitchFlight_능력치_정리.xlsx');
console.log('Exported 17 spells, 3 brooms, 3 hats');
