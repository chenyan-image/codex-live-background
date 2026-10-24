import fs from 'node:fs/promises';
import path from 'node:path';
import {createHash, randomUUID} from 'node:crypto';
import {inflateRawSync} from 'node:zlib';
import {fileURLToPath} from 'node:url';

const here=path.dirname(fileURLToPath(import.meta.url));
const sha256=bytes=>createHash('sha256').update(bytes).digest('hex');
// Only the pinned official archive is accepted. No executable is searched for on PATH.
export function extractRuntimeArchive(bytes, config) {
 if(sha256(bytes)!==config.sha256.toLowerCase())throw new Error('兼容组件校验失败，下载文件与已验证版本不一致。');
 let eocd=-1;
 for(let p=bytes.length-22;p>=Math.max(0,bytes.length-65557);p--){if(bytes.readUInt32LE(p)===0x06054b50&&p+22+bytes.readUInt16LE(p+20)===bytes.length){eocd=p;break;}}
 if(eocd<0||bytes.readUInt16LE(eocd+4)!==0||bytes.readUInt16LE(eocd+6)!==0)throw new Error('兼容组件 ZIP 目录无效。');
 const count=bytes.readUInt16LE(eocd+10),end=bytes.readUInt32LE(eocd+16)+bytes.readUInt32LE(eocd+12);
 if(count===65535||end>eocd)throw new Error('兼容组件 ZIP 目录越界。');
 const expected=new Map(config.files.map(item=>[item.archivePath,item]));
 const result=new Map();
 let p=bytes.readUInt32LE(eocd+16);
 for(let i=0;i<count;i++){
  if(p+46>end||bytes.readUInt32LE(p)!==0x02014b50)throw new Error('兼容组件 ZIP 条目无效。');
  const flags=bytes.readUInt16LE(p+8),method=bytes.readUInt16LE(p+10),compressed=bytes.readUInt32LE(p+20),expanded=bytes.readUInt32LE(p+24),nameLength=bytes.readUInt16LE(p+28),extra=bytes.readUInt16LE(p+30),comment=bytes.readUInt16LE(p+32),local=bytes.readUInt32LE(p+42);
  if(p+46+nameLength+extra+comment>end)throw new Error('兼容组件 ZIP 条目越界。');
  const name=bytes.toString('utf8',p+46,p+46+nameLength),item=expected.get(name);
  if(item){
   if(result.has(item.name)||item.name!==path.basename(item.name)||expanded!==item.bytes||expanded>128*1024*1024||(flags&1)||![0,8].includes(method)||local+30>bytes.length||bytes.readUInt32LE(local)!==0x04034b50)throw new Error('兼容组件文件不符合预期。');
   const start=local+30+bytes.readUInt16LE(local+26)+bytes.readUInt16LE(local+28);
   if(start+compressed>bytes.readUInt32LE(eocd+16))throw new Error('兼容组件文件越界。');
   const payload=bytes.subarray(start,start+compressed);
   const data=method===8?inflateRawSync(payload,{maxOutputLength:item.bytes}):Buffer.from(payload);
   if(data.length!==item.bytes)throw new Error('兼容组件文件长度校验失败。');
   result.set(item.name,data);
  }
  p+=46+nameLength+extra+comment;
 }
 if(result.size!==expected.size)throw new Error('兼容组件 ZIP 缺少必需文件。');
 return result;
}

async function readReadyRuntime(directory,config){
 try{
  const marker=JSON.parse(await fs.readFile(path.join(directory,'runtime.json'),'utf8'));
  if(marker.archiveSha256!==config.sha256||marker.files.length!==config.files.length)return null;
  for(const item of config.files){
   const full=path.join(directory,item.name),stat=await fs.lstat(full),record=marker.files.find(entry=>entry.name===item.name);
   if(!stat.isFile()||stat.isSymbolicLink()||stat.size!==item.bytes||!record||sha256(await fs.readFile(full))!==record.sha256)return null;
  }
  return path.join(directory,'ffmpeg.exe');
 }catch{return null;}
}

