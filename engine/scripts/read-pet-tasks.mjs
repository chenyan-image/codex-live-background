// Local task titles only. Read-only SQLite, no messages, account data or writes.
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
export async function readPetTaskTitles(home,ids){
 if(!Array.isArray(ids)||ids.length>32||ids.some(id=>typeof id!=='string'||id.length>100))throw new Error('Invalid task IDs');
 const files=(await fs.readdir(home)).filter(name=>/^state_\d+\.sqlite$/.test(name)).sort((a,b)=>Number(b.match(/\d+/)[0])-Number(a.match(/\d+/)[0]));
 if(!files.length||!ids.length)return [];
 const {DatabaseSync}=await import('node:sqlite');const database=new DatabaseSync(path.join(home,files[0]),{readOnly:true});
 try{database.exec('PRAGMA query_only=ON; PRAGMA busy_timeout=500;');const statement=database.prepare('SELECT id, substr(title,1,120) AS title FROM threads WHERE id = ?');return ids.map(id=>statement.get(id)).filter(Boolean);}finally{database.close();}
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
 try{let input='';for await(const chunk of process.stdin){input+=chunk;if(input.length>8192)throw new Error('Input too large');}const request=JSON.parse(input);process.stdout.write(JSON.stringify(await readPetTaskTitles(request.home,request.ids))+'\n');}catch{process.stdout.write('[]\n');process.exitCode=1;}
}
