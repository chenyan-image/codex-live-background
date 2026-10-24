import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {spawn} from 'node:child_process';
import {createHash,randomUUID} from 'node:crypto';
import {fileURLToPath} from 'node:url';
import {readImageMetadata,readImageAnimation} from './image-metadata.mjs';
import {validateVideoFile} from './video-decode-probe.mjs';
import {ensureFFmpegRuntime} from './ffmpeg-runtime.mjs';

const hash=bytes=>createHash('sha256').update(bytes).digest('hex');
const maxBytes=512*1024*1024;
async function inspect(file){
 const stat=await fs.lstat(file);
 if(!stat.isFile()||stat.isSymbolicLink()||stat.size<1||stat.size>maxBytes)throw new Error('视频必须为非空普通文件，且不能超过 512 MiB。');
 const bytes=await fs.readFile(file);
 if(bytes.length!==stat.size)throw new Error('素材正在变化，请等文件保存完再重试。');
 const extension=path.extname(file).toLowerCase(),metadata=readImageMetadata(bytes,extension),animation=readImageAnimation(bytes,extension);
 if(!metadata||!animation?.video)throw new Error('视频结构不符合要求：支持标准 H.264/H.265 MP4 或 VP8/VP9 WebM，最长 10 分钟、最高 120 FPS。');
 return {sha256:hash(bytes),bytes:bytes.length,...metadata,...animation};
}
export function runVideoConversion(executable,input,output,{onProgress=()=>{}}={}){
 return new Promise((resolve,reject)=>{
  const args=['-nostdin','-hide_banner','-loglevel','error','-y','-protocol_whitelist','file,pipe','-threads',String(Math.max(1,Math.min(4,os.availableParallelism()))),'-i',input,'-map','0:v:0','-an','-sn','-dn','-map_metadata','-1','-vf',"scale=w='min(iw,1920)':h='min(ih,1920)':force_original_aspect_ratio=decrease:force_divisible_by=2,fps=30,format=yuv420p",'-c:v','libvpx-vp9','-deadline','realtime','-cpu-used','6','-row-mt','1','-threads',String(Math.max(1,Math.min(4,os.availableParallelism()))),'-b:v','0','-crf','30','-auto-alt-ref','0','-f','webm',output];
  const child=spawn(executable,args,{windowsHide:true,stdio:['ignore','ignore','pipe'],cwd:path.dirname(executable)});
  let stderr='',settled=false,timedOut=false;
  const timer=setTimeout(()=>{timedOut=true;child.kill();},30*60*1000);
  const progress=setInterval(()=>onProgress('正在生成 VP9 兼容副本，请稍候；原视频不会被修改。'),15000);
  const finish=(error)=>{if(settled)return;settled=true;clearTimeout(timer);clearInterval(progress);error?reject(error):resolve();};
  child.stderr.on('data',chunk=>{stderr=(stderr+chunk.toString('utf8')).slice(-4096);});
  child.on('error',finish);
  child.on('close',code=>{
   if(code!==0){const error=new Error(timedOut?'兼容转换超时，请使用较短的视频后重试。':'生成兼容视频失败，原壁纸已保留。');error.details={stage:'ffmpeg',exitCode:code,stderr};finish(error);}else finish();
  });
 });
}
async function pruneCopies(directory,current){
 const candidates=[];
 for(const name of await fs.readdir(directory)){
  if(!/^[a-f0-9]{64}\.webm$/.test(name))continue;
  const full=path.join(directory,name),stat=await fs.lstat(full);
  if(stat.isFile()&&!stat.isSymbolicLink())candidates.push({full,size:stat.size,time:stat.mtimeMs});
 }
 let retained=(await fs.stat(current)).size;
 for(const item of candidates.filter(item=>item.full!==current).sort((a,b)=>b.time-a.time)){
  if(retained+item.size<=maxBytes){retained+=item.size;continue;}
  await fs.unlink(item.full).catch(()=>{});
 }
}
export async function prepareVideoFile(input,stateRoot,{probe=validateVideoFile,ensureRuntime=ensureFFmpegRuntime,convert=runVideoConversion,onProgress=()=>{}}={}){
 const original=path.resolve(input),state=path.join(stateRoot,'state.json');
 if(!['.mp4','.webm'].includes(path.extname(original).toLowerCase()))return {path:original,converted:false};
 await fs.mkdir(stateRoot,{recursive:true});
 const diagnostic={version:'1.1.2',time:new Date().toISOString(),fileName:path.basename(original),stage:'validate-original'};
 const diagnosticFile=path.join(stateRoot,'last-video-diagnostic.json');
 const save=()=>fs.writeFile(diagnosticFile,JSON.stringify(diagnostic,null,2));
 try{
  const source=await inspect(original);
  diagnostic.source={codec:source.codec,width:source.width,height:source.height,bytes:source.bytes,durationSeconds:source.durationSeconds,fps:source.fps};
  try{
   const decoded=await probe(original,state);
   diagnostic.stage='ready';diagnostic.decoded=decoded;diagnostic.converted=false;await save();
   return {path:original,converted:false};
  }catch(error){
   diagnostic.originalFailure={code:error.code||'',message:error.message,details:error.details};
   if(error.code!=='DREAM_VIDEO_UNSUPPORTED')throw error;
  }
  const directory=path.join(stateRoot,'compatible-video-v1');
  await fs.mkdir(directory,{recursive:true});
  const key=hash(source.sha256+'\0vp9-1920-30fps-crf30-v1'),destination=path.join(directory,key+'.webm');
  let cached=false;
  try{await inspect(destination);cached=true;}catch{}
  if(cached){
   diagnostic.stage='verify-cached';await save();
   try{diagnostic.decoded=await probe(destination,state);}catch(error){
    if(error.code!=='DREAM_VIDEO_UNSUPPORTED')throw error;
    diagnostic.cacheFailure={code:error.code,message:error.message,details:error.details};cached=false;
   }
  }
  if(!cached){
   diagnostic.stage='prepare-runtime';await save();
   const executable=await ensureRuntime(stateRoot,{onProgress});
   diagnostic.stage='convert';await save();
   onProgress('当前 Codex 不支持原视频编码，正在自动生成 VP9 兼容副本。');
   const temporary=path.join(directory,key+'.'+randomUUID()+'.part.webm');
   try{
    await convert(executable,original,temporary,{onProgress});
    await inspect(temporary);
    if(hash(await fs.readFile(original))!==source.sha256)throw new Error('原视频在转换期间发生变化，请重新选择。');
    diagnostic.stage='verify-compatible';await save();
    diagnostic.decoded=await probe(temporary,state);
    await fs.rename(temporary,destination);
   }finally{await fs.unlink(temporary).catch(()=>{});}
  }
  await pruneCopies(directory,destination);
  diagnostic.stage='ready';diagnostic.converted=true;diagnostic.cacheUsed=cached;await save();
  onProgress('兼容副本已通过当前 Codex 的真实解码验证，正在应用。');
  return {path:destination,converted:true,cacheUsed:cached};
 }catch(error){
  diagnostic.failure={code:error.code||'',message:error.message,details:error.details};await save().catch(()=>{});
  error.diagnosticPath=diagnosticFile;throw error;
 }
}

if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
 try{
  if(process.argv.length!==3)throw new Error('Usage: prepare-video.mjs <request.json>');
  const request=JSON.parse(await fs.readFile(process.argv[2],'utf8'));
  if(typeof request.media!=='string'||typeof request.stateRoot!=='string'||!path.isAbsolute(request.media)||!path.isAbsolute(request.stateRoot))throw new Error('视频请求参数无效。');
  const result=await prepareVideoFile(request.media,request.stateRoot,{onProgress:message=>console.log('DREAM_PROGRESS:'+message)});
  console.log('DREAM_RESULT:'+JSON.stringify(result));
 }catch(error){console.error(error.message+(error.diagnosticPath?'\n诊断文件：'+error.diagnosticPath:''));process.exitCode=1;}
}
