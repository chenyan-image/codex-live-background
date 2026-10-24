import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const SOF_MARKERS = new Set([
  0xc0, 0xc1, 0xc2, 0xc3, 0xc5, 0xc6, 0xc7,
  0xc9, 0xca, 0xcb, 0xcd, 0xce, 0xcf,
]);
const MP4_VIDEO_CODECS = new Set(["avc1", "avc3"]);
const MP4_HEVC_CODECS = new Set(["hvc1", "hev1"]);
export const MAX_IMAGE_DIMENSION = 16384;
export const MAX_IMAGE_PIXELS = 50_000_000;
export const MAX_IMAGE_FRAMES = 300;
export const MAX_VIDEO_DURATION_SECONDS = 600;
export const MAX_VIDEO_FPS = 120;

function uint16be(bytes, offset) {
  return bytes[offset] * 256 + bytes[offset + 1];
}

function uint16le(bytes, offset) {
  return bytes[offset] + bytes[offset + 1] * 256;
}

function uint24le(bytes, offset) {
  return bytes[offset] + bytes[offset + 1] * 256 + bytes[offset + 2] * 65536;
}

function uint32be(bytes, offset) {
  return bytes[offset] * 0x1000000 + bytes[offset + 1] * 0x10000 +
    bytes[offset + 2] * 0x100 + bytes[offset + 3];
}

function uint64be(bytes, offset) {
  const high = uint32be(bytes, offset);
  const low = uint32be(bytes, offset + 4);
  const value = high * 0x100000000 + low;
  return Number.isSafeInteger(value) ? value : null;
}

function uint32le(bytes, offset) {
  return bytes[offset] + bytes[offset + 1] * 0x100 + bytes[offset + 2] * 0x10000 +
    bytes[offset + 3] * 0x1000000;
}

function ascii(bytes, offset, length) {
  return String.fromCharCode(...bytes.subarray(offset, offset + length));
}

function isoBoxAt(bytes, offset, end) {
  if (offset + 8 > end) return null;
  let size = uint32be(bytes, offset);
  let header = 8;
  if (size === 1) {
    if (offset + 16 > end) return null;
    const high = uint32be(bytes, offset + 8);
    const low = uint32be(bytes, offset + 12);
    size = high * 0x100000000 + low;
    header = 16;
    if (!Number.isSafeInteger(size)) return null;
  } else if (size === 0) {
    size = end - offset;
  }
  if (size < header || offset + size > end) return null;
  return {
    type: ascii(bytes, offset + 4, 4),
    start: offset,
    data: offset + header,
    end: offset + size,
  };
}

function isoBoxes(bytes, start, end) {
  const result = [];
  let offset = start;
  while (offset < end) {
    const box = isoBoxAt(bytes, offset, end);
    if (!box) return null;
    result.push(box);
    offset = box.end;
  }
  return result;
}

function childBox(bytes, parent, type) {
  return isoBoxes(bytes, parent.data, parent.end)?.find((box) => box.type === type) ?? null;
}

function mp4Timescale(bytes, mdhd) {
  if (!mdhd || mdhd.data + 4 > mdhd.end) return null;
  const offset = bytes[mdhd.data] === 1 ? mdhd.data + 20 : mdhd.data + 12;
  if (offset + 4 > mdhd.end) return null;
  const timescale = uint32be(bytes, offset);
  return timescale > 0 ? timescale : null;
}

function mp4AvcConfiguration(bytes, entry) {
  const childrenStart = entry.data + 78;
  if (childrenStart > entry.end) return null;
  const avcC = isoBoxes(bytes, childrenStart, entry.end)
    ?.find((box) => box.type === "avcC");
  if (!avcC || avcC.data + 7 > avcC.end || bytes[avcC.data] !== 1) return null;
  let offset = avcC.data + 5;
  const sequenceCount = bytes[offset++] & 0x1f;
  if (sequenceCount < 1) return null;
  for (let index = 0; index < sequenceCount; index += 1) {
    if (offset + 2 > avcC.end) return null;
    const length = uint16be(bytes, offset);
    offset += 2;
    if (length < 1 || offset + length > avcC.end) return null;
    offset += length;
  }
  if (offset >= avcC.end) return null;
  const pictureCount = bytes[offset++];
  if (pictureCount < 1) return null;
  for (let index = 0; index < pictureCount; index += 1) {
    if (offset + 2 > avcC.end) return null;
    const length = uint16be(bytes, offset);
    offset += 2;
    if (length < 1 || offset + length > avcC.end) return null;
    offset += length;
  }
  return {
    profile: bytes[avcC.data + 1],
    compatibility: bytes[avcC.data + 2],
    level: bytes[avcC.data + 3],
  };
}

