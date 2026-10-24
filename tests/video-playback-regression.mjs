import assert from "node:assert/strict";
import fs from "node:fs/promises";
import vm from "node:vm";
import { fileURLToPath } from "node:url";
import { installVideoDecodeProbe, probeVideoDecode, createVideoDecodeError }
  from "../engine/scripts/video-decode-probe.mjs";

const engineRoot = fileURLToPath(new URL("../engine/", import.meta.url));
const flush = async () => { for (let i = 0; i < 8; ++i) await Promise.resolve(); };
let checks = 0;
function check(condition, message) { assert.ok(condition, message); checks++; }

function probeHarness(names = [], { name = "sample.mp4", type = "video/mp4", hidden = false } = {}) {
  const timers = new Map();
  const attrs = new Map();
  const removed = [];
  let timerId = 0, frame = null, plays = 0, revoked = 0;
  const input = { files: [{ name, type, size: 32 }], remove() { removed.push("input"); } };
  const video = {
    style: {}, error: null,
    setAttribute(key, value) { attrs.set(key, value); },
    requestVideoFrameCallback(callback) { frame = callback; return 1; },
    cancelVideoFrameCallback() { frame = null; },
    play() {
      plays++;
      check(this.muted && this.defaultMuted && this.volume === 0 && attrs.has("muted"), "probe is explicitly muted before play");
      const exceptionName = names[plays - 1];
      return exceptionName ? Promise.reject(Object.assign(new Error("x".repeat(400)), { name: exceptionName })) : Promise.resolve();
    },
    pause() {}, removeAttribute() {}, load() {}, remove() { removed.push("video"); },
  };
  const context = vm.createContext({
    window: {}, document: { hidden, visibilityState: hidden ? "hidden" : "visible",
      body: { append() {} }, createElement(tag) { return tag === "input" ? input : video; } },
    URL: { createObjectURL() { return "blob:mock"; }, revokeObjectURL() { revoked++; } },
    setTimeout(fn, ms) { timers.set(++timerId, { fn, ms }); return timerId; },
    clearTimeout(id) { timers.delete(id); },
  });
  vm.runInContext(`(${installVideoDecodeProbe.toString()})("probe")`, context);
  const probe = context.window.probe;
  return { context, probe, input, video, timers,
    plays: () => plays,
    async timer(ms) {
      const entry = [...timers].find(([, t]) => t.ms === ms);
      assert.ok(entry, `expected pending ${ms}ms timer`);
      timers.delete(entry[0]); entry[1].fn(); await flush();
    },
    emitFrame(value = { width: 1920, height: 1080, presentedFrames: 1 }) { frame?.(0, value); },
    clean() {
      check(removed.length === 2 && !context.window.probe && timers.size === 0, "probe cleans all DOM and timers");
      check(revoked === (plays ? 1 : 0), "probe releases owned Blob URL");
    },
  };
}

