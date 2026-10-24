import fs from 'node:fs/promises';
import {createReadStream} from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import {spawn} from 'node:child_process';
import {createHash,randomUUID} from 'node:crypto';
import {fileURLToPath} from 'node:url';
import {readImageMetadata,readImageAnimation} from './image-metadata.mjs';
import {validateVideoFile} from './video-decode-probe.mjs';
import {ensureFFmpegRuntime} from './ffmpeg-runtime.mjs';

const hash=bytes=>createHash('sha256').update(bytes).digest('hex');
export const MAX_SOURCE_VIDEO_BYTES=2*1024*1024*1024;
const maxBytes=512*1024*1024;
const demuxer=file=>path.extname(file).toLowerCase()==='.mp4'?'mov':'matroska';
const threads=()=>String(Math.max(1,Math.min(4,os.availableParallelism())));
async function fileHash(file){
 const digest=createHash('sha256');
 for await(const chunk of createReadStream(file))digest.update(chunk);
 return digest.digest('hex');
}
async function inspectReady(file){
 const stat=await fs.lstat(file);
 if(!stat.isFile()||stat.isSymbolicLink()||stat.size<1||stat.size>maxBytes)throw new Error('播放副本必须为非空普通文件，且不能超过 512 MiB。');
 const bytes=await fs.readFile(file),extension=path.extname(file).toLowerCase();
 if(bytes.length!==stat.size)throw new Error('素材正在变化，请等文件保存完再重试。');
 const metadata=readImageMetadata(bytes,extension),animation=readImageAnimation(bytes,extension);
 if(!metadata||!animation?.video)throw new Error('视频需要标准化处理。');
 return {sha256:hash(bytes),bytes:bytes.length,...metadata,...animation};
}
function fraction(value){
 const parts=String(value).split('/').map(Number);
 return parts.length===2?parts[0]/parts[1]:parts[0];
}
export async function inspectSourceVideo(executable,file){
 const output=await new Promise((resolve,reject)=>{
  const args=['-v','error','-protocol_whitelist','file,pipe','-f',demuxer(file),'-select_streams','v:0','-show_entries','stream=codec_name,width,height,avg_frame_rate,r_frame_rate,duration:format=duration','-of','json',file];
  const child=spawn(path.join(path.dirname(executable),'ffprobe.exe'),args,{windowsHide:true,stdio:['ignore','pipe','pipe']});
  let stdout='',stderr='',settled=false;
  const finish=(error)=>{if(settled)return;settled=true;clearTimeout(timer);error?reject(error):resolve(stdout);};
  const timer=setTimeout(()=>{child.kill();finish(new Error('视频信息读取超时，请检查文件是否完整。'));},30000);
  child.stdout.on('data',chunk=>{stdout+=chunk.toString('utf8');if(stdout.length>256*1024){child.kill();finish(new Error('视频信息过大。'));}});
  child.stderr.on('data',chunk=>{stderr=(stderr+chunk.toString('utf8')).slice(-1024);});
  child.on('error',finish);
  child.on('close',code=>finish(code===0?null:new Error('无法读取视频，请确认 MP4/WebM 文件完整。'+stderr)));
 });
 const result=JSON.parse(output),stream=result.streams?.[0];
 const durationSeconds=Number(stream?.duration)||Number(result.format?.duration),fps=fraction(stream?.avg_frame_rate)||fraction(stream?.r_frame_rate);
 if(!stream||!['h264','hevc','av1','vp8','vp9'].includes(stream.codec_name)||
    !Number.isInteger(stream.width)||!Number.isInteger(stream.height)||stream.width<1||stream.height<1||
    stream.width>16384||stream.height>16384||stream.width*stream.height>50_000_000||
    !Number.isFinite(durationSeconds)||durationSeconds<=0||durationSeconds>600||
    !Number.isFinite(fps)||fps<=0||fps>120.01)throw new Error('视频超出支持范围：H.264/H.265/AV1/VP8/VP9，最长 10 分钟、最高 120 FPS、最大 50 MP。');
 return {codec:stream.codec_name,width:stream.width,height:stream.height,durationSeconds,fps};
}
export function conversionProfiles(source){
 const edge=Math.max(source.width,source.height);
 return [...new Set([Math.min(edge,4096),2560,1920].filter(value=>value<=edge))].map(maxEdge=>({maxEdge,fps:Math.min(30,source.fps)}));
}
export function runVideoConversion(executable,input,output,{onProgress=()=>{},maxEdge=1920,fps=30,durationSeconds=600}={}){
 return new Promise((resolve,reject)=>{
  const rate=String(Math.max(100000,Math.min(24_000_000,Math.floor(maxBytes*0.85*8/durationSeconds))));
  const args=['-nostdin','-hide_banner','-loglevel','error','-y','-protocol_whitelist','file,pipe','-threads',threads(),'-f',demuxer(input),'-i',input,'-map','0:v:0','-an','-sn','-dn','-map_metadata','-1','-vf',`scale=w='min(iw,${maxEdge})':h='min(ih,${maxEdge})':force_original_aspect_ratio=decrease:force_divisible_by=2,fps=${fps},format=yuv420p`,'-c:v','libvpx-vp9','-deadline','realtime','-cpu-used','6','-row-mt','1','-threads',threads(),'-b:v',rate,'-maxrate',rate,'-bufsize',String(Number(rate)*2),'-crf','26','-auto-alt-ref','0','-fs',String(maxBytes),'-f','webm',output];
  const child=spawn(executable,args,{windowsHide:true,stdio:['ignore','ignore','pipe'],cwd:path.dirname(executable)});
  let stderr='',settled=false,timedOut=false;
  const timer=setTimeout(()=>{timedOut=true;child.kill();},30*60*1000);
  const progress=setInterval(()=>onProgress(`正在生成 ${maxEdge} 像素兼容副本，原视频不会被修改…`),15000);
  const finish=(error)=>{if(settled)return;settled=true;clearTimeout(timer);clearInterval(progress);error?reject(error):resolve();};
  child.stderr.on('data',chunk=>{stderr=(stderr+chunk.toString('utf8')).slice(-4096);});
  child.on('error',finish);
  child.on('close',code=>{
   if(code!==0){const error=new Error(timedOut?'兼容转换超时，请使用较短的视频后重试。':'生成兼容视频失败，原壁纸已保留。');error.details={stage:'ffmpeg',exitCode:code,stderr};if(!timedOut&&/cannot allocate memory|memory allocation|failed to allocate|not enough memory|error initializing encoder/i.test(stderr))error.code='DREAM_VIDEO_CONVERSION_RESOURCE';finish(error);}else finish();
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
const canReduce=(error,source)=>error.code==='DREAM_VIDEO_UNSUPPORTED'||(['DREAM_VIDEO_TIMEOUT','DREAM_VIDEO_CONVERSION_RESOURCE'].includes(error.code)&&Math.max(source.width,source.height)>1920);
export async function prepareVideoFile(input,stateRoot,{probe=validateVideoFile,ensureRuntime=ensureFFmpegRuntime,convert=runVideoConversion,inspectSource=inspectSourceVideo,onProgress=()=>{}}={}){
 const original=path.resolve(input),state=path.join(stateRoot,'state.json');
 if(!['.mp4','.webm'].includes(path.extname(original).toLowerCase()))return {path:original,converted:false};
 await fs.mkdir(stateRoot,{recursive:true});
 const diagnostic={version:'1.2.0',time:new Date().toISOString(),fileName:path.basename(original),stage:'validate-original',attempts:[]};
 const diagnosticFile=path.join(stateRoot,'last-video-diagnostic.json');
 const save=()=>fs.writeFile(diagnosticFile,JSON.stringify(diagnostic,null,2));
 let executable;
 const runtime=async()=>executable||(executable=await ensureRuntime(stateRoot,{onProgress}));
 try{
  const stat=await fs.lstat(original);
  if(!stat.isFile()||stat.isSymbolicLink()||stat.size<1||stat.size>MAX_SOURCE_VIDEO_BYTES)throw new Error('视频必须为非空普通文件，且不能超过 2 GiB。');
  let source;
  if(stat.size<=maxBytes){try{source=await inspectReady(original);}catch{}}
  if(!source){
   onProgress('正在读取视频信息并准备标准化，支持 4K 和分片 MP4…');
   source={...await inspectSource(await runtime(),original),sha256:await fileHash(original),bytes:stat.size};
  }else{
   onProgress(`正在验证原画质 ${source.width} × ${source.height} 视频…`);
   try{
    diagnostic.decoded=await probe(original,state);
    diagnostic.stage='ready';diagnostic.source=source;diagnostic.converted=false;await save();
    return {path:original,converted:false,width:source.width,height:source.height};
   }catch(error){
    diagnostic.originalFailure={code:error.code||'',message:error.message,details:error.details};
    if(!canReduce(error,source))throw error;
   }
  }
  diagnostic.source={codec:source.codec,width:source.width,height:source.height,bytes:source.bytes,durationSeconds:source.durationSeconds,fps:source.fps};
  const directory=path.join(stateRoot,'compatible-video-v2');
  await fs.mkdir(directory,{recursive:true});
  const profiles=conversionProfiles(source),memo=path.join(directory,source.sha256+'.json');
  let preferred;
  try{preferred=JSON.parse(await fs.readFile(memo,'utf8')).maxEdge;}catch{}
  const preferredIndex=profiles.findIndex(item=>item.maxEdge===preferred);
  if(preferredIndex>0)profiles.splice(0,preferredIndex);
  let lastError;
  for(const profile of profiles){
   const key=hash(source.sha256+'\0'+JSON.stringify(profile)+'\0vp9-crf26-v2'),destination=path.join(directory,key+'.webm');
   let cached=false,metadata;
   try{metadata=await inspectReady(destination);cached=true;}catch{}
   const attempt={maxEdge:profile.maxEdge,cacheUsed:cached};diagnostic.attempts.push(attempt);
   const temporary=path.join(directory,key+'.'+randomUUID()+'.part.webm');
   try{
    if(!cached){
     diagnostic.stage='convert';await save();
     onProgress(`正在生成兼容副本：长边最高 ${profile.maxEdge} 像素 / ${profile.fps} FPS…`);
     await convert(await runtime(),original,temporary,{...profile,durationSeconds:source.durationSeconds,onProgress});
     metadata=await inspectReady(temporary);
     if(metadata.width>profile.maxEdge||metadata.height>profile.maxEdge||metadata.fps>profile.fps+0.1||
        metadata.durationSeconds<source.durationSeconds-Math.max(0.2,2/profile.fps)||metadata.durationSeconds>source.durationSeconds+0.5)throw new Error('兼容副本尺寸、帧率或完整时长校验失败，原壁纸已保留。');
     if(await fileHash(original)!==source.sha256)throw new Error('原视频在转换期间发生变化，请重新选择。');
    }
    diagnostic.stage='verify-compatible';await save();
    diagnostic.decoded=await probe(cached?destination:temporary,state);
    if(!cached)await fs.rename(temporary,destination);
    await fs.writeFile(memo,JSON.stringify({maxEdge:profile.maxEdge}));
    await pruneCopies(directory,destination);
    diagnostic.stage='ready';diagnostic.converted=true;diagnostic.cacheUsed=cached;diagnostic.output={width:metadata.width,height:metadata.height};await save();
    onProgress(`兼容副本 ${metadata.width} × ${metadata.height} 已通过 Codex 解码验证，正在应用。`);
    return {path:destination,converted:true,cacheUsed:cached,width:metadata.width,height:metadata.height};
   }catch(error){
    attempt.failure={code:error.code||'',message:error.message};lastError=error;
    if(!canReduce(error,{width:profile.maxEdge,height:1}))throw error;
    onProgress('当前清晰度无法解码，正在尝试下一级；原壁纸保持不变。');
   }finally{await fs.unlink(temporary).catch(()=>{});}
  }
  throw lastError||new Error('此电脑无法播放兼容副本，原壁纸已保留。');
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