function mp4HevcConfiguration(bytes, entry) {
  const childrenStart = entry.data + 78;
  if (childrenStart > entry.end) return null;
  const config = isoBoxes(bytes, childrenStart, entry.end)?.find((box) => box.type === "hvcC");
  if (!config || config.data + 23 > config.end || bytes[config.data] !== 1) return null;
  let offset = config.data + 23;
  const parameterSets = new Set();
  for (let array = 0; array < bytes[config.data + 22]; array += 1) {
    if (offset + 3 > config.end) return null;
    const type = bytes[offset++] & 0x3f;
    const count = uint16be(bytes, offset);
    offset += 2;
    for (let index = 0; index < count; index += 1) {
      if (offset + 2 > config.end) return null;
      const length = uint16be(bytes, offset);
      offset += 2;
      if (length < 2 || offset + length > config.end || ((bytes[offset] >> 1) & 0x3f) !== type) return null;
      parameterSets.add(type);
      offset += length;
    }
  }
  if (offset !== config.end) return null;
  // hvc1 carries parameter sets in the configuration; hev1 may carry them in-band.
  if (entry.type === "hvc1" && ![32, 33, 34].every((type) => parameterSets.has(type))) return null;
  return { profile: bytes[config.data + 1] & 0x1f, level: bytes[config.data + 12] };
}

function mp4SampleSizes(bytes, stbl) {
  const stsz = childBox(bytes, stbl, "stsz");
  if (!stsz || stsz.data + 12 > stsz.end) return null;
  const fixedSize = uint32be(bytes, stsz.data + 4);
  const sampleCount = uint32be(bytes, stsz.data + 8);
  const maximumSamples = MAX_VIDEO_DURATION_SECONDS * MAX_VIDEO_FPS;
  if (sampleCount < 1 || sampleCount > maximumSamples) return null;
  if (fixedSize > 0) {
    const sampleBytes = fixedSize * sampleCount;
    return Number.isSafeInteger(sampleBytes)
      ? { sampleCount, sampleBytes, sampleSizes: Array(sampleCount).fill(fixedSize) }
      : null;
  }
  if (stsz.data + 12 + sampleCount * 4 > stsz.end) return null;
  let sampleBytes = 0;
  const sampleSizes = [];
  for (let index = 0; index < sampleCount; index += 1) {
    const size = uint32be(bytes, stsz.data + 12 + index * 4);
    if (size < 1) return null;
    sampleSizes.push(size);
    sampleBytes += size;
    if (!Number.isSafeInteger(sampleBytes)) return null;
  }
  return { sampleCount, sampleBytes, sampleSizes };
}

function mp4Timing(bytes, stbl, timescale, expectedSamples) {
  const stts = childBox(bytes, stbl, "stts");
  if (!stts || stts.data + 8 > stts.end) return null;
  const entryCount = uint32be(bytes, stts.data + 4);
  if (entryCount < 1 || stts.data + 8 + entryCount * 8 > stts.end) return null;
  let sampleCount = 0;
  let durationTicks = 0;
  let minimumDelta = Infinity;
  for (let index = 0; index < entryCount; index += 1) {
    const offset = stts.data + 8 + index * 8;
    const count = uint32be(bytes, offset);
    const delta = uint32be(bytes, offset + 4);
    if (count < 1 || delta < 1) return null;
    sampleCount += count;
    durationTicks += count * delta;
    minimumDelta = Math.min(minimumDelta, delta);
    if (!Number.isSafeInteger(sampleCount) || !Number.isSafeInteger(durationTicks)) return null;
  }
  if (sampleCount !== expectedSamples) return null;
  const durationSeconds = durationTicks / timescale;
  const fps = sampleCount / durationSeconds;
  const peakFps = timescale / minimumDelta;
  if (!Number.isFinite(durationSeconds) || durationSeconds <= 0 ||
      durationSeconds > MAX_VIDEO_DURATION_SECONDS || !Number.isFinite(fps) ||
      fps > MAX_VIDEO_FPS + 0.01 || peakFps > MAX_VIDEO_FPS + 0.01) return null;
  return { durationSeconds, fps, peakFps };
}

function mp4ChunkOffsets(bytes, stbl) {
  const box = childBox(bytes, stbl, "stco") || childBox(bytes, stbl, "co64");
  if (!box || box.data + 8 > box.end) return null;
  const count = uint32be(bytes, box.data + 4);
  const width = box.type === "co64" ? 8 : 4;
  if (count < 1 || box.data + 8 + count * width > box.end) return null;
  const offsets = [];
  for (let index = 0; index < count; index += 1) {
    const offset = box.type === "co64"
      ? uint64be(bytes, box.data + 8 + index * width)
      : uint32be(bytes, box.data + 8 + index * width);
    if (!Number.isSafeInteger(offset)) return null;
    offsets.push(offset);
  }
  return offsets;
}