for (const [name, code] of [["NotAllowedError", "DREAM_VIDEO_PLAYBACK_BLOCKED"],
  ["NotSupportedError", "DREAM_VIDEO_UNSUPPORTED"], ["OtherError", "DREAM_VIDEO_PLAYBACK_FAILED"]]) {
  const h = probeHarness([name]); const pending = h.probe.start(); await flush();
  const result = await pending;
  const error = createVideoDecodeError(result);
  check(error.code === code && error.details.errorName === name, `${name} retains its classification`);
  check(error.details.errorMessage.length === 240, "diagnostic message is bounded");
  check(h.plays() === 1, "non-interruption errors do not loop"); h.clean();
}
for (const code of [3, 4]) {
  const h = probeHarness(); const pending = h.probe.start(); await flush();
  h.video.error = { code, message: "codec unavailable" }; h.video.onerror();
  check(createVideoDecodeError(await pending).code === "DREAM_VIDEO_UNSUPPORTED", `MediaError ${code} requests compatible codec`); h.clean();
}
{
  const h = probeHarness(["AbortError"]); const pending = h.probe.start(); await flush();
  await h.timer(0); check(h.plays() === 2, "one interrupted play retries once");
  h.emitFrame(); check((await pending).pass === true, "only a real frame completes success"); h.clean();
}
{
  const h = probeHarness(["AbortError", "AbortError"]); const pending = h.probe.start(); await flush(); await h.timer(0);
  check(createVideoDecodeError(await pending).code === "DREAM_VIDEO_INTERRUPTED" && h.plays() === 2, "repeated abort is bounded"); h.clean();
}
{
  const h = probeHarness(["AbortError"]); const pending = h.probe.start(); await flush(); h.probe.cleanup(); await flush();
  check((await pending).reason === "cancelled" && h.plays() === 1, "cancel prevents scheduled abort retry"); h.clean();
}
{
  const h = probeHarness(); const pending = h.probe.start(); await flush();
  check(h.timers.size === 1, "play promise resolving alone is insufficient");
  await h.timer(20000); check(createVideoDecodeError(await pending).code === "DREAM_VIDEO_TIMEOUT", "20-second deadline is classified"); h.clean();
}
{
  const h = probeHarness(); const pending = h.probe.start(); await flush();
  h.emitFrame({ width: 0, height: 0, presentedFrames: 0 }); await flush();
  check(h.timers.size === 1, "invalid frame is not accepted as decoding success");
  h.emitFrame(); check((await pending).pass === true, "a later valid decoded frame passes"); h.clean();
}
{
  const h = probeHarness([], { hidden: true });
  check(createVideoDecodeError(await h.probe.start()).code === "DREAM_VIDEO_WINDOW_HIDDEN" && h.plays() === 0, "hidden window is not a codec error"); h.clean();
}
{
  const h = probeHarness([], { name: "sample.webm", type: "video/webm" }); const pending = h.probe.start(); await flush();
  check(h.input.accept.includes("video/webm"), "probe accepts WebM");
  h.emitFrame(); check((await pending).pass === true, "WebM uses actual frame validation"); h.clean();
}
{
  const h = probeHarness([], { name: "sample.webm", type: "video/mp4" });
  check((await h.probe.start()).reason === "invalid-file", "MIME and extension mismatch is rejected"); h.clean();
}
{
  const calls = [];
  const session = {
    async send(method, params) { calls.push({ method, params }); return { result: { objectId: "input" } }; },
    async evaluate(expression, options) { calls.push({ expression, options }); return expression.includes("start()")
      ? { pass: true, reason: "decoded-frame", width: 16, height: 16 } : undefined; },
  };
  await probeVideoDecode(session, "C:\\background.webm");
  const start = calls.find(c => c.expression?.includes("start()"));
  check(start.options.userGesture === true && start.options.timeoutMs === 25000, "probe start has explicit gesture and 25-second transport budget");
  check(calls.some(c => c.method === "Runtime.releaseObject"), "CDP remote input is released");
}
{
  const calls = [];
  const session = {
    async send(method) { calls.push(method); return { result: { objectId: "input" } }; },
    async evaluate(expression) { calls.push(expression.includes("cleanup()") ? "cleanup" : "start");
      return expression.includes("start()") ? { pass: false, reason: "playback-rejected", errorName: "NotSupportedError", errorMessage: "unsupported" } : undefined; },
  };
  await assert.rejects(probeVideoDecode(session, "C:\\background.mp4"), error => error.code === "DREAM_VIDEO_UNSUPPORTED" && error.details.errorName === "NotSupportedError"); checks++;
  check(calls.includes("cleanup") && calls.includes("Runtime.releaseObject"), "unsupported error keeps structured fields and releases CDP resources");
}

// Execute the actual Session.evaluate method without opening a WebSocket.
const injector = await fs.readFile(`${engineRoot}/scripts/injector.mjs`, "utf8");
const sessionSource = injector.slice(injector.indexOf("class CdpSession {"), injector.indexOf("class BrowserIdentityAnchor {"));
const sessionType = vm.runInNewContext(`${sessionSource}\nCdpSession`);
{
  const records = [];
  const session = Object.create(sessionType.prototype);
  session.send = async (...args) => { records.push(args); return { result: { value: true } }; };
  await session.evaluate("1"); await session.evaluate("2", { userGesture: true, timeoutMs: 25000 });
  check(records[0][1].userGesture === false && records[0][2] === 10000, "ordinary evaluate keeps default policy");
  check(records[1][1].userGesture === true && records[1][2] === 25000, "media-only evaluate opts in explicitly");
}

