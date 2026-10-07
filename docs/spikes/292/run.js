const {launch}=require('./lib');
const URL='https://ssp.aesv.io/editor';
const out=[];
const rec=(vp,action,ms,loadMs,clicks,found,note,shot)=>{out.push({vp,action,loadMs,actionMs:ms,totalMs:loadMs+ms,clicks,found,note,shot});console.log(JSON.stringify(out.at(-1)));};
async function fresh(b,w,h){
  const p=await b.newPage({viewport:{width:w,height:h}});
  const t0=Date.now();
  await p.goto(URL);
  await p.waitForFunction(()=>document.querySelectorAll('button').length>5,null,{timeout:90000});
  await p.waitForTimeout(1500); // settle after render
  return {p,loadMs:Date.now()-t0};
}
async function drag(p,from,x,y){
  const b=await from.boundingBox();
  await p.mouse.move(b.x+b.width/2,b.y+b.height/2);
  await p.mouse.down();
  await p.mouse.move(b.x+b.width/2+10,b.y+b.height/2+10,{steps:4});
  await p.waitForTimeout(300);
  await p.mouse.move(x,y,{steps:12});
  await p.waitForTimeout(300);
  await p.mouse.up();
  await p.waitForTimeout(500);
}
(async()=>{
 const b=await launch();
 // ---- 1280
 {
  const vp=1280;
  let {p,loadMs}=await fresh(b,vp,900);
  let t=Date.now();
  await p.locator('.dock-close[aria-label="Hide Netlist"]').click();
  await p.locator('.dock-panel[data-panel=text]').waitFor({state:'detached',timeout:5000});
  rec(vp,'hide',Date.now()-t,loadMs,1,'yes','"–" button right of each tab; title tooltip "Hide Netlist"; no text label');
  await p.screenshot({path:`s-1280-hide.png`}); await p.close();

  ({p,loadMs}=await fresh(b,vp,900)); t=Date.now();
  await p.locator('.dock-close[aria-label="Hide Netlist"]').click();
  await p.locator('.dock-open[data-panel=text]').click();
  await p.locator('.dock-panel[data-panel=text]').waitFor({timeout:5000});
  rec(vp,'restore',Date.now()-t,loadMs,2,'yes','"+ Netlist" button appears in the bar under the title; clicks include the hide');
  await p.screenshot({path:`s-1280-restore.png`}); await p.close();

  ({p,loadMs}=await fresh(b,vp,900)); t=Date.now();
  const rb=await p.locator('.dock-panel[data-panel=results]').boundingBox();
  await drag(p,p.locator('.dock-tab[data-panel=schematic]'),rb.x+rb.width/2,rb.y+rb.height/2);
  const same=await p.evaluate(()=>{const s=document.querySelector('.dock-tab[data-panel=schematic]').parentElement.parentElement,r=document.querySelector('.dock-tab[data-panel=results]').parentElement.parentElement;return s===r});
  rec(vp,'tabs (drag Schematic onto Results group)',Date.now()-t,loadMs,1,'no',`one drag gesture; success=${same}; nothing on screen says a tab can be dragged (only a tooltip); drop zones show only during a drag`);
  await p.screenshot({path:`s-1280-tabs.png`}); await p.close();

  ({p,loadMs}=await fresh(b,vp,900)); t=Date.now();
  const sb=await p.locator('.dock-panel[data-panel=schematic]').boundingBox();
  const before=(await p.locator('.dock-panel[data-panel=results]').boundingBox());
  await drag(p,p.locator('.dock-tab[data-panel=results]'),sb.x+sb.width*0.1,sb.y+sb.height/2);
  const after=await p.locator('.dock-panel[data-panel=results]').boundingBox();
  rec(vp,'move to edge (drag Results to left edge of Schematic)',Date.now()-t,loadMs,1,'no',`one drag gesture; results box before ${Math.round(before.x)},${Math.round(before.y)} ${Math.round(before.width)}x${Math.round(before.height)} after ${Math.round(after.x)},${Math.round(after.y)} ${Math.round(after.width)}x${Math.round(after.height)}; same affordance gap as tabs`);
  await p.screenshot({path:`s-1280-move.png`}); await p.close();

  ({p,loadMs}=await fresh(b,vp,900)); t=Date.now();
  await p.locator('.dock-help-toggle').click();
  const txt=await p.locator('.dock-help').innerText();
  rec(vp,'key help',Date.now()-t,loadMs,1,'yes','"?" button under the title, aria-label "Layout help"; help text: '+txt.replace(/\s+/g,' ').slice(0,200));
  await p.screenshot({path:`s-1280-help.png`}); await p.close();
 }
 // ---- 390
 {
  const vp=390;
  let {p,loadMs}=await fresh(b,vp,800);
  const tabs=p.locator('button[aria-pressed][data-panel=text], button[aria-pressed][data-panel=knobs]');
  const vis=async()=>p.evaluate(()=>[...document.querySelectorAll('*')].filter(e=>e.children.length===0&&e.offsetParent&&/Hide|Restore|Reset layout|Layout help/i.test(e.textContent+(e.getAttribute('aria-label')||''))).length);
  console.log('hide/restore/help-like visible controls at 390:',await vis());
  let t=Date.now();
  await p.locator('button[aria-pressed][data-panel=text]').click(); await p.waitForTimeout(500);
  await p.screenshot({path:'s-390-tap-netlist.png'});
  const open1=await p.locator('textarea:visible').count();
  await p.locator('button[aria-pressed][data-panel=text]').click(); await p.waitForTimeout(500);
  const open2=await p.locator('textarea:visible').count();
  await p.screenshot({path:'s-390-tap-netlist-again.png'});
  rec(vp,'hide (tap tab to open, tap again to hide)',Date.now()-t,loadMs,2,'yes (by toggle)',`netlist visible after 1st tap=${open1>0}, after 2nd tap=${open2>0}; tab bar buttons toggle (aria-pressed); no Hide label or button; all panels start hidden so a hide needs an open first`);
  t=Date.now();
  await p.locator('button[aria-pressed][data-panel=text]').click(); await p.waitForTimeout(500);
  const open3=await p.locator('textarea:visible').count();
  rec(vp,'restore (third tap on the same tab)',Date.now()-t,loadMs,3,'yes (by toggle)',`netlist visible again=${open3>0}; clicks counted from load including open and hide`);
  // tabs: drag one tab onto another
  const a=p.locator('button[aria-pressed][data-panel=knobs]'),c=p.locator('button[aria-pressed][data-panel=results]');
  const ab=await a.boundingBox(),cb=await c.boundingBox();
  const drg=await a.getAttribute('draggable');
  rec(vp,'tabs',0,loadMs,0,'no',`phone tab bar is one switcher showing one panel at a time; tab draggable attr=${drg}; no group concept`);
  rec(vp,'move to edge',0,loadMs,0,'no','dock zones and splitters are display:none under 48rem (app.css:227, @media max-width 48rem)');
  rec(vp,'key help',0,loadMs,0,'no','the "?" Layout help button is not rendered in the phone layout (hidden with the dock bar)');
  await p.close();
 }
 require('fs').writeFileSync('result.json',JSON.stringify(out,null,1));
 await b.close();
})().catch(e=>{console.error(e);process.exit(1)});