function mp4SampleToChunkInfo(bytes, stbl, chunkCount, expectedSamples, allowedDescriptions) {
  const stsc = childBox(bytes, stbl, "stsc");
  if (!stsc || stsc.data + 8 > stsc.end) return null;
  const count = uint32be(bytes, stsc.data + 4);
  if (count < 1 || stsc.data + 8 + count * 12 > stsc.end) return null;
  const entries = [];
  let previousFirstChunk = 0;
  for (let index = 0; index < count; index += 1) {
    const offset = stsc.data + 8 + index * 12;
    const firstChunk = uint32be(bytes, offset);
    const samplesPerChunk = uint32be(bytes, offset + 4);
    const descriptionIndex = uint32be(bytes, offset + 8);
    if (firstChunk <= previousFirstChunk || samplesPerChunk < 1 ||
        !allowedDescriptions.has(descriptionIndex)) return null;
    previousFirstChunk = firstChunk;
    entries.push({ firstChunk, samplesPerChunk, descriptionIndex });
  }
  if (entries[0].firstChunk !== 1 || entries.at(-1).firstChunk > chunkCount) return null;
  let sampleCount = 0;
  const chunks = [];
  let entryIndex = 0;
  for (let chunkIndex = 1; chunkIndex <= chunkCount; chunkIndex += 1) {
    while (entryIndex + 1 < entries.length && entries[entryIndex + 1].firstChunk <= chunkIndex) {
      entryIndex += 1;
    }
    const entry = entries[entryIndex];
    chunks.push({
      descriptionIndex: entry.descriptionIndex,
      sampleStart: sampleCount,
      sampleCount: entry.samplesPerChunk,
    });
    sampleCount += entry.samplesPerChunk;
    if (!Number.isSafeInteger(sampleCount) || sampleCount > expectedSamples) return null;
  }
  return sampleCount === expectedSamples ? {
    descriptionIndices: new Set(entries.map((entry) => entry.descriptionIndex)),
    chunks,
  } : null;
}

function mp4ChunkRangesAreValid(chunkOffsets, chunking, sampleSizes, mediaBoxes) {
  return chunkOffsets.every((offset, index) => {
    const chunk = chunking.chunks[index];
    let chunkBytes = 0;
    for (let sample = chunk.sampleStart;
      sample < chunk.sampleStart + chunk.sampleCount; sample += 1) {
      chunkBytes += sampleSizes[sample];
    }
    const end = offset + chunkBytes;
    return Number.isSafeInteger(end) && mediaBoxes.some((box) =>
      offset >= box.data && end <= box.end);
  });
}

function mp4VideoMetadata(bytes) {
  const roots = isoBoxes(bytes, 0, bytes.length);
  if (!roots?.some((box) => box.type === "ftyp") ||
      roots.some((box) => box.type === "moof")) return null;
  const mediaBoxes = roots.filter((box) => box.type === "mdat" && box.end > box.data);
  const mediaBytes = mediaBoxes.reduce((total, box) => total + box.end - box.data, 0);
  if (!mediaBoxes.length || mediaBytes < 1) return null;
  const moov = roots.find((box) => box.type === "moov");
  if (!moov || childBox(bytes, moov, "mvex")) return null;
  for (const trak of isoBoxes(bytes, moov.data, moov.end) ?? []) {
    if (trak.type !== "trak") continue;
    const mdia = childBox(bytes, trak, "mdia");
    const hdlr = mdia && childBox(bytes, mdia, "hdlr");
    if (!hdlr || hdlr.data + 12 > hdlr.end || ascii(bytes, hdlr.data + 8, 4) !== "vide") continue;
    const timescale = mp4Timescale(bytes, childBox(bytes, mdia, "mdhd"));
    if (!timescale) continue;
    const minf = childBox(bytes, mdia, "minf");
    const stbl = minf && childBox(bytes, minf, "stbl");
    const stsd = stbl && childBox(bytes, stbl, "stsd");
    if (!stsd || stsd.data + 8 > stsd.end) continue;
    const entryCount = uint32be(bytes, stsd.data + 4);
    const configurations = [];
    let entryOffset = stsd.data + 8;
    for (let index = 0; index < entryCount; index += 1) {
      const entry = isoBoxAt(bytes, entryOffset, stsd.end);
      if (!entry) break;
      const configuration = MP4_VIDEO_CODECS.has(entry.type)
        ? mp4AvcConfiguration(bytes, entry)
        : MP4_HEVC_CODECS.has(entry.type) ? mp4HevcConfiguration(bytes, entry) : null;
      if (configuration && entry.data + 28 <= entry.end) {
        const width = uint16be(bytes, entry.data + 24);
        const height = uint16be(bytes, entry.data + 26);
        if (width > 0 && height > 0) configurations.push({
          index: index + 1,
          width,
          height,
          codec: entry.type,
          ...configuration,
        });
      }
      entryOffset = entry.end;
    }
    if (!configurations.length) continue;
    const sizes = mp4SampleSizes(bytes, stbl);
    const timing = sizes && mp4Timing(bytes, stbl, timescale, sizes.sampleCount);
    const chunkOffsets = mp4ChunkOffsets(bytes, stbl);
    const allowedDescriptions = new Set(configurations.map((entry) => entry.index));
    const chunking = sizes && chunkOffsets
      ? mp4SampleToChunkInfo(bytes, stbl, chunkOffsets.length, sizes.sampleCount, allowedDescriptions)
      : null;
    if (!sizes || !timing || sizes.sampleBytes > mediaBytes || !chunkOffsets ||
        !chunking ||
        !mp4ChunkRangesAreValid(chunkOffsets, chunking, sizes.sampleSizes, mediaBoxes)) continue;
    const selected = configurations.find((entry) => chunking.descriptionIndices.has(entry.index));
    if (selected) return {
      ...selected,
      sampleCount: sizes.sampleCount,
      durationSeconds: timing.durationSeconds,
      fps: timing.fps,
      peakFps: timing.peakFps,
    };
  }
  return null;
}

