import fs from "node:fs/promises";
import path from "node:path";
import { randomUUID, createHash } from "node:crypto";
import { readImageMetadata, readImageAnimation } from "./image-metadata.mjs";

// Runs inside the actual Codex renderer, without touching its current theme.
export function installVideoDecodeProbe(key) {
  const input = document.createElement("input");
  input.type = "file";
  input.accept = "video/mp4,video/webm,.mp4,.webm";
  input.hidden = true;
  const video = document.createElement("video");
  video.muted = true;
  video.defaultMuted = true;
  video.volume = 0;
  video.setAttribute("muted", "");
  video.playsInline = true;
  video.setAttribute("playsinline", "");
  video.preload = "auto";
  video.setAttribute("aria-hidden", "true");
  video.style.cssText = "position:fixed;left:0;top:0;width:1px;height:1px;opacity:0;pointer-events:none";
  document.body.append(input, video);
  let url = null;
  let timer = null;
  let frameCallback = null;
  let finish = null;
  let disposed = false;
  let retryTimer = null;
  let started = null;
  const cleanup = () => {
    if (disposed) return;
    disposed = true;
    if (timer) clearTimeout(timer);
    if (retryTimer) clearTimeout(retryTimer);
    if (frameCallback !== null) video.cancelVideoFrameCallback?.(frameCallback);
    video.onerror = null;
    video.pause();
    video.removeAttribute("src");
    video.load();
    input.remove();
    video.remove();
    if (url) URL.revokeObjectURL(url);
    delete window[key];
  };
  window[key] = {
    cleanup: () => { if (finish) finish({ pass: false, reason: "cancelled" }); else cleanup(); },
    start: () => started || (started = new Promise((resolve) => {
      let settled = false;
      finish = (result) => {
        if (settled) return;
        settled = true;
        cleanup();
        resolve(result);
      };
      if (timer) clearTimeout(timer);
      const describe = (error) => ({
        errorName: String(error?.name || "").slice(0, 64),
        errorMessage: String(error?.message || "").slice(0, 240),
        mediaError: Number(video.error?.code) || 0,
        mediaMessage: String(video.error?.message || "").slice(0, 240),
      });
      const hidden = () => document.hidden === true || document.visibilityState === "hidden";
      timer = setTimeout(() => finish({ pass: false,
        reason: hidden() ? "window-hidden" : "decode-timeout", ...describe() }), 20000);
      const file = input.files?.[0];
      const mime = /\.mp4$/i.test(file?.name || "") ? "video/mp4"
        : /\.webm$/i.test(file?.name || "") ? "video/webm" : null;
      if (!file || file.size < 1 || file.size > 512 * 1024 * 1024 ||
          !mime || file.type && file.type !== mime) {
        finish({ pass: false, reason: "invalid-file" });
        return;
      }
      if (hidden()) { finish({ pass: false, reason: "window-hidden" }); return; }
      if (typeof video.requestVideoFrameCallback !== "function") {
        finish({ pass: false, reason: "frame-verification-unavailable" });
        return;
      }
      video.onerror = () => finish({ pass: false, reason: "decode-error", ...describe() });
      const verifyFrame = (_time, frame) => {
        if (settled || disposed) return;
        if (frame.width > 0 && frame.height > 0 && frame.presentedFrames > 0) {
          finish({ pass: true, reason: "decoded-frame", width: frame.width, height: frame.height });
        } else frameCallback = video.requestVideoFrameCallback(verifyFrame);
      };
      frameCallback = video.requestVideoFrameCallback(verifyFrame);
      url = URL.createObjectURL(file);
      video.src = url;
      let attempts = 0;
      const rejected = (error) => {
        if (settled || disposed) return;
        const detail = describe(error);
        if (detail.errorName === "AbortError" && ++attempts < 2 && !detail.mediaError && !hidden()) {
          retryTimer = setTimeout(play, 0);
          return;
        }
        finish({ pass: false, reason: hidden() ? "window-hidden"
          : detail.errorName === "NotAllowedError" ? "playback-blocked"
            : detail.errorName === "AbortError" ? "playback-interrupted" : "playback-rejected", ...detail });
      };
      const play = () => {
        retryTimer = null;
        if (settled || disposed) return;
        try { Promise.resolve(video.play()).catch(rejected); } catch (error) { rejected(error); }
      };
      play();
    })),
  };
  // Self-clean even if CDP disconnects before start is invoked.
  timer = setTimeout(() => window[key]?.cleanup(), 25000);
  return input;
}

