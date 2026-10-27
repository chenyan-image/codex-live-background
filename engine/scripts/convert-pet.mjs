import fs from 'node:fs/promises';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {ensureFFmpegRuntime} from './ffmpeg-runtime.mjs';
import {readImageMetadata} from './image-metadata.mjs';

export async function convertPetAtlas(input,output,stateRoot,{ensureRuntime=ensureFFmpegRuntime}={}){
 const prefix=path.resolve(stateRoot,'pet-imports')+path.sep;
 if(!path.resolve(input).startsWith(prefix)||!path.resolve(output).startsWith(prefix)||path.dirname(input)!==path.dirname(output))throw new Error('宠物转换目录无效。');
 const file=await fs.stat(input);if(file.size>32*1024*1024)throw new Error('图集超过 32 MiB。');
 const meta=readImageMetadata(await fs.readFile(input));
 if(meta.width!==1536||![1872,2288].includes(meta.height))throw new Error('图集必须为 1536×1872 或 1536×2288。');
 const ffmpeg=await ensureRuntime(stateRoot,{onProgress:message=>process.stdout.write('DREAM_PROGRESS:'+message+'\n')});
 await new Promise((resolve,reject)=>{
  const child=spawn(ffmpeg,['-nostdin','-hide_banner','-loglevel','error','-y','-protocol_whitelist','file,pipe','-i',input,'-frames:v','1','-pix_fmt','rgba',output],{windowsHide:true,stdio:['ignore','ignore','pipe']});let errors='',settled=false;
  const finish=error=>{if(settled)return;settled=true;clearTimeout(timer);error?reject(error):resolve();};
  const timer=setTimeout(()=>{child.kill();finish(new Error('宠物图集转换超时。'));},30000);
  child.stderr.on('data',chunk=>{errors=(errors+chunk).slice(-3000);});child.on('error',finish);child.on('close',code=>finish(code?new Error('宠物图集无法读取：'+errors):null));
 });
 if((await fs.stat(output)).size>32*1024*1024)throw new Error('转换后图集过大。');return output;
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
 try{const request=JSON.parse(await fs.readFile(process.argv[2],'utf8'));await convertPetAtlas(request.input,request.output,request.stateRoot);process.stdout.write('DREAM_RESULT:'+JSON.stringify({path:request.output})+'\n');}catch(error){process.stderr.write(error.message+'\n');process.exitCode=1;}
}
