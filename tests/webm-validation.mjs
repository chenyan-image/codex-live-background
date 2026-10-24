import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";
import { readImageMetadata, readImageAnimation } from "../engine/scripts/image-metadata.mjs";

const root = path.dirname(fileURLToPath(import.meta.url));
const engine = path.resolve(root, "../engine");
const results = [];
function test(name, action) { action(); results.push({ name, pass: true }); }
function id(hex) { return Buffer.from(hex, "hex"); }
function vint(value) {
  for (let width = 1; width <= 8; width += 1) {
    if (value >= 2 ** (width * 7) - 1) continue;
    const bytes = Buffer.alloc(width);
    let remaining = value;
    for (let index = width - 1; index >= 0; index -= 1) {
      bytes[index] = remaining % 256;
      remaining = Math.floor(remaining / 256);
    }
    bytes[0] |= 1 << (8 - width);
    return bytes;
  }
  throw new Error("test value out of range");
}
function el(hex, ...contents) {
  const data = Buffer.concat(contents.map((item) => Buffer.isBuffer(item) ? item : Buffer.from(item)));
  return Buffer.concat([id(hex), vint(data.length), data]);
}
function uint(value) {
  const bytes = [];
  do { bytes.unshift(value % 256); value = Math.floor(value / 256); } while (value);
  return Buffer.from(bytes);
}
function float(value) { const bytes = Buffer.alloc(8); bytes.writeDoubleBE(value); return bytes; }
function specimen(options = {}) {
  const header = el("1a45dfa3", el("4286", [1]), el("42f7", [1]), el("42f2", [4]), el("42f3", [8]),
    el("4282", Buffer.from(options.docType ?? "webm")), el("4287", [4]), el("4285", [2]));
  const info = el("1549a966", el("2ad7b1", uint(1_000_000)), el("4489", float(options.duration ?? 500)));
  const track = el("ae", el("d7", [1]), el("83", [options.type ?? 1]), el("86", Buffer.from(options.codec ?? "V_VP9")),
    el("23e383", uint(options.defaultDuration ?? 41_666_667)),
    el("e0", el("b0", uint(options.width ?? 160)), el("ba", uint(options.height ?? 90))));
  const tracks = el("1654ae6b", track, ...(options.extraTrack ? [track] : []));
  const blocks = options.emptyCluster ? [] : Array.from({ length: 12 }, (_, index) => {
    const block = Buffer.alloc(5);
    block[0] = options.wrongTrack ? 0x82 : 0x81;
    block.writeInt16BE(options.negativeTimestamp ? -1 : index * (options.fastTimestamps ? 1 : 42), 1);
    block[3] = options.laced ? 0x82 : 0x80;
    block[4] = 0x82; // synthetic payload is never used as a positive decode fixture
    return el("a3", options.emptyPayload ? block.subarray(0, 4) : block);
  });
  const cluster = el("1f43b675", el("e7", [0]), ...blocks, ...(options.deepGroup ? [el("a0", el("a0", [0]))] : []));
  return Buffer.concat([header, el("18538067", info, ...(options.duplicateInfo ? [info] : []), tracks,
    ...(options.noCluster ? [] : [cluster]))]);
}
for (const [codec, count] of [["vp9", 12], ["vp8", 15], ["vp9-120", 60]]) {
  const data = await fs.readFile(path.join(root, "fixtures", `${codec}.webm`));
  test(`real FFmpeg ${codec}`, () => {
    const metadata = readImageMetadata(data, ".webm");
    const animation = readImageAnimation(data, ".webm");
    assert.equal(metadata?.width, 160);
    assert.equal(metadata?.height, 90);
    assert.equal(animation?.sampleCount, count);
    assert.equal(animation?.frameCount, 0);
    assert.equal(animation?.video, true);
    assert.equal(animation?.codec, codec.startsWith("vp9") ? "vp9" : "vp8");
    assert.equal(animation?.durationSeconds, 0.5);
  });
  test(`truncated ${codec}`, () => assert.equal(readImageMetadata(data.subarray(0, data.length - 1), ".webm"), null));
  test(`extension mismatch ${codec}`, () => assert.equal(readImageMetadata(data, ".mp4"), null));
}
for (const [name, options] of [
  ["Matroska DocType", { docType: "matroska" }], ["wrong codec", { codec: "V_MPEG4/ISO/AVC" }],
  ["audio track", { type: 2 }], ["two video tracks", { extraTrack: true }], ["missing clusters", { noCluster: true }],
  ["empty clusters", { emptyCluster: true }], ["empty video payload", { emptyPayload: true }],
  ["wrong block track", { wrongTrack: true }], ["laced block", { laced: true }], ["negative timestamp", { negativeTimestamp: true }],
  ["fast timestamps despite fake DefaultDuration", { fastTimestamps: true }], ["240 FPS DefaultDuration", { defaultDuration: 4_166_667 }],
  ["over 10 minutes", { duration: 600001 }], ["NaN duration", { duration: NaN }], ["infinite duration", { duration: Infinity }],
  ["zero duration", { duration: 0 }], ["oversized width", { width: 16385 }], ["over 50 MP", { width: 8192, height: 8192 }],
  ["duplicate Info", { duplicateInfo: true }], ["deep BlockGroup", { deepGroup: true }],
]) {
  test(`reject ${name}`, () => {
    assert.equal(readImageMetadata(specimen(options), ".webm"), null);
    assert.equal(readImageAnimation(specimen(options), ".webm"), null);
  });
}
for (const [name, bytes] of [
  ["empty", Buffer.alloc(0)], ["magic only", id("1a45dfa3")], ["zero-size VINT", id("1a45dfa300")],
  ["overflow VINT", id("1a45dfa301fffffffffffffe")], ["unknown EBML size", id("1a45dfa3ff")],
  ["arbitrary text", Buffer.from("this is not a WebM video")],
]) test(`reject ${name}`, () => assert.equal(readImageMetadata(bytes, ".webm"), null));

