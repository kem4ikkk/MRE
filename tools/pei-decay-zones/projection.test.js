'use strict';
const fs=require('fs'),vm=require('vm'),path=require('path');
const source=fs.readFileSync(path.join(__dirname,'app.js'),'utf8');
vm.runInThisContext(source.slice(source.indexOf('function worldToPixel'),source.indexOf('function render')));
const v={position:{x:'-4.718623',z:'8.407498'},scale:{x:'1922.711',z:'1926.264'}};
const centre=worldToPixel(-4.718623,8.407498,v,1923,1927);
if(centre.x!==961.5||centre.y!==963.5)throw Error('centre');
const corner=worldToPixel(-4.718623+1922.711/2,8.407498+1926.264/2,v,1923,1927);
if(corner.x!==1923||corner.y!==0)throw Error('corner');
for(const [x,z] of [[0,0],[200,-700],[-900,900]]){const p=worldToPixel(x,z,v,1923,1927),w=pixelToWorld(p.x,p.y,v,1923,1927);if(Math.abs(w.x-x)>1e-9||Math.abs(w.z-z)>1e-9)throw Error('roundtrip');}
v.sha256='volume';const current={source_sha256:'map',chart_sha256:'chart',cartography:v,locations:[{instance_id:1,name:'A'},{instance_id:2,name:'B'}]};
const valid={status:'DRAFT_NOT_FOR_AUTOMATIC_APPLICATION',source_sha256:'map',chart_sha256:'chart',cartography:{sha256:'volume'},zones:current.locations.map(l=>({instance_id:l.instance_id,name:l.name,vertices:[{x:0,z:0},{x:1,z:0},{x:1,z:1}]}))};
if(Object.keys(validateDraft(valid,current,1923,1927)).length!==2)throw Error('valid import');
for(const points of [
 [{x:0,y:0},{x:3,y:0},{x:3,y:3},{x:0,y:3}],
 [{x:0,y:0},{x:1,y:0},{x:3,y:0},{x:3,y:3},{x:0,y:3}],
 [{x:0,y:0},{x:3,y:0},{x:1,y:1},{x:0,y:3}]
])validatePolygon(points);
for(const points of [
 [{x:0,y:0},{x:3,y:3},{x:0,y:3},{x:3,y:0}],
 [{x:0,y:0},{x:3,y:0},{x:1,y:0},{x:3,y:3},{x:0,y:3}],
 [{x:0,y:0},{x:1,y:0},{x:2,y:0}],
 [{x:0,y:0},{x:1,y:0},{x:0,y:0},{x:0,y:1}]
]){let failed=false;try{validatePolygon(points);}catch{failed=true;}if(!failed)throw Error('invalid polygon accepted');}
for(const mutate of [d=>d.status='wrong',d=>d.source_sha256='other',d=>d.chart_sha256='other',d=>d.cartography.sha256='other',d=>d.zones.pop(),d=>d.zones[1].instance_id=1,d=>d.zones[0].name='B',d=>d.zones[0].vertices[0].x=Infinity,d=>d.zones[0].vertices[0].x=1e9,d=>d.zones[0].vertices.pop()]){let d=structuredClone(valid);mutate(d);let failed=false;try{validateDraft(d,current,1923,1927);}catch{failed=true;}if(!failed)throw Error('invalid import accepted');}
console.log('PASS: projection, valid import, mismatch, duplicate, nonfinite, bounds and incomplete geometry');

const multi=structuredClone(valid);
multi.zones[0].polygons=[multi.zones[0].vertices,[{x:10,z:10},{x:11,z:10},{x:11,z:11}]];
delete multi.zones[0].vertices;
const imported=validateDraft(multi,current,1923,1927);
if(imported[1].length!==2||imported[2].length!==1)throw Error('multi and legacy import');
for(const mutate of [d=>d.zones[0].polygons[1].pop(),d=>d.zones[0].polygons=[],d=>d.zones[0].vertices=[],d=>d.zones[0].polygons[1][0].x=Infinity]){
 const d=structuredClone(multi);mutate(d);let failed=false;try{validateDraft(d,current,1923,1927);}catch{failed=true;}if(!failed)throw Error('invalid multipolygon accepted');
}
console.log('PASS: multiple polygons, legacy import and invalid multipolygon');