export function createVideoDecodeError(result) {
  const details = {
    reason: String(result?.reason || "renderer-changed").slice(0, 64),
    errorName: String(result?.errorName || "").slice(0, 64),
    errorMessage: String(result?.errorMessage || "").slice(0, 240),
    mediaError: Number(result?.mediaError) || 0,
    mediaMessage: String(result?.mediaMessage || "").slice(0, 240),
  };
  let code = "DREAM_VIDEO_PLAYBACK_FAILED";
  let message = "Codex 视频播放失败，请重试；若持续失败，请保留诊断信息。";
  if (details.reason === "window-hidden") {
    code = "DREAM_VIDEO_WINDOW_HIDDEN";
    message = "请展开 Codex 主窗口后重试视频校验。";
  } else if (details.errorName === "NotSupportedError" || [3, 4].includes(details.mediaError)) {
    code = "DREAM_VIDEO_UNSUPPORTED";
    message = "此电脑的 Codex 不支持该视频编码或无法解码此文件，需要生成兼容视频副本。";
  } else if (details.reason === "playback-blocked" || details.errorName === "NotAllowedError") {
    code = "DREAM_VIDEO_PLAYBACK_BLOCKED";
    message = "Codex 的播放策略阻止了视频。请在 Codex 主窗口点击一次后重试。";
  } else if (details.reason === "playback-interrupted" || details.errorName === "AbortError") {
    code = "DREAM_VIDEO_INTERRUPTED";
    message = "视频播放被中断，请保持 Codex 主窗口打开后重试。";
  } else if (details.reason === "decode-timeout") {
    code = "DREAM_VIDEO_TIMEOUT";
    message = "20 秒内未收到视频解码帧，请展开 Codex 主窗口后重试。";
  } else if (details.reason === "cancelled") {
    code = "DREAM_VIDEO_CANCELLED";
    message = "视频校验已取消，请重试。";
  } else if (details.reason === "invalid-file") {
    code = "DREAM_VIDEO_INVALID_FILE";
    message = "视频必须是非空 MP4 或 WebM 普通文件，且不能超过 512 MiB。";
  }
  const diagnostic = [details.reason, details.errorName,
    details.mediaError ? `MediaError ${details.mediaError}` : ""].filter(Boolean).join(" / ");
  const error = new Error(`${message}（${diagnostic}）`);
  error.code = code;
  error.details = details;
  return error;
}

export async function probeVideoDecode(session, snapshotPath) {
  const key = `__dream_skin_decode_${randomUUID().replaceAll("-", "")}`;
  const literal = JSON.stringify(key);
  let objectId;
  try {
    const result = await session.send("Runtime.evaluate", {
      expression: `(${installVideoDecodeProbe.toString()})(${literal})`, returnByValue: false,
    });
    objectId = result.result?.objectId;
    if (result.exceptionDetails || !objectId) throw new Error("无法在 Codex 中建立视频解码校验。");
    await session.send("DOM.setFileInputFiles", { files: [snapshotPath], objectId });
    const decoded = await session.evaluate(`window[${literal}]?.start()`, { userGesture: true, timeoutMs: 25000 });
    if (decoded?.pass !== true) {
      throw createVideoDecodeError(decoded);
    }
    return decoded;
  } finally {
    await session.evaluate(`window[${literal}]?.cleanup()`).catch(() => {});
    if (objectId) await session.send("Runtime.releaseObject", { objectId }).catch(() => {});
  }
}

export async function validateVideoFile(filePath, statePath, forceVideo = false) {
  const extension = path.extname(filePath).toLowerCase() === ".webm" ? ".webm" : ".mp4";
  if (!forceVideo && ![".mp4", ".webm"].includes(path.extname(filePath).toLowerCase())) return { pass: true, video: false };
  const stat = await fs.lstat(filePath);
  if (!stat.isFile() || stat.isSymbolicLink() || stat.size < 1 || stat.size > 512 * 1024 * 1024) {
    throw new Error("视频必须是非空普通文件，且不能超过 512 MiB。");
  }
  const bytes = await fs.readFile(filePath);
  if (bytes.length !== stat.size) throw new Error("视频在校验期间发生变化，请重试。");
  const metadata = readImageMetadata(bytes, extension);
  if (!metadata) throw new Error(extension === ".webm"
    ? "WebM 格式不合规：需要受支持的 VP9 视频，最长 10 分钟、最高 120 FPS，尺寸需符合限制。"
    : "MP4 格式不合规：仅支持非分片 H.264/AVC 或 H.265/HEVC，最长 10 分钟、最高 120 FPS，尺寸需符合限制。");
  let state;
  try { state = JSON.parse(await fs.readFile(statePath, "utf8")); } catch {}
  if (!Number.isInteger(state?.port) || state.port < 1024 || state.port > 65535) {
    throw new Error("无法验证视频解码能力：请先启动 Codex 并连接皮肤运行时，再重试导入或应用。");
  }
  const { materializeMediaSnapshot, probeVideoInCodex } = await import("./injector.mjs");
  const snapshot = await materializeMediaSnapshot(bytes, undefined, extension);
  const decoded = await probeVideoInCodex(snapshot, state);
  const current = await fs.readFile(filePath);
  if (!createHash("sha256").update(bytes).digest().equals(createHash("sha256").update(current).digest())) {
    throw new Error("视频在校验期间发生变化，请重试。");
  }
  return { ...decoded, codec: readImageAnimation(bytes, extension)?.codec };
}
