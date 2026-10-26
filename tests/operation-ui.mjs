import assert from 'node:assert/strict';
import vm from 'node:vm';
import {applyLoadedToSession, operationUiExpression} from '../engine/scripts/injector.mjs';

// Isolated renderer fixture. Never opens a CDP endpoint, Codex, or user files.
const hostId='chatgpt-dream-skin-operation';
const registryKey='__CHATGPT_DREAM_SKIN_OPERATION_UI__';
let checks=0;
const check=(condition,message)=>{checks++;assert.ok(condition,message)};
function fixture({host=true,registry=true,head=true}={}){
  const nodes=new Map(),timers=new Map(),cleared=[],created=[];
  const make=(id,textContent='')=>{
    const node={id,textContent,remove(){nodes.delete(this.id);this.removed=true}};
    nodes.set(id,node);return node;
  };
  const wallpaper=make('codex-dream-skin-style','body{background-image:url(blob:existing)}');
  const composer=make('composer'),conversation=make('conversation');
  let oldHost,shadowStyle;
  if(host){oldHost=make(hostId);oldHost.dataset={operationToken:'newer-token'};shadowStyle={removed:false,remove(){this.removed=true}};oldHost.shadowRoot={querySelectorAll:()=>[shadowStyle]};make(hostId+'-style','legacy');make(hostId+'-styles','legacy');}
  const activeSkin={revision:'existing',imageReady:true};
  const window={__CODEX_DREAM_SKIN_STATE__:activeSkin};
  if(registry){window[registryKey]={token:'newer-token',startedAt:1,showTimer:0,expiryTimer:3,terminalTimer:9,retryTimer:12};for(const id of [0,3,9,12])timers.set(id,()=>make(hostId));}
  const parent={append(node){nodes.set(node.id,node)}};
  const document={getElementById:id=>nodes.get(id)||null,head:head?parent:null,documentElement:parent,createElement(tag){created.push(tag);assert.equal(tag,'style','Progress host must not be recreated');return {id:'',textContent:''}}};
  const context={window,document,clearTimeout(id){cleared.push(id);timers.delete(id)},clearInterval(id){timers.delete(id)},setTimeout(){throw new Error('Progress timer recreated')},setInterval(){throw new Error('Progress interval recreated')}};
  return {nodes,timers,cleared,created,oldHost,shadowStyle,window,activeSkin,wallpaper,composer,conversation,run:expression=>vm.runInNewContext(expression,context)};
}

for(const [action,state] of [['show','loading'],['update','loading'],['update','success'],['update','error'],['update','cancelled'],['hide','loading'],['clear','loading']]){
  const page=fixture();
  const result=page.run(operationUiExpression(action,'older-token',state,'正在应用「纸上微光」…'));
  check(result.cleared===true&&!result.visible&&result.disabled===true,action+' must retain a successful begin/finish response without a popup');
  check(!page.nodes.has(hostId)&&page.shadowStyle.removed,action+' left the legacy host or shadow style');
  check(!page.nodes.has(hostId+'-style')&&!page.nodes.has(hostId+'-styles'),action+' left legacy global styles');
  check(page.timers.size===0&&[0,3,9,12].every(id=>page.cleared.includes(id)),action+' did not cancel all old registry timers');
  check(!(registryKey in page.window),action+' left old operation registry');
  check(page.nodes.get('codex-dream-skin-style')===page.wallpaper&&page.wallpaper.textContent.includes('blob:existing')&&page.window.__CODEX_DREAM_SKIN_STATE__===page.activeSkin,'Wallpaper was modified by HUD cleanup');
  check(page.nodes.get('composer')===page.composer&&page.nodes.get('conversation')===page.conversation,'Composer/conversation was modified');
  const guard=page.nodes.get(hostId+'-suppression');
  check(guard?.textContent==='#'+hostId+'{display:none!important;visibility:hidden!important;pointer-events:none!important}','Missing narrowly scoped guard against an already-running old injector');
  page.run(operationUiExpression('show','future-token','loading','another operation'));
  check(page.created.length===1&&page.nodes.get(hostId+'-suppression')===guard&&!page.nodes.has(hostId),'Repeated operations recreated a popup or duplicate guard');
}

for(const options of [{host:false,registry:false},{host:false},{registry:false},{head:false}]){
  const page=fixture(options),result=page.run(operationUiExpression('clear',''));
  check(result.cleared&&!page.nodes.has(hostId),'Missing/expired legacy UI cleanup must be safe');
}

const payload='WALLPAPER_PAYLOAD_UNCHANGED';
{
  const page=fixture(),calls=[];
  const session={closed:false,async evaluate(expression,options){calls.push({expression,options});return expression===payload?true:page.run(expression)}};
  await applyLoadedToSession(session,{theme:{},payload,mediaFilePath:null});
  check(calls.length===2&&calls[1].expression===payload&&!page.nodes.has(hostId),'Cleanup must precede the unchanged wallpaper payload');
  check(calls[0].options?.timeoutMs===1500,'Cleanup budget must be passed as evaluate options, not ignored as a numeric argument');
}
{
  const page=fixture(),session={closed:false,async evaluate(expression){if(expression===payload)throw new Error('original renderer application failed');return page.run(expression)}};
  checks++;await assert.rejects(applyLoadedToSession(session,{theme:{},payload,mediaFilePath:null}),/original renderer application failed/);
  check(!page.nodes.has(hostId),'Application failure left progress UI behind');
}
{
  let applied=false,logged=false;const previous=console.error;
  console.error=()=>{logged=true};
  try{
    const session={closed:false,async evaluate(expression){if(expression!==payload)throw new Error('cleanup unavailable');applied=true;return true}};
    await applyLoadedToSession(session,{theme:{},payload,mediaFilePath:null});
  }finally{console.error=previous}
  check(applied&&logged,'Best-effort cleanup must not turn a successful wallpaper application into failure');
}

console.log(JSON.stringify({pass:true,checks,progressPopupDisabled:true,legacyTimersAndStylesCleared:true,oldInjectorSuppressed:true,wallpaperAndComposerPreserved:true,applicationErrorsPreserved:true,codexTouched:false}));