// A bounded WebM subset for local wallpaper videos. Element sizes are checked
// against their parent before reading any field. Only Segment may be open-ended;
// live/unknown-size clusters, encrypted tracks and laced blocks are deliberately
// excluded. Decoder support is still tested in the actual Codex renderer.
function webmVideoMetadata(bytes) {
  const EBML = 0x1a45dfa3;
  const SEGMENT = 0x18538067;
  const INFO = 0x1549a966;
  const TRACKS = 0x1654ae6b;
  const CLUSTER = 0x1f43b675;
  const MAX_ELEMENTS = 250_000;
  let elements = 0;
  function vint(offset, end, keepMarker = false) {
    if (offset >= end || bytes[offset] === 0) return null;
    let width = 1;
    let marker = 0x80;
    while (!(bytes[offset] & marker)) { marker >>= 1; width += 1; }
    if (width > (keepMarker ? 4 : 8) || offset + width > end) return null;
    let value = keepMarker ? bytes[offset] : bytes[offset] & (marker - 1);
    let unknown = !keepMarker && value === marker - 1;
    for (let index = 1; index < width; index += 1) {
      value = value * 256 + bytes[offset + index];
      unknown = unknown && bytes[offset + index] === 0xff;
    }
    if (!unknown && !Number.isSafeInteger(value)) return null;
    return { value, width, unknown };
  }
  function element(offset, end, allowUnknownSegment = false) {
    if (++elements > MAX_ELEMENTS) return null;
    const id = vint(offset, end, true);
    const size = id && vint(offset + id.width, end);
    if (!size || (size.unknown && !(allowUnknownSegment && id.value === SEGMENT))) return null;
    const data = offset + id.width + size.width;
    const finish = size.unknown ? end : data + size.value;
    if (!Number.isSafeInteger(finish) || finish > end || finish < data) return null;
    return { id: id.value, data, end: finish };
  }
  function children(parent) {
    const result = [];
    for (let offset = parent.data; offset < parent.end;) {
      const child = element(offset, parent.end);
      if (!child) return null;
      result.push(child);
      offset = child.end;
    }
    return result;
  }
  function single(items, id) {
    const found = items.filter((item) => item.id === id);
    return found.length === 1 ? found[0] : null;
  }
  function uint(item) {
    if (!item || item.end - item.data < 1 || item.end - item.data > 8) return null;
    let value = 0;
    for (let offset = item.data; offset < item.end; offset += 1) value = value * 256 + bytes[offset];
    return Number.isSafeInteger(value) ? value : null;
  }
  function float(item) {
    if (!item || ![4, 8].includes(item.end - item.data)) return null;
    const view = new DataView(bytes.buffer, bytes.byteOffset + item.data, item.end - item.data);
    const value = item.end - item.data === 4 ? view.getFloat32(0) : view.getFloat64(0);
    return Number.isFinite(value) ? value : null;
  }
  function text(item, maximum = 64) {
    if (!item || item.end - item.data > maximum || item.end === item.data) return null;
    return ascii(bytes, item.data, item.end - item.data);
  }
  function uniqueFields(items, ids) {
    return ids.every((id) => items.filter((item) => item.id === id).length <= 1);
  }
  const header = element(0, bytes.length);
  if (!header || header.id !== EBML || header.end > 4096) return null;
  const headerFields = children(header);
  if (!headerFields || text(single(headerFields, 0x4282)) !== "webm" ||
      !uniqueFields(headerFields, [0x4286, 0x42f7, 0x42f2, 0x42f3, 0x4282, 0x4287, 0x4285])) return null;
  for (const [id, maximum] of [[0x42f7, 1], [0x42f2, 4], [0x42f3, 8], [0x4285, 4]]) {
    const field = single(headerFields, id);
    if (field && (!(uint(field) > 0) || uint(field) > maximum)) return null;
  }
  let segmentOffset = header.end;
  while (segmentOffset < bytes.length) {
    const next = element(segmentOffset, bytes.length, true);
    if (!next) return null;
    if (next.id === SEGMENT) break;
    if (next.id !== 0xec) return null;
    segmentOffset = next.end;
  }
  const segment = element(segmentOffset, bytes.length, true);
  if (!segment || segment.id !== SEGMENT || segment.end !== bytes.length) return null;
  const roots = children(segment);
  const info = roots && single(roots, INFO);
  const tracks = roots && single(roots, TRACKS);
  if (!info || !tracks || roots.some((item) => !new Set([
    0x114d9b74, INFO, TRACKS, CLUSTER, 0x1c53bb6b, 0x1254c367, 0xec, 0xbf,
  ]).has(item.id))) return null;
  const infoFields = children(info);
  if (!infoFields || !uniqueFields(infoFields, [0x2ad7b1, 0x4489])) return null;
  const scaleField = single(infoFields, 0x2ad7b1);
  const scale = scaleField ? uint(scaleField) : 1_000_000;
  const durationField = single(infoFields, 0x4489);
  const durationTicks = durationField ? float(durationField) : null;
  if (!(scale > 0) || scale > 1_000_000_000 ||
      (durationField && !(durationTicks > 0))) return null;
  const declaredDuration = durationTicks === null ? null : durationTicks * scale / 1e9;
  if (declaredDuration !== null && declaredDuration > MAX_VIDEO_DURATION_SECONDS) return null;
  const trackChildren = children(tracks);
  if (!trackChildren || trackChildren.filter((item) => item.id === 0xae).length !== 1) return null;
  const track = children(single(trackChildren, 0xae));
  if (!track || !uniqueFields(track, [0xd7, 0x83, 0x86, 0xe0, 0x23e383, 0x23314f]) ||
      track.some((item) => item.id === 0x6d80)) return null;
  const trackNumber = uint(single(track, 0xd7));
  const codec = text(single(track, 0x86));
  if (!(trackNumber > 0) || uint(single(track, 0x83)) !== 1 || !["V_VP9", "V_VP8"].includes(codec)) return null;
  const trackScale = single(track, 0x23314f);
  if (trackScale && float(trackScale) !== 1) return null;
  const defaultDurationField = single(track, 0x23e383);
  const defaultDuration = defaultDurationField ? uint(defaultDurationField) : null;
  if (defaultDurationField && (!(defaultDuration > 0) || 1e9 / defaultDuration > MAX_VIDEO_FPS + 0.01)) return null;
  const videoElement = single(track, 0xe0);
  const video = videoElement && children(videoElement);
  if (!video || !uniqueFields(video, [0xb0, 0xba, 0x54b2]) ||
      (single(video, 0x54b2) && uint(single(video, 0x54b2)) !== 0)) return null;
  const width = uint(single(video, 0xb0));
  const height = uint(single(video, 0xba));
  if (!classifyImageDimensions({ width, height })) return null;
  let sampleCount = 0;
  let displayedCount = 0;
  let firstTimestamp = null;
  let lastTimestamp = null;
  let minimumDelta = Infinity;
  let maximumEnd = 0;
  let keyframes = 0;
  const clusters = roots.filter((item) => item.id === CLUSTER);
  if (!clusters.length) return null;
  for (const cluster of clusters) {
    const entries = children(cluster);
    if (!entries || !uniqueFields(entries, [0xe7])) return null;
    const clusterTimestamp = uint(single(entries, 0xe7));
    if (clusterTimestamp === null) return null;
    let blocksInCluster = 0;
    for (const entry of entries) {
      let block = entry;
      let blockDuration = null;
      let groupKeyframe = false;
      if (entry.id === 0xa0) {
        const group = children(entry);
        if (!group || !uniqueFields(group, [0xa1, 0x9b]) || group.some((item) => item.id === 0xa0)) return null;
        block = single(group, 0xa1);
        if (!block) return null;
        const duration = single(group, 0x9b);
        blockDuration = duration ? uint(duration) : null;
        if (duration && !(blockDuration > 0)) return null;
        groupKeyframe = !group.some((item) => item.id === 0xfb);
      } else if (entry.id !== 0xa3) {
        if (![0xe7, 0xa7, 0xab, 0x5854, 0xec, 0xbf].includes(entry.id)) return null;
        continue;
      }
      const number = vint(block.data, block.end);
      if (!number || number.unknown || number.value !== trackNumber || block.data + number.width + 3 >= block.end) return null;
      const offset = block.data + number.width;
      const rawTimestamp = uint16be(bytes, offset);
      const relativeTimestamp = rawTimestamp >= 0x8000 ? rawTimestamp - 0x10000 : rawTimestamp;
      const flags = bytes[offset + 2];
      // Wallpaper conversion emits no lacing. This also makes every sample's
      // payload and timing independently bounded without trusting lace counts.
      if (flags & 0x06) return null;
      const timestamp = (clusterTimestamp + relativeTimestamp) * scale / 1e9;
      if (!Number.isFinite(timestamp) || timestamp < 0 || timestamp > MAX_VIDEO_DURATION_SECONDS) return null;
      sampleCount += 1;
      blocksInCluster += 1;
      if (sampleCount > MAX_VIDEO_DURATION_SECONDS * MAX_VIDEO_FPS * 2) return null;
      if ((entry.id === 0xa3 && flags & 0x80) || groupKeyframe) keyframes += 1;
      if (!(flags & 0x08)) {
        if (lastTimestamp !== null) {
          const delta = timestamp - lastTimestamp;
          // Matroska timestamps are quantized to TimecodeScale. Allow one tick
          // of rounding at 120 FPS (8/9 ms with the usual 1 ms scale).
          if (delta <= 0 || delta + scale / 1e9 + 1e-9 < 1 / MAX_VIDEO_FPS) return null;
          minimumDelta = Math.min(minimumDelta, delta);
        }
        firstTimestamp ??= timestamp;
        lastTimestamp = timestamp;
        displayedCount += 1;
      }
      const duration = blockDuration !== null ? blockDuration * scale / 1e9 : (defaultDuration ?? 0) / 1e9;
      maximumEnd = Math.max(maximumEnd, timestamp + duration);
    }
    if (!blocksInCluster) return null;
  }
  if (!keyframes || !displayedCount || displayedCount > MAX_VIDEO_DURATION_SECONDS * MAX_VIDEO_FPS) return null;
  const inferredFrameDuration = defaultDuration !== null ? defaultDuration / 1e9
    : Number.isFinite(minimumDelta) ? minimumDelta : null;
  const durationSeconds = declaredDuration ?? (inferredFrameDuration !== null ? lastTimestamp + inferredFrameDuration : null);
  if (!(durationSeconds > 0) || durationSeconds > MAX_VIDEO_DURATION_SECONDS + 1e-8 ||
      maximumEnd > MAX_VIDEO_DURATION_SECONDS + 1e-8 ||
      maximumEnd > durationSeconds + scale / 1e9 + 1e-8 ||
      lastTimestamp >= durationSeconds || firstTimestamp > scale / 1e9) return null;
  const fps = displayedCount / durationSeconds;
  const peakFps = defaultDuration !== null ? 1e9 / defaultDuration
    : Number.isFinite(minimumDelta) ? 1 / (minimumDelta + scale / 1e9) : fps;
  if (fps > MAX_VIDEO_FPS + 0.01 || peakFps > MAX_VIDEO_FPS + 0.01) return null;
  return { width, height, codec: codec === "V_VP9" ? "vp9" : "vp8", sampleCount, durationSeconds, fps, peakFps };
}

