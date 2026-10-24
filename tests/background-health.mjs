import assert from 'node:assert/strict';
import vm from 'node:vm';
import {rendererBackgroundStatus} from '../engine/scripts/injector.mjs';
let checks=0;
function status(runtime,{visible=true,active=true,reduced=false}={}){
  const context={window:{__CODEX_DREAM_SKIN_STATE__:runtime,matchMedia:()=>({matches:reduced})},document:{visibilityState:visible?'visible':'hidden',hidden:!visible,documentElement:{getAttribute:()=>active?'active':null}}};
  return vm.runInNewContext(`(${rendererBackgroundStatus.toString()})()`,context);
}
const video={paused:false,readyState:3,videoWidth:1920,videoHeight:1080,currentTime:1,getVideoPlaybackQuality:()=>({totalVideoFrames:30})};
const state={mediaType:'video',videoLayer:video};
for(const [expected,runtime,options] of [
  ['not-applied',undefined,{}],['not-applied',state,{active:false}],
  ['image-ready',{mediaType:'image',imageReady:true},{}],
  ['loading',{mediaType:'image',imageReady:false},{}],
  ['error',{mediaType:'image',imageError:'decode failed'},{}],
  ['playing',state,{}],['waiting-visible',state,{visible:false}],
  ['reduced-motion',state,{reduced:true}],
  ['blocked',{...state,videoPlaybackBlocked:true},{}],
  ['error',{...state,videoError:'codec failed'},{}],
  ['paused',{...state,videoLayer:{...video,paused:true}},{}],
  ['loading',{...state,videoLayer:{...video,readyState:1}},{}],
]){const before=JSON.stringify(runtime);assert.equal(status(runtime,options).status,expected);assert.equal(JSON.stringify(runtime),before,'health check must not mutate media');checks++;}
console.log(JSON.stringify({pass:true,checks,connectionDoesNotImplyPlayback:true,readOnly:true}));
