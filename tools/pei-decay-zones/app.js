'use strict';
const canvas=document.querySelector('#map'),ctx=canvas.getContext('2d'),zone=document.querySelector('#zone'),status=document.querySelector('#status'),progress=document.querySelector('#progress');
let data, image=new Image(), polygons={};
function worldToPixel(x,z,v,w,h){return {x:((x-Number(v.position.x))/Number(v.scale.x)+0.5)*w,y:(0.5-(z-Number(v.position.z))/Number(v.scale.z))*h};}
function pixelToWorld(x,y,v,w,h){return {x:(x/w-0.5)*Number(v.scale.x)+Number(v.position.x),z:(0.5-y/h)*Number(v.scale.z)+Number(v.position.z)};}
function validatePolygon(p){
 if(p.length<3||p.length>256)throw Error('Для полигона нужно от 3 до 256 вершин');
 const cross=(a,b,c)=>(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
 const on=(a,b,c)=>cross(a,b,c)===0&&c.x>=Math.min(a.x,b.x)&&c.x<=Math.max(a.x,b.x)&&c.y>=Math.min(a.y,b.y)&&c.y<=Math.max(a.y,b.y);
 let area=0;
 for(let i=0;i<p.length;i++){
  const a=p[i],b=p[(i+1)%p.length];
  for(let j=i+1;j<p.length;j++)if(a.x===p[j].x&&a.y===p[j].y)throw Error('Повторная вершина полигона');
  area+=a.x*b.y-b.x*a.y;
  for(let j=i+1;j<p.length;j++){
   if(j===i+1||(i===0&&j===p.length-1))continue;
   const c=p[j],d=p[(j+1)%p.length],ab1=cross(a,b,c),ab2=cross(a,b,d),cd1=cross(c,d,a),cd2=cross(c,d,b);
   if(((ab1>0&&ab2<0)||(ab1<0&&ab2>0))&&((cd1>0&&cd2<0)||(cd1<0&&cd2>0))||on(a,b,c)||on(a,b,d)||on(c,d,a)||on(c,d,b))throw Error('Самопересечение полигона');
  }
 }
 if(Math.abs(area)<1e-8)throw Error('Площадь полигона равна нулю');
}
function validateDraft(doc,current,w,h){
 if(!doc||doc.status!=='DRAFT_NOT_FOR_AUTOMATIC_APPLICATION'||doc.source_sha256!==current.source_sha256||doc.chart_sha256!==current.chart_sha256||!doc.cartography||doc.cartography.sha256!==current.cartography.sha256)throw Error('Статус или хеши карты не совпадают');
 if(!Array.isArray(doc.zones)||doc.zones.length!==current.locations.length)throw Error('Неполный список зон');
 const expected=new Map(current.locations.map(l=>[l.instance_id,l.name])),seen=new Set(),result={};
 for(const z of doc.zones){if(!z||!Number.isSafeInteger(z.instance_id)||seen.has(z.instance_id)||expected.get(z.instance_id)!==z.name||!Array.isArray(z.vertices)||z.vertices.length<3||z.vertices.length>256)throw Error('Некорректные ID, имена или вершины');seen.add(z.instance_id);result[z.instance_id]=z.vertices.map(p=>{if(!p||typeof p.x!=='number'||typeof p.z!=='number'||!Number.isFinite(p.x)||!Number.isFinite(p.z))throw Error('Некорректные координаты');const pixel=worldToPixel(p.x,p.z,current.cartography,w,h);if(!Number.isFinite(pixel.x)||!Number.isFinite(pixel.y)||pixel.x<0||pixel.x>w||pixel.y<0||pixel.y>h)throw Error('Вершина вне карты');return pixel;});validatePolygon(result[z.instance_id]);}
 if(seen.size!==expected.size)throw Error('Неполный набор ID');return result;
}
function render(){ctx.drawImage(image,0,0);for(const [id,points] of Object.entries(polygons)){if(!points.length)continue;ctx.beginPath();points.forEach((p,i)=>i?ctx.lineTo(p.x,p.y):ctx.moveTo(p.x,p.y));if(points.length>2)ctx.closePath();ctx.strokeStyle=id===zone.value?'#f22':'#0af';ctx.lineWidth=3;ctx.stroke();}for(const l of data.locations){const p=worldToPixel(l.x,l.z,data.cartography,canvas.width,canvas.height);ctx.fillStyle='#ff0';ctx.beginPath();ctx.arc(p.x,p.y,5,0,Math.PI*2);ctx.fill();}progress.textContent=`Полигонов с 3+ вершинами: ${Object.values(polygons).filter(p=>p.length>=3).length} / ${data.locations.length}`;}
canvas.onclick=e=>{const r=canvas.getBoundingClientRect(),p={x:(e.clientX-r.left)*canvas.width/r.width,y:(e.clientY-r.top)*canvas.height/r.height};(polygons[zone.value]??=[]).push(p);render();};
document.querySelector('#undo').onclick=()=>{polygons[zone.value]?.pop();render();};document.querySelector('#clear').onclick=()=>{polygons[zone.value]=[];render();};zone.onchange=render;
document.querySelector('#export').onclick=()=>{if(!data||!image.complete||!canvas.width){alert('Карта ещё не загружена.');return;}try{for(const l of data.locations)try{validatePolygon(polygons[l.instance_id]||[]);}catch(e){throw Error(l.name+': '+e.message);}}catch(e){alert(e.message);return;}const zones=data.locations.map(l=>({instance_id:l.instance_id,name:l.name,vertices:polygons[l.instance_id].map(p=>pixelToWorld(p.x,p.y,data.cartography,canvas.width,canvas.height))}));const blob=new Blob([JSON.stringify({status:'DRAFT_NOT_FOR_AUTOMATIC_APPLICATION',source_sha256:data.source_sha256,chart_sha256:data.chart_sha256,cartography:data.cartography,zones},null,2)],{type:'application/json'});let a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download='pei-zones-DRAFT.json';a.click();URL.revokeObjectURL(a.href);};
document.querySelector('#import').onchange=async e=>{const f=e.target.files[0];if(!f)return;try{if(!data||!canvas.width)throw Error('Карта ещё не загружена');if(f.size>5000000)throw Error('Файл слишком велик');const doc=JSON.parse(await f.text()),next=validateDraft(doc,data,canvas.width,canvas.height);polygons=next;render();status.textContent='Черновик загружен; игровая проверка обязательна.';}catch(err){status.textContent='Импорт отклонён: '+err.message;}finally{e.target.value='';}};
fetch('/data').then(r=>{if(!r.ok)throw Error('Ошибка чтения карты или неподдерживаемая проекция');return r.json();}).then(d=>{data=d;for(const l of d.locations){const o=document.createElement('option');o.value=l.instance_id;o.textContent=l.name+(l.visible_on_map?'':' (скрыто)');zone.appendChild(o);}image.onload=()=>{canvas.width=image.naturalWidth;canvas.height=image.naturalHeight;status.textContent='Проекция CartographyVolume; геометрия и изображение требуют игровой проверки.';render();};image.onerror=()=>status.textContent='Не удалось загрузить изображение.';image.src='/chart';}).catch(e=>status.textContent=e.message);
