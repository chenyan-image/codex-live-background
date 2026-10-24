import fs from 'node:fs/promises';
import path from 'node:path';
import {createHash,randomUUID} from 'node:crypto';
import {spawn} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {ensureFFmpegRuntime} from './ffmpeg-runtime.mjs';
import {readImageMetadata,readImageAnimation} from './image-metadata.mjs';

export async function prepareMediaPreview(input,stateRoot,{ensureRuntime=ensureFFmpegRuntime,onProgress=()=>{}}={}){
 const source=path.resolve(input),extension=path.extname(source).toLowerCase();
 if(!['.mp4','.webm','.gif'].includes(extension))return {path:source,animated:false};
 const stat=await fs.lstat(source);
 if(!stat.isFile()||stat.isSymbolicLink()||stat.size<1||stat.size>(extension==='.gif'?512:2048)*1024*1024)throw new Error('预览源文件无效或过大。');
 const directory=path.join(stateRoot,'previews');await fs.mkdir(directory,{recursive:true});
 const key=createHash('sha256').update(source+'\0'+stat.size+'\0'+stat.mtimeMs+'\0gif-384-12-6s-v1').digest('hex');
 const destination=path.join(directory,key+'.gif');
 try{const cached=await fs.readFile(destination);if(cached.length<16*1024*1024&&readImageMetadata(cached,'.gif')&&readImageAnimation(cached,'.gif'))return {path:destination,animated:true,cacheUsed:true};}catch{}
 const executable=await ensureRuntime(stateRoot,{onProgress}),temporary=path.join(directory,key+'.'+randomUUID()+'.part.gif');
 onProgress('正在生成动态预览（最长 6 秒）…');
 try{
  await new Promise((resolve,reject)=>{
   const demuxer=extension==='.mp4'?'mov':extension==='.gif'?'gif':'matroska';
   const args=['-nostdin','-hide_banner','-loglevel','error','-y','-protocol_whitelist','file,pipe','-threads','2','-f',demuxer,'-i',source,'-t','6','-an','-sn','-dn','-filter_complex_threads','2','-filter_complex','[0:v]fps=12,scale=384:216:force_original_aspect_ratio=decrease:flags=bilinear,split[a][b];[a]palettegen=max_colors=128[p];[b][p]paletteuse=dither=bayer','-frames:v','72','-loop','0','-fs',String(16*1024*1024),'-f','gif',temporary];
   const child=spawn(executable,args,{windowsHide:true,stdio:['ignore','ignore','pipe']});
   let stderr='',settled=false;const timer=setTimeout(()=>{child.kill();finish(new Error('预览生成超时。'));},90000);
   const finish=error=>{if(settled)return;settled=true;clearTimeout(timer);error?reject(error):resolve();};
   child.stderr.on('data',chunk=>{stderr=(stderr+chunk.toString('utf8')).slice(-1500);});
   child.on('error',finish);child.on('close',code=>finish(code===0?null:new Error('无法生成预览：'+stderr)));
  });
  const bytes=await fs.readFile(temporary),metadata=readImageMetadata(bytes,'.gif'),animation=readImageAnimation(bytes,'.gif');
  if(bytes.length>=16*1024*1024||bytes.at(-1)!==0x3b||!metadata||!animation||animation.frameCount>72||metadata.width>384||metadata.height>216)throw new Error('预览输出校验失败。');
  const current=await fs.stat(source);if(current.size!==stat.size||current.mtimeMs!==stat.mtimeMs)throw new Error('源文件已改变，请重新选择。');
  await fs.rename(temporary,destination);
  return {path:destination,animated:true,cacheUsed:false};
 }finally{await fs.unlink(temporary).catch(()=>{});}
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
 try{
  const request=JSON.parse(await fs.readFile(process.argv[2],'utf8'));
  if(typeof request.media!=='string'||typeof request.stateRoot!=='string'||!path.isAbsolute(request.media)||!path.isAbsolute(request.stateRoot))throw new Error('预览请求无效。');
  const result=await prepareMediaPreview(request.media,request.stateRoot,{onProgress:message=>console.log('DREAM_PROGRESS:'+message)});
  console.log('DREAM_RESULT:'+JSON.stringify(result));
 }catch(error){console.error(error.message);process.exitCode=1;}
}