function gifDimensions(bytes) {
  if (bytes.length < 10 || !["GIF87a", "GIF89a"].includes(ascii(bytes, 0, 6))) return null;
  const width = uint16le(bytes, 6);
  const height = uint16le(bytes, 8);
  return width > 0 && height > 0 ? { width, height } : null;
}

function pngDimensions(bytes) {
  const signature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
  if (bytes.length < 24 || signature.some((value, index) => bytes[index] !== value) ||
      uint32be(bytes, 8) !== 13 || ascii(bytes, 12, 4) !== "IHDR") return null;
  const width = uint32be(bytes, 16);
  const height = uint32be(bytes, 20);
  return width > 0 && height > 0 ? { width, height } : null;
}

function jpegDimensions(bytes) {
  if (bytes.length < 12 || bytes[0] !== 0xff || bytes[1] !== 0xd8) return null;
  let offset = 2;
  while (offset + 9 < bytes.length) {
    if (bytes[offset] !== 0xff) {
      offset += 1;
      continue;
    }
    while (offset < bytes.length && bytes[offset] === 0xff) offset += 1;
    const marker = bytes[offset++];
    if (marker === 0xd9 || marker === 0xda) break;
    if (marker === 0x01 || (marker >= 0xd0 && marker <= 0xd8)) continue;
    if (offset + 2 > bytes.length) break;
    const length = uint16be(bytes, offset);
    if (length < 2 || offset + length > bytes.length) break;
    if (SOF_MARKERS.has(marker) && length >= 7) {
      const height = uint16be(bytes, offset + 3);
      const width = uint16be(bytes, offset + 5);
      return width > 0 && height > 0 ? { width, height } : null;
    }
    offset += length;
  }
  return null;
}