await fs.mkdir(path.join(root, "output"), { recursive: true });
const packageRoot = await fs.mkdtemp(path.join(root, "output", "webm-package-"));
const source = path.join(packageRoot, "source");
const stage = path.join(packageRoot, "stage");
await fs.mkdir(source); await fs.mkdir(stage);
await fs.writeFile(path.join(source, "theme.json"), JSON.stringify({ schemaVersion: 1, id: "test-webm", name: "test", image: "background.webm" }));
await fs.writeFile(path.join(source, "theme.css"), '[data-ds-part="root"] { color: #ffeeaa; }\n');
await fs.copyFile(path.join(root, "fixtures/vp9.webm"), path.join(source, "background.webm"));
let result = spawnSync(process.execPath, [path.join(engine, "assets/theme-package-validator.mjs"),
  "--source", source, "--stage", stage, "--platform", "windows", "--client-version", "1.1.2"], { encoding: "utf8" });
test("simple WebM theme package", () => {
  assert.equal(result.status, 0, result.stderr);
  assert.equal(JSON.parse(result.stdout).image, "background.webm");
});
await fs.writeFile(path.join(source, "background.webm"), specimen({ noCluster: true }));
const rejectedStage = path.join(packageRoot, "rejected-stage"); await fs.mkdir(rejectedStage);
result = spawnSync(process.execPath, [path.join(engine, "assets/theme-package-validator.mjs"),
  "--source", source, "--stage", rejectedStage, "--platform", "windows", "--client-version", "1.1.2"], { encoding: "utf8" });
test("reject forged WebM theme package", () => assert.notEqual(result.status, 0));
const quotePs = (value) => `'${value.replaceAll("'", "''")}'`;
const powershell = path.join(process.env.SystemRoot, "System32/WindowsPowerShell/v1.0/powershell.exe");
result = spawnSync(powershell, ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
  `$ErrorActionPreference='Stop'; . ${quotePs(path.join(engine, "scripts/theme-windows.ps1"))}; function Get-DreamSkinNodeRuntime { [pscustomobject]@{Path=${quotePs(process.execPath)}} }; Assert-DreamSkinImageFile -Path ${quotePs(path.join(root, "fixtures/vp9.webm"))}; [Console]::Out.WriteLine('PASS')`], { encoding: "utf8" });
test("PowerShell WebM metadata import chain", () => { assert.equal(result.status, 0, result.stderr); assert.match(result.stdout, /PASS/); });
result = spawnSync(powershell, ["-NoLogo", "-NoProfile", "-NonInteractive", "-File",
  path.join(engine, "scripts/manager-actions.ps1"), "-Action", "Status", "-SkillRoot", path.join(packageRoot, "missing-root")], { encoding: "utf8" });
test("manager failure has no PowerShell stack", () => {
  assert.equal(result.status, 1);
  assert.ok(result.stderr.trim().length > 0);
  assert.doesNotMatch(result.stderr, /(?:^|\n)At |CategoryInfo|FullyQualifiedErrorId/);
});
await fs.writeFile(path.join(root, "output", "webm-validation-results.json"), JSON.stringify({ pass: true, count: results.length, results, packageRoot }, null, 2));
console.log(JSON.stringify({ pass: true, count: results.length, packageRoot }));
