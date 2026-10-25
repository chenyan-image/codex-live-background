import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawnSync} from 'node:child_process';
import {createHash} from 'node:crypto';
import {MAX_THEME_NAME_CODE_POINTS} from '../engine/assets/theme-package-validator.mjs';
import {loadTheme} from '../engine/scripts/injector.mjs';

const repo=fileURLToPath(new URL('../',import.meta.url));
const output=path.join(repo,'tests/output');await fs.mkdir(output,{recursive:true});
const fixture=await fs.mkdtemp(path.join(output,'theme-names-'));
const png=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jK9sAAAAASUVORK5CYII=','base64');
await fs.writeFile(path.join(fixture,'background.png'),png);
const results=[];
async function test(name,action){await action();results.push({name,pass:true});}
const valid=['a'.repeat(80),'a'.repeat(81),'a'.repeat(4096),'长'.repeat(4096),'🌙'.repeat(4096)];
assert.equal(MAX_THEME_NAME_CODE_POINTS,4096);
for(const name of valid)await test('load '+Array.from(name).length+' code points / '+name.codePointAt(0),async()=>{
 await fs.writeFile(path.join(fixture,'theme.json'),JSON.stringify({schemaVersion:1,id:'name-test',name,image:'background.png'}));
 assert.equal((await loadTheme(fixture)).theme.name,name);
});
for(const [label,name] of [['too-long','长'.repeat(4097)],['too-many-emoji','🌙'.repeat(4097)],['newline','line\nbreak'],['tab','line\tbreak'],['DEL','line\u007fbreak'],['null',null],['numeric',123]])await test('reject '+label,async()=>{
 await fs.writeFile(path.join(fixture,'theme.json'),JSON.stringify({schemaVersion:1,id:'name-test',name,image:'background.png'}));
 await assert.rejects(loadTheme(fixture),/invalid name field/);
});
const validator=path.join(repo,'engine/assets/theme-package-validator.mjs');
await fs.writeFile(path.join(fixture,'theme.css'),'[data-ds-part="root"] { color: #ffeeaa; }\n');
for(const length of [4096,4097])await test('simple package name '+length,async()=>{
 const source=path.join(fixture,'package-'+length);const stage=path.join(fixture,'stage-'+length);await fs.mkdir(source);await fs.mkdir(stage);
 await fs.writeFile(path.join(source,'theme.json'),JSON.stringify({schemaVersion:1,id:'name-test',name:'🌙'.repeat(length),image:'background.png'}));
 await fs.copyFile(path.join(fixture,'background.png'),path.join(source,'background.png'));await fs.copyFile(path.join(fixture,'theme.css'),path.join(source,'theme.css'));
 const result=spawnSync(process.execPath,[validator,'--source',source,'--stage',stage,'--platform','windows','--client-version','1.5.4'],{encoding:'utf8',windowsHide:true,timeout:15000});
 if(length===4096)assert.equal(result.status,0,result.stderr);else{assert.notEqual(result.status,0);assert.match(result.stderr,/invalid name field/);}
});
const ps=path.join(process.env.SystemRoot,'System32/WindowsPowerShell/v1.0/powershell.exe');
for(const length of [4096,4097])await test('official package name '+length,async()=>{
 const source=path.join(fixture,'official-'+length),stage=path.join(fixture,'official-stage-'+length);await fs.mkdir(source);await fs.mkdir(stage);
 const theme=Buffer.from(JSON.stringify({schemaVersion:1,id:'name-test',name:'🌙'.repeat(length),image:'background.png'}));
 const files=[['theme.json',theme,'application/json'],['background.png',png,'image/png'],['theme.css',Buffer.from('[data-ds-part="root"] { color: #ffeeaa; }\n'),'text/css']];
 for(const [name,bytes] of files)await fs.writeFile(path.join(source,name),bytes);
 const manifest={packageVersion:1,themeId:'name-test',version:'1.0.0',skinApiVersion:1,minClientVersion:'1.0.0',platforms:['windows'],capabilities:['background','safe-css'],publisher:{id:'test',displayName:'Test'},license:'MIT',provenance:{aiGenerated:false,summary:'Generated name boundary fixture'},createdAt:'2026-10-25T00:00:00Z',files:files.map(([name,bytes,mediaType])=>({path:name,bytes:bytes.length,mediaType,sha256:createHash('sha256').update(bytes).digest('hex')}))};
 await fs.writeFile(path.join(source,'manifest.json'),JSON.stringify(manifest));
 const result=spawnSync(process.execPath,[validator,'--source',source,'--stage',stage,'--platform','windows','--client-version','1.5.4'],{encoding:'utf8',windowsHide:true,timeout:15000});
 if(length===4096)assert.equal(result.status,0,result.stderr);else{assert.notEqual(result.status,0);assert.match(result.stderr,/name.*invalid length/);}
});
const result=spawnSync(ps,['-NoLogo','-NoProfile','-NonInteractive','-ExecutionPolicy','RemoteSigned','-File',path.join(repo,'tests/theme-names.ps1'),'-FixtureRoot',fixture],{encoding:'utf8',windowsHide:true,timeout:120000,env:{...process.env,PATH:path.dirname(process.execPath)+';'+process.env.PATH},maxBuffer:2*1024*1024});
assert.equal(result.status,0,result.stderr||result.stdout);const report=JSON.parse(result.stdout.trim().split(/\r?\n/).at(-1));assert.equal(report.pass,true);
for(const directory of ['active-state/active-theme',report.importedDirectory,report.savedDirectory])await test('load PowerShell result '+directory,async()=>{
 const target=path.isAbsolute(directory)?directory:path.join(fixture,directory);assert.equal(Array.from((await loadTheme(target)).theme.name).length,4096);
});
const reportFile=path.join(output,'theme-names-result.json');
const all={pass:true,maxNameCodePoints:4096,results,powerShell:report,realCodexTouched:false};await fs.writeFile(reportFile,JSON.stringify(all,null,2));console.log(JSON.stringify(all));
