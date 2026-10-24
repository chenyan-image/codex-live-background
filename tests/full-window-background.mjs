import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {pathToFileURL} from 'node:url';

// Real Chromium layout/CSS checks in an isolated headless Edge profile.
// No Codex process, installed component or user wallpaper is changed.
const renderer=await fs.readFile(new URL('../engine/assets/renderer-inject.js',import.meta.url),'utf8');
const css=await fs.readFile(new URL('../engine/assets/dream-skin.css',import.meta.url),'utf8');
const temporary=await fs.mkdtemp(path.join(os.tmpdir(),'CodexBackground-Cover-'));
const edge=process.env.EDGE_EXECUTABLE || path.join(process.env['ProgramFiles(x86)'],'Microsoft/Edge/Application/msedge.exe');
const body=`<aside class="app-shell-left-panel">Navigation</aside><main class="main-surface" data-app-shell-main-surface="default"><header class="app-header-tint" data-app-shell-header-edge-scroll>Header</header><div class="thread-scroll-container"><p data-message-author-role="assistant">Readable conversation</p><div class="composer-surface-chrome"><textarea aria-label="Message"></textarea></div></div></main>`;
const testScript=`(async()=>{
 const template=${JSON.stringify(renderer)},css=${JSON.stringify(css)};
 const results=[];
 const wait=ms=>new Promise(resolve=>setTimeout(resolve,ms));
 const check=(condition,message)=>{if(!condition)throw new Error(message)};
 const clear=n=>{const s=getComputedStyle(n);return s.backgroundColor==='rgba(0, 0, 0, 0)'&&s.backgroundImage==='none'};
 const apply=(data,theme)=>{
   const source=template.replace('__DREAM_SKIN_CSS_JSON__',()=>JSON.stringify(css)).replace('__DREAM_SKIN_ART_JSON__',()=>JSON.stringify(data)).replace('__DREAM_SKIN_THEME_JSON__',()=>JSON.stringify(theme)).replace('__DREAM_SKIN_VERSION_JSON__','"2.0.3"').replace('__DREAM_SKIN_STYLE_REVISION_JSON__','"test-css"').replace('__DREAM_SKIN_PAYLOAD_REVISION_JSON__',()=>JSON.stringify(theme.id));
   (0,eval)(source);
 };
 try{
   for(const [shape,w,h] of [['portrait',240,420],['square',320,320],['landscape',640,360]]){
     const canvas=document.createElement('canvas');canvas.width=w;canvas.height=h;const ctx=canvas.getContext('2d');ctx.fillStyle='#257c89';ctx.fillRect(0,0,w,h);ctx.fillStyle='#a5ddba';ctx.fillRect(0,0,w/2,h/2);
     apply(canvas.toDataURL('image/png'),{id:'test-'+shape,appearance:'dark',art:{taskMode:'full',surfaceOpacity:.55},artMetadata:{width:w,height:h,aspect:shape,wide:false}});
     await wait(100);
     const root=document.documentElement,style=getComputedStyle(document.body);
     check(root.getAttribute('data-dream-art-wide')==='true',shape+' did not request window coverage');
     check(style.backgroundImage.includes('blob:')&&style.backgroundSize==='cover'&&style.backgroundRepeat==='no-repeat',shape+' body does not paint one cover image');
     for(const selector of ['aside','main','header'])check(clear(document.querySelector(selector)),shape+' opaque '+selector);
     check(getComputedStyle(document.querySelector('main'),'::before').content==='none',shape+' duplicated conversation artwork');
     const composer=getComputedStyle(document.querySelector('.composer-surface-chrome'));
     check(composer.backgroundColor!=='rgba(0, 0, 0, 0)',shape+' lost readable composer surface');
     check(getComputedStyle(document.querySelector('textarea')).display!=='none',shape+' editor hidden');
     results.push({shape,pass:true,cover:true,shellTransparent:true,composerReadable:true});
   }
   document.getElementById('report').textContent=JSON.stringify({pass:true,results,codexTouched:false,oldWindowTestsRepeated:false});
 }catch(error){document.getElementById('report').textContent=JSON.stringify({pass:false,error:error.stack,results})}
})()`;
const html=`<!doctype html><html><head><meta charset="utf-8"><style>html,body{margin:0;width:100%;height:100%;overflow:hidden}body{background:#121212;color:white}aside{position:fixed;left:0;top:0;bottom:0;width:210px;background:#232323}main{position:absolute;left:210px;right:0;top:0;bottom:0;background:#181818}header{height:40px;background:#222}.thread-scroll-container{height:calc(100% - 40px)}.composer-surface-chrome{position:absolute;left:20px;right:20px;bottom:20px;background:#333}textarea{width:85%;height:55px}#report{display:none}</style></head><body>${body}<pre id="report">pending</pre><script>${testScript.replaceAll('</script','<\\/script')}</script></body></html>`;
const file=path.join(temporary,'fixture.html');await fs.writeFile(file,html);
let output='',errors='';const child=spawn(edge,['--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check','--user-data-dir='+path.join(temporary,'profile'),'--window-size=1100,800','--virtual-time-budget=10000','--dump-dom',pathToFileURL(file).href],{windowsHide:true,stdio:['ignore','pipe','pipe']});
child.stdout.on('data',data=>output+=data);child.stderr.on('data',data=>{errors=(errors+data).slice(-3000)});
const timer=setTimeout(()=>child.kill(),30000);
try{await new Promise((resolve,reject)=>{child.once('error',reject);child.once('close',code=>code===0?resolve():reject(new Error('Headless Edge failed: '+errors)))})}finally{clearTimeout(timer)}
const encoded=/<pre id="report">([\s\S]*?)<\/pre>/.exec(output)?.[1];
assert.ok(encoded&&encoded!=='pending','Browser did not finish the coverage checks');
const report=JSON.parse(encoded.replaceAll('&quot;','"').replaceAll('&lt;','<').replaceAll('&gt;','>').replaceAll('&amp;','&'));
console.log(JSON.stringify(report));assert.equal(report.pass,true,report.error);