// Execute the actual playback functions from the canonical renderer, with a
// minimal DOM. This tests the policy branch without recreating its algorithm.
const renderer = await fs.readFile(`${engineRoot}/assets/renderer-inject.js`, "utf8");
const begin = renderer.indexOf("  const removePlaybackRecovery = () => {");
const end = renderer.indexOf("  const ensureMediaLayer = () => {", begin);
assert.ok(begin > 0 && end > begin);
function rendererHarness(errors = ["NotAllowedError"]) {
  const listeners = new Map(), attrs = new Map(); let plays = 0;
  const root = { setAttribute(k, v) { attrs.set(k, v); }, getAttribute() { return "ambient"; } };
  const document = { documentElement: root, hidden: false,
    addEventListener(name, handler) { listeners.set(name, handler); },
    removeEventListener(name, handler) { if (listeners.get(name) === handler) listeners.delete(name); } };
  const video = { paused: true, hidden: false, error: null,
    addEventListener() {}, removeEventListener() {}, pause() { this.paused = true; }, load() {},
    play() { const name = errors[plays++]; if (name) { this.paused = true; return Promise.reject(Object.assign(new Error(name), { name })); }
      this.paused = false; return Promise.resolve(); } };
  const state = {};
  const context = vm.createContext({ document, window: { testState: state }, video, state, root, URL: { revokeObjectURL() {}, createObjectURL() { return "blob:new"; } } });
  vm.runInContext(`const STATE_KEY="testState", DISABLED_KEY="disabled", installToken={};
    state.installToken=installToken;
    let videoFailure=null, videoDisposed=false, videoPlaybackBlocked=false, playbackRecoveryHandler=null,
      playbackRecoveryAttempts=0, videoSourceGeneration=0, artUrl="blob:old", artUrlOwned=true,
      imageProbe=null, motionQuery=null;
    const videoLayer=video, mediaFileInput=null, artMime="video/mp4", mediaExtensions={"video/mp4":/\\.mp4$/i};
    ${renderer.slice(begin, end)}
    globalThis.api={play:syncVideoPlayback,fail:failVideo,remove:removePlaybackRecovery,
      failure:()=>videoFailure, blocked:()=>videoPlaybackBlocked,
      dispose:()=>{videoDisposed=true;videoSourceGeneration++;removePlaybackRecovery()},
      replace:()=>videoSourceGeneration++};`, context);
  return { api: context.api, video, state, attrs, listeners, plays: () => plays,
    interact(name = "pointerdown", isTrusted = true) { listeners.get(name)?.({ isTrusted }); } };
}
{
  const h = rendererHarness(); h.api.play(); await flush();
  check(h.api.blocked() && !h.api.failure() && !h.video.hidden && h.state.videoError == null, "policy rejection keeps background recoverable");
  h.api.play(); await flush(); check(h.plays() === 1, "routine refresh does not retry blocked playback");
  h.interact("pointerdown", false); await flush(); check(h.plays() === 1, "synthetic event cannot consume gesture recovery");
  h.interact(); await flush();
  check(h.plays() === 2 && !h.api.blocked() && h.listeners.size === 0 && h.attrs.get("data-dream-video-status") === "playing", "real interaction restores playback and removes listeners");
}
{
  const h = rendererHarness(["NotAllowedError", "NotAllowedError", "NotAllowedError", "NotAllowedError"]);
  h.api.play(); await flush(); h.interact(); await flush(); h.interact("keydown"); await flush();
  check(h.plays() === 3 && h.listeners.size === 0 && !h.api.failure(), "policy recovery is bounded to two interactions without permanent codec failure");
}
{
  const h = rendererHarness(); h.api.play(); await flush(); h.api.dispose(); h.interact(); await flush();
  check(h.plays() === 1 && h.listeners.size === 0, "dispose removes recovery listeners");
}
{
  const h = rendererHarness(); h.api.play(); h.api.replace(); await flush();
  check(!h.api.blocked() && !h.api.failure(), "old source rejection cannot mark replacement as failed");
}
{
  const h = rendererHarness(["NotSupportedError"]); h.api.play(); await flush();
  check(Boolean(h.api.failure()) && h.video.hidden && h.listeners.size === 0, "true unsupported media remains a visible error state");
}
console.log(JSON.stringify({ pass: true, checks, codexConnected: false, filesWritten: false }));