function webpDimensions(bytes) {
  if (bytes.length < 20 || ascii(bytes, 0, 4) !== "RIFF" || ascii(bytes, 8, 4) !== "WEBP") {
    return null;
  }
  const riffEnd = Math.min(bytes.length, uint32le(bytes, 4) + 8);
  let offset = 12;
  while (offset + 8 <= riffEnd) {
    const type = ascii(bytes, offset, 4);
    const size = bytes[offset + 4] + bytes[offset + 5] * 256 +
      bytes[offset + 6] * 65536 + bytes[offset + 7] * 0x1000000;
    const data = offset + 8;
    if (data + size > riffEnd) break;
    if (type === "VP8X" && size >= 10) {
      return { width: uint24le(bytes, data + 4) + 1, height: uint24le(bytes, data + 7) + 1 };
    }
    if (type === "VP8L" && size >= 5 && bytes[data] === 0x2f) {
      const width = 1 + bytes[data + 1] + ((bytes[data + 2] & 0x3f) << 8);
      const height = 1 + (bytes[data + 2] >> 6) + (bytes[data + 3] << 2) +
        ((bytes[data + 4] & 0x0f) << 10);
      return { width, height };
    }
    if (type === "VP8 " && size >= 10 && bytes[data + 3] === 0x9d &&
      bytes[data + 4] === 0x01 && bytes[data + 5] === 0x2a) {
      return {
        width: uint16le(bytes, data + 6) & 0x3fff,
        height: uint16le(bytes, data + 8) & 0x3fff,
      };
    }
    offset = data + size + (size % 2);
  }
  return null;
}

