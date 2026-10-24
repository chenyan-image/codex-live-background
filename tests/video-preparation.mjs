import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawnSync} from 'node:child_process';
import {createHash} from 'node:crypto';
import {prepareVideoFile,runVideoConversion,inspectSourceVideo} from '../engine/scripts/prepare-video.mjs';
import {readImageMetadata,readImageAnimation} from '../engine/scripts/image-metadata.mjs';
import {createVideoDecodeError} from '../engine/scripts/video-decode-probe.mjs';

const here=path.dirname(fileURLToPath(import.meta.url));
await fs.mkdir(path.join(here,'output'),{recursive:true});
const directory=await fs.mkdtemp(path.join(here,'output','4k-'));
const original=path.join(here,'fixtures/4k.webm');
const results=[];
const unsupported=()=>createVideoDecodeError({reason:'decode-error',mediaError:4});
const originalHash=createHash('sha256').update(await fs.readFile(original)).digest('hex');
const metadata=async file=>readImageMetadata(await fs.readFile(file),path.extname(file));
async function test(name,action){await action();results.push(name);}
await test('native 4K does not download or transcode',async()=>{
 const result=await prepareVideoFile(original,path.join(directory,'native'),{
  probe:async file=>{assert.equal(file,original);assert.equal((await metadata(file)).width,3840);return {pass:true};},
  ensureRuntime:async()=>{throw new Error('must not download');}
 });
 assert.equal(result.path,original);assert.equal(result.converted,false);
});
for(const [reason,errorName] of [['playback-blocked','NotAllowedError'],['window-hidden',''],['playback-interrupted','AbortError']]){
 await test(`${reason} never transcodes`,async()=>{
  let runtime=false;
  await assert.rejects(prepareVideoFile(original,path.join(directory,reason),{
   probe:async()=>{throw createVideoDecodeError({reason,errorName});},
   ensureRuntime:async()=>{runtime=true;throw new Error('must not download');}
  }));
  assert.equal(runtime,false);
 });
}
const executable=process.env.DREAM_TEST_FFMPEG;
if(executable){
 await test('insufficient 4K encoder resources can fall back to 1080p',async()=>{
  const result=await prepareVideoFile(original,path.join(directory,'resource-limit'),{ensureRuntime:async()=>executable,
   probe:async file=>{if(file===original)throw unsupported();return {pass:true};},
   convert:async(exe,input,output,options)=>{if(options.maxEdge>1920){const error=new Error('Insufficient encoder memory');error.code='DREAM_VIDEO_CONVERSION_RESOURCE';throw error;}return runVideoConversion(exe,input,output,options);}
  });assert.equal(result.width,1920);
 });
 for(const target of [3840,2560,1920])await test(`real conversion falls back to ${target} and caches the successful profile`,async()=>{
  const state=path.join(directory,String(target));let conversions=0;
  const options={ensureRuntime:async()=>executable,
   convert:async(...args)=>{conversions++;return runVideoConversion(...args);},
   probe:async file=>{if(file===original)throw unsupported();const m=await metadata(file);if(m.width>target)throw unsupported();return {pass:true,width:m.width,height:m.height};}
  };
  const result=await prepareVideoFile(original,state,options);
  assert.equal(result.width,target);assert.equal(result.converted,true);
  const animation=readImageAnimation(await fs.readFile(result.path),'.webm');assert.equal(animation.durationSeconds,.5);assert.equal(animation.codec,'vp9');
  const before=conversions;const repeat=await prepareVideoFile(original,state,options);
  assert.equal(repeat.cacheUsed,true);assert.equal(conversions,before);
  assert.equal(createHash('sha256').update(await fs.readFile(original)).digest('hex'),originalHash);
 });
 await test('fragmented MP4 is normalized instead of rejected by the strict parser',async()=>{
  const input=path.join(directory,'fragmented.mp4');
  const command=spawnSync(executable,['-nostdin','-hide_banner','-loglevel','error','-y','-i',original,'-c','copy','-movflags','frag_keyframe+empty_moov',input],{windowsHide:true,encoding:'utf8'});
  assert.equal(command.status,0,command.stderr);assert.equal(await metadata(input),null);
  const source=await inspectSourceVideo(executable,input);assert.equal(source.width,3840);
  const result=await prepareVideoFile(input,path.join(directory,'normalized'),{ensureRuntime:async()=>executable,probe:async file=>{assert.equal((await metadata(file)).width,3840);return {pass:true};}});
  assert.equal(result.converted,true);assert.equal(result.width,3840);
 });
 await test('truncated conversion is never accepted',async()=>{
  await assert.rejects(prepareVideoFile(original,path.join(directory,'truncated'),{ensureRuntime:async()=>executable,probe:async()=>{throw unsupported();},
   convert:async(exe,input,output)=>{const result=spawnSync(exe,['-nostdin','-hide_banner','-loglevel','error','-y','-i',input,'-t','0.05','-c','copy',output],{windowsHide:true,encoding:'utf8'});assert.equal(result.status,0,result.stderr);}
  }),/完整时长/);
 });
 await test('corrupt MP4 cannot become a valid background',async()=>{
  const input=path.join(directory,'corrupt.mp4');await fs.writeFile(input,'invalid mp4');
  await assert.rejects(prepareVideoFile(input,path.join(directory,'corrupt'),{ensureRuntime:async()=>executable,probe:async()=>{throw new Error('must not probe');}}),/无法读取视频/);
 });
 await test('failed fallback leaves no partial files and preserves the active theme',async()=>{
  const state=path.join(directory,'all-fail');await fs.mkdir(state);await fs.writeFile(path.join(state,'active-theme.json'),'original');
  await assert.rejects(prepareVideoFile(original,state,{ensureRuntime:async()=>executable,probe:async()=>{throw unsupported();}}));
  assert.equal(await fs.readFile(path.join(state,'active-theme.json'),'utf8'),'original');
  assert.equal((await fs.readdir(path.join(state,'compatible-video-v2'))).filter(x=>x.includes('.part.')).length,0);
 });
}
const report={pass:true,count:results.length,realFFmpeg:Boolean(executable),results};
await fs.writeFile(path.join(directory,'results.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({...report,directory}));
