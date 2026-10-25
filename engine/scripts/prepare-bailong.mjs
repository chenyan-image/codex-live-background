import fs from 'node:fs/promises';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {ensureFFmpegRuntime} from './ffmpeg-runtime.mjs';

export async function prepareBailong(request,{ensureRuntime=ensureFFmpegRuntime}={}){
const prefix=path.resolve(request.stateRoot,'pet-imports')+path.sep;
if(!path.resolve(request.stage).startsWith(prefix)||!Array.isArray(request.actions)||request.actions.length<15||request.actions.length>40)throw new Error('白龙准备请求无效。');
const ffmpeg=await ensureRuntime(request.stateRoot,{onProgress:message=>process.stdout.write('DREAM_PROGRESS:'+message+'\n')});
async function run(args){await new Promise((resolve,reject)=>{const process=spawn(ffmpeg,args,{windowsHide:true,stdio:['ignore','ignore','pipe']});let error='',done=false;const finish=value=>{if(done)return;done=true;clearTimeout(timer);value?reject(value):resolve();};const timer=setTimeout(()=>{process.kill();finish(Error('白龙动作转换超时。'));},60000);process.stderr.on('data',data=>{error=(error+data).slice(-2500)});process.on('error',finish);process.on('close',code=>finish(code?Error(error):null));});}
const actions=[];
for(const [index,action] of request.actions.entries()){
 if(path.basename(action.file)!==action.file||!action.file.endsWith('.gif')||path.extname(action.input).toLowerCase()!=='.webp')throw new Error('动作路径无效。');
 process.stdout.write('DREAM_PROGRESS:正在准备白龙完整动作 '+(index+1)+'/'+request.actions.length+' · '+action.name+'\n');
 const output=path.join(request.stage,action.file);
 await run(['-nostdin','-hide_banner','-loglevel','error','-y','-threads','2','-i',action.input,'-filter_complex_threads','2','-filter_complex','[0:v]fps=12,scale=224:256:force_original_aspect_ratio=decrease:flags=lanczos,pad=224:256:(ow-iw)/2:oh-ih:color=black@0,split[a][b];[a]palettegen=reserve_transparent=1:stats_mode=diff[p];[b][p]paletteuse=alpha_threshold=100:dither=bayer','-an','-loop','0',output]);
 actions.push({id:action.id,name:action.name,file:action.file,durationMs:action.durationMs,frames:Math.max(1,Math.round(action.durationMs/1000*12))});
}
await run(['-nostdin','-hide_banner','-loglevel','error','-y','-i',path.join(request.stage,actions[0].file),'-frames:v','1',path.join(request.stage,'poster.png')]);
const find=id=>Math.max(0,actions.findIndex(action=>action.id===id));
await fs.writeFile(path.join(request.stage,'pet.json'),JSON.stringify({id:'bailong',displayName:'白龙娘',spritesheetPath:'poster.png',format:'animated-gif-set',actions,rowMap:[0,find('landing'),find('landing'),find('01'),find('landing'),find('13'),find('idle2'),find('work01'),find('work05')]},null,2));
await fs.writeFile(path.join(request.stage,'source.json'),JSON.stringify({author:'jingh12312-rgb; Wunsch2048',source:'https://github.com/jingh12312-rgb/bailong-desktop-pet',enhancedSource:'https://github.com/Wunsch2048/codex-bailong-desktop-pet',license:'Loaded personally from the original author material. No original white-dragon artwork is bundled in CodexHub distribution.',modification:'Local 12 FPS GIF playback copies at 224x256. All action clips and their full duration retained.'},null,2));
process.stdout.write('DREAM_RESULT:'+JSON.stringify({actions:actions.length})+'\n');
return {actions:actions.length};
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
 try{await prepareBailong(JSON.parse(await fs.readFile(process.argv[2],'utf8')));}catch(error){process.stderr.write(error.message+'\n');process.exitCode=1;}
}