function skipGifSubBlocks(bytes, offset) {
  while (offset < bytes.length) {
    const size = bytes[offset++];
    if (size === 0) return offset;
    if (offset + size > bytes.length) return null;
    offset += size;
  }
  return null;
}

function gifAnimationInfo(bytes) {
  if (!gifDimensions(bytes)) return null;
  let offset = 13;
  const packed = bytes[10];
  if (packed & 0x80) offset += 3 * (1 << ((packed & 0x07) + 1));
  let frameCount = 0;
  while (offset < bytes.length) {
    const marker = bytes[offset++];
    if (marker === 0x3b) break;
    if (marker === 0x21) {
      if (offset >= bytes.length) return null;
      const label = bytes[offset++];
      if (label === 0xf9) {
        if (offset >= bytes.length) return null;
        const size = bytes[offset++];
        if (size !== 4 || offset + size >= bytes.length) return null;
        offset += size;
        if (bytes[offset++] !== 0) return null;
      } else if (label === 0x01) {
        if (offset >= bytes.length) return null;
        const size = bytes[offset++];
        if (size !== 12 || offset + size > bytes.length) return null;
        offset += size;
        const next = skipGifSubBlocks(bytes, offset);
        if (next === null) return null;
        offset = next;
      } else {
        const next = skipGifSubBlocks(bytes, offset);
        if (next === null) return null;
        offset = next;
      }
      continue;
    }
    if (marker !== 0x2c || offset + 9 > bytes.length) return null;
    const imagePacked = bytes[offset + 8];
    offset += 9;
    if (imagePacked & 0x80) offset += 3 * (1 << ((imagePacked & 0x07) + 1));
    if (offset >= bytes.length) return null;
    offset += 1;
    const next = skipGifSubBlocks(bytes, offset);
    if (next === null) return null;
    offset = next;
    frameCount += 1;
    if (frameCount > MAX_IMAGE_FRAMES) return { animated: true, frameCount };
  }
  return frameCount > 0 ? { animated: frameCount > 1, frameCount } : null;
}

function pngAnimationInfo(bytes) {
  if (!pngDimensions(bytes)) return null;
  const signature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
  let offset = signature.length;
  let declaredFrameCount = 0;
  let actualFrameCount = 0;
  while (offset + 12 <= bytes.length) {
    const length = uint32be(bytes, offset);
    const data = offset + 8;
    const end = data + length;
    if (end + 4 > bytes.length) return null;
    const type = ascii(bytes, offset + 4, 4);
    if (type === "acTL" && length >= 8) {
      declaredFrameCount = Math.max(declaredFrameCount, uint32be(bytes, data));
      if (declaredFrameCount > MAX_IMAGE_FRAMES) {
        return { animated: true, frameCount: declaredFrameCount };
      }
    }
    if (type === "fcTL") {
      actualFrameCount += 1;
      if (actualFrameCount > MAX_IMAGE_FRAMES) {
        return { animated: true, frameCount: actualFrameCount };
      }
    }
    offset = end + 4;
    if (type === "IEND") break;
  }
  const frameCount = Math.max(1, declaredFrameCount, actualFrameCount);
  return { animated: frameCount > 1, frameCount };
}

function webpAnimationInfo(bytes) {
  if (!webpDimensions(bytes)) return null;
  const riffEnd = Math.min(bytes.length, uint32le(bytes, 4) + 8);
  let offset = 12;
  let frameCount = 0;
  let animated = false;
  while (offset + 8 <= riffEnd) {
    const type = ascii(bytes, offset, 4);
    const size = uint32le(bytes, offset + 4);
    const data = offset + 8;
    if (data + size > riffEnd) return null;
    if (type === "VP8X" && size >= 1) animated = Boolean(bytes[data] & 0x02);
    if (type === "ANMF") {
      frameCount += 1;
      if (frameCount > MAX_IMAGE_FRAMES) return { animated: true, frameCount };
    }
    offset = data + size + (size % 2);
  }
  if (frameCount > 0) animated = true;
  return { animated, frameCount: frameCount || 1 };
}

