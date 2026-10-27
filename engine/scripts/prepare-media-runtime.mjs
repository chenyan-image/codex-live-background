import path from 'node:path';
import {ensureFFmpegRuntime} from './ffmpeg-runtime.mjs';
const state=path.resolve(process.argv[2]||'');
if(!process.argv[2])throw new Error('Missing state directory');
const runtime=await ensureFFmpegRuntime(state,{onProgress:message=>console.log('DREAM_PROGRESS:'+message)});
console.log(JSON.stringify({ready:true,path:runtime}));