async function downloadArchive(config,onProgress){
 const controller=new AbortController(),timer=setTimeout(()=>controller.abort(),600000);
 try{
  const response=await fetch(config.url,{signal:controller.signal});
  if(!response.ok)throw new Error('HTTP '+response.status);
  const declared=Number(response.headers.get('content-length'));
  if(declared>config.archiveBytes||declared>100*1024*1024)throw new Error('下载长度不符合预期。');
  const chunks=[];let size=0,lastProgress=0;
  for await(const chunk of response.body){
   size+=chunk.length;
   if(size>config.archiveBytes)throw new Error('下载超过预期长度。');
   chunks.push(chunk);
   if(Date.now()-lastProgress>1500){lastProgress=Date.now();onProgress('正在下载兼容组件：'+Math.floor(size/config.archiveBytes*100)+'%');}
  }
  if(size!==config.archiveBytes)throw new Error('下载不完整。');
  return Buffer.concat(chunks);
 }finally{clearTimeout(timer);}
}

export async function ensureFFmpegRuntime(stateRoot,{onProgress=()=>{}}={}){
 const config=JSON.parse(await fs.readFile(path.join(here,'../assets/ffmpeg-runtime.json'),'utf8'));
 const directory=path.join(stateRoot,'runtime','ffmpeg-'+config.sha256.slice(0,16));
 const ready=await readReadyRuntime(directory,config);
 if(ready)return ready;
 onProgress('此视频需要兼容转换，首次准备解码组件约 74 MiB；视频只在本机处理。');
 const offline=path.join(stateRoot,'runtime-downloads',config.archiveName);
 let bytes;
 try{
  const stat=await fs.lstat(offline);
  if(!stat.isFile()||stat.isSymbolicLink()||stat.size!==config.archiveBytes)throw new Error('离线组件文件大小不匹配。');
  bytes=await fs.readFile(offline);
 }catch(error){
  if(error.code!=='ENOENT')throw error;
  try{bytes=await downloadArchive(config,onProgress);}catch(error){throw new Error('首次兼容转换需要下载组件，但下载未完成（'+String(error.message).slice(0,160)+'）。可按使用说明把官方 ZIP 放入 CodexDreamSkin\\runtime-downloads 后重试。');}
 }
 const files=extractRuntimeArchive(bytes,config);
 const temporary=directory+'.'+randomUUID()+'.tmp';
 await fs.mkdir(temporary,{recursive:true});
 try{
  const records=[];
  for(const [name,data] of files){await fs.writeFile(path.join(temporary,name),data,{flag:'wx'});records.push({name,sha256:sha256(data)});}
  await fs.writeFile(path.join(temporary,'runtime.json'),JSON.stringify({archiveSha256:config.sha256,source:config.url,files:records},null,2),{flag:'wx'});
  if(!await readReadyRuntime(directory,config)){
   let backup=null;
   try{
    const previous=await fs.lstat(directory).catch(error=>{if(error.code==='ENOENT')return null;throw error;});
    if(previous){
     if(!previous.isDirectory()||previous.isSymbolicLink()||path.dirname(directory)!==path.resolve(stateRoot,'runtime'))throw new Error('兼容组件目录异常，已保留原路径。');
     backup=directory+'.invalid-'+randomUUID();
     await fs.rename(directory,backup);
    }
    await fs.rename(temporary,directory);
   }catch(error){
    if(backup&&!await fs.lstat(directory).catch(()=>null))await fs.rename(backup,directory).catch(()=>{});
    if(!await readReadyRuntime(directory,config))throw error;
   }
  }
 }finally{
  // Only files created in this unique directory are removed, never an existing runtime.
  for(const name of [...files.keys(),'runtime.json'])await fs.unlink(path.join(temporary,name)).catch(()=>{});
  await fs.rmdir(temporary).catch(()=>{});
 }
 const executable=await readReadyRuntime(directory,config);
 if(!executable)throw new Error('兼容组件安装验证失败，请保留诊断文件。');
 return executable;
}
