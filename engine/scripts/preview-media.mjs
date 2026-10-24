import fs from 'node:fs/promises';
import path from 'node:path';
import {createHash,randomUUID} from 'node:crypto';
import {spawn} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {ensureFFmpegRuntime} from './ffmpeg-runtime.mjs';
import {readImageMetadata,readImageAnimation} from './image-metadata.mjs';

const profiles=[{width:384,height:216,fps:12,seconds:6,timeout:45000},{width:256,height:144,fps:8,seconds:4,timeout:30000},{width:192,height:108,fps:6,seconds:3,timeout:20000},{width:192,height:108,fps:0,seconds:1,timeout:15000}];
export async function renderPreview(executable,source,output,profile){
 const extension=path.extname(source).toLowerCase(),demuxer=extension==='.mp4'?'mov':extension==='.gif'?'gif':'matroska';
 const args=['-nostdin','-hide_banner','-loglevel','error','-y','-protocol_whitelist','file,pipe','-threads','2','-f',demuxer,'-i',source,'-t',String(profile.seconds),'-an','-sn','-dn'];
 const scale=`scale=${profile.width}:${profile.height}:force_original_aspect_ratio=decrease:flags=bilinear`;
 if(profile.fps)args.push('-filter_complex_threads','2','-filter_complex',`[0:v]fps=${profile.fps},${scale},split[a][b];[a]palettegen=max_colors=96[p];[b][p]paletteuse=dither=bayer`,'-frames:v',String(profile.fps*profile.seconds),'-loop','0','-fs',String(16*1024*1024),'-f','gif',output);
 else args.push('-vf',scale,'-frames:v','1','-f','image2','-update','1',output);
 await new Promise((resolve,reject)=>{
  const child=spawn(executable,args,{windowsHide:true,stdio:['ignore','ignore','pipe']});let stderr='',settled=false;
  const finish=error=>{if(settled)return;settled=true;clearTimeout(timer);error?reject(error):resolve();};
  const timer=setTimeout(()=>{child.kill();finish(new Error('预览生成超时。'));},profile.timeout);
  child.stderr.on('data',chunk=>{stderr=(stderr+chunk.toString('utf8')).slice(-1000);});
  child.on('error',finish);child.on('close',code=>finish(code===0?null:new Error('预览解码失败：'+stderr)));
 });
}
async function inspect(file,extension){
 const stat=await fs.lstat(file);if(!stat.isFile()||stat.isSymbolicLink()||stat.size<1||stat.size>=16*1024*1024)return null;
 const bytes=await fs.readFile(file),metadata=readImageMetadata(bytes,extension);
 if(!metadata||metadata.width>384||metadata.height>216)return null;
 const animation=extension==='.gif'?readImageAnimation(bytes,'.gif'):null;
 if(extension==='.gif'&&(!animation||animation.frameCount>72||bytes.at(-1)!==0x3b))return null;
 return {width:metadata.width,height:metadata.height,animated:extension==='.gif'&&animation.frameCount>1};
}
export async function prepareMediaPreview(input,stateRoot,{ensureRuntime=ensureFFmpegRuntime,onProgress=()=>{},convert=renderPreview}={}){
 const source=path.resolve(input),extension=path.extname(source).toLowerCase();
 if(!['.mp4','.webm','.gif'].includes(extension))return {path:source,animated:false};
 const stat=await fs.lstat(source);
 if(!stat.isFile()||stat.isSymbolicLink()||stat.size<1||stat.size>(extension==='.gif'?512:2048)*1024*1024)throw new Error('预览源文件无效或过大。');
 const directory=path.join(stateRoot,'previews');await fs.mkdir(directory,{recursive:true});
 const key=createHash('sha256').update(source+'\0'+stat.size+'\0'+stat.mtimeMs+'\0preview-fallback-v2').digest('hex');
 for(const ext of ['.gif','.png']){const file=path.join(directory,key+ext);try{const details=await inspect(file,ext);if(details)return {path:file,...details,cacheUsed:true};}catch{}}
 const errors=[],diagnostic=async(stage,error)=>fs.writeFile(path.join(stateRoot,'last-preview-diagnostic.json'),JSON.stringify({version:'1.4.0',time:new Date().toISOString(),stage,sourceExtension:extension,errors,error},null,2));
 let executable;try{executable=await ensureRuntime(stateRoot,{onProgress});}catch(error){await diagnostic('runtime',error.message);throw new Error('预览组件无法准备：'+error.message);}
 for(const [index,profile] of profiles.entries()){
  const ext=profile.fps?'.gif':'.png',destination=path.join(directory,key+ext),temporary=path.join(directory,key+'.'+randomUUID()+'.part'+ext);
  onProgress((index?'预览降级重试：':'正在生成动态预览：')+profile.width+' × '+profile.height+(profile.fps?'，'+profile.fps+' FPS':'，静态缩略图'));
  try{
   await convert(executable,source,temporary,profile);const details=await inspect(temporary,ext);
   if(!details||details.width>profile.width||details.height>profile.height)throw new Error('预览输出校验失败。');
   const current=await fs.stat(source);if(current.size!==stat.size||current.mtimeMs!==stat.mtimeMs){const error=new Error('源文件已改变，请重新选择。');error.code='SOURCE_CHANGED';throw error;}
   await fs.rename(temporary,destination);await diagnostic('ready',null);
   return {path:destination,...details,cacheUsed:false,degraded:index>0,fps:profile.fps,seconds:profile.seconds};
  }catch(error){if(error.code==='SOURCE_CHANGED')throw error;errors.push({width:profile.width,height:profile.height,fps:profile.fps,message:String(error.message).slice(-1000)});}
  finally{await fs.unlink(temporary).catch(()=>{});}
 }
 await diagnostic('failed','All preview profiles failed');throw new Error('预览已降至最低画质仍无法解码，请查看 last-preview-diagnostic.json；实际壁纸将继续验证。');
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
 try{
  const request=JSON.parse(await fs.readFile(process.argv[2],'utf8'));
  if(typeof request.media!=='string'||typeof request.stateRoot!=='string'||!path.isAbsolute(request.media)||!path.isAbsolute(request.stateRoot))throw new Error('预览请求无效。');
  const result=await prepareMediaPreview(request.media,request.stateRoot,{onProgress:message=>console.log('DREAM_PROGRESS:'+message)});
  console.log('DREAM_RESULT:'+JSON.stringify(result));
 }catch(error){console.error(error.message);process.exitCode=1;}
}