export function classifyImageDimensions({ width, height }) {
  const ratio = width / height;
  if (
    !Number.isSafeInteger(width) || !Number.isSafeInteger(height)
    || width < 1 || height < 1
    || width > MAX_IMAGE_DIMENSION || height > MAX_IMAGE_DIMENSION
    || width * height > MAX_IMAGE_PIXELS
    || !Number.isFinite(ratio)
  ) return null;
  const aspect = ratio >= 2.25 ? "ultrawide" : ratio >= 1.45 ? "wide"
    : ratio >= 1.08 ? "landscape" : ratio >= 0.9 ? "square" : "portrait";
  return {
    width,
    height,
    ratio,
    wide: ratio >= 1.75,
    aspect,
    taskMode: ratio >= 2.25 ? "banner" : "ambient",
  };
}

// Raw pixel dimensions straight from the container header — no decode, and no
// safety-cap classification, so callers can reject oversized images *before*
// anything rasterizes them. Returns null for formats this header parser does
// not recognize (e.g. HEIC/TIFF).
export function readRawDimensions(value, extension = "") {
  const bytes = value instanceof Uint8Array ? value : new Uint8Array(value);
  const normalized = extension.toLowerCase();
  if (normalized === ".gif" || bytes[0] === 0x47 && bytes[1] === 0x49 && bytes[2] === 0x46) {
    return gifDimensions(bytes);
  }
  if (normalized === ".png" || bytes[0] === 0x89) return pngDimensions(bytes);
  if (normalized === ".jpg" || normalized === ".jpeg" ||
    (bytes[0] === 0xff && bytes[1] === 0xd8)) return jpegDimensions(bytes);
  if (normalized === ".webp" || ascii(bytes, 8, 4) === "WEBP") return webpDimensions(bytes);
  if (normalized === ".webm" || uint32be(bytes, 0) === 0x1a45dfa3) {
    if (normalized && normalized !== ".webm") return null;
    const metadata = webmVideoMetadata(bytes);
    return metadata ? { width: metadata.width, height: metadata.height } : null;
  }
  if (normalized === ".mp4" || ascii(bytes, 4, 4) === "ftyp") {
    const metadata = mp4VideoMetadata(bytes);
    return metadata ? { width: metadata.width, height: metadata.height } : null;
  }
  return null;
}

export function readImageMetadata(value, extension = "") {
  const dimensions = readRawDimensions(value, extension);
  return dimensions ? classifyImageDimensions(dimensions) : null;
}

export function readImageAnimation(value, extension = "") {
  const bytes = value instanceof Uint8Array ? value : new Uint8Array(value);
  const normalized = extension.toLowerCase();
  if (normalized === ".gif" || bytes[0] === 0x47 && bytes[1] === 0x49 && bytes[2] === 0x46) {
    return gifAnimationInfo(bytes);
  }
  if (normalized === ".png" || bytes[0] === 0x89) return pngAnimationInfo(bytes);
  if (normalized === ".webp" || ascii(bytes, 8, 4) === "WEBP") return webpAnimationInfo(bytes);
  const webm = normalized === ".webm" || uint32be(bytes, 0) === 0x1a45dfa3;
  if (webm || normalized === ".mp4" || ascii(bytes, 4, 4) === "ftyp") {
    if (webm && normalized && normalized !== ".webm") return null;
    const metadata = webm ? webmVideoMetadata(bytes) : mp4VideoMetadata(bytes);
    return metadata ? {
      animated: true,
      frameCount: 0,
      video: true,
      codec: metadata.codec,
      sampleCount: metadata.sampleCount,
      durationSeconds: metadata.durationSeconds,
      fps: metadata.fps,
      peakFps: metadata.peakFps,
    } : null;
  }
  return { animated: false, frameCount: 1 };
}

// Keep the PowerShell theme store on the same strict parser as the injector.
// The CLI is intentionally tiny: it only reads a user-selected file and emits
// validated dimensions; it never writes or follows a caller-provided output.
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const [mode, imagePath] = process.argv.slice(2);
  if (mode !== "--check" || !imagePath) {
    console.error("Usage: image-metadata.mjs --check <image>");
    process.exitCode = 2;
  } else {
    try {
      const resolved = path.resolve(imagePath);
      const bytes = await fs.readFile(resolved);
      const metadata = readImageMetadata(bytes, path.extname(resolved));
      const animation = readImageAnimation(bytes, path.extname(resolved));
      if (!metadata || !animation || animation.frameCount > MAX_IMAGE_FRAMES) {
        throw new Error(path.extname(resolved).toLowerCase() === ".webm"
          ? "WebM must contain one unencrypted VP8/VP9 video track with valid blocks, within 10 minutes, 120 FPS and dimension limits."
          : path.extname(resolved).toLowerCase() === ".mp4"
          ? "MP4 must be non-fragmented H.264/AVC or H.265/HEVC, within 10 minutes, 120 FPS and dimension limits."
          : "Media metadata is invalid or exceeds the image/video safety limits");
      }
      console.log(JSON.stringify({ ...metadata, ...animation }));
    } catch (error) {
      console.error(error?.message ?? String(error));
      process.exitCode = 2;
    }
  }
}
