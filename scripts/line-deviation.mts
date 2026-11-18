// 線課題のズレ解析（オフライン試作）
//
// 使い方:
//   node scripts/line-deviation.mts \
//     --reference <基準線.csv> \          x_m,z_m,segment の列（フィールド座標、進行順）
//     --markers <マーカー.csv> \          name,x_m,z_m の列（固定マーカー3点のフィールド座標）
//     --check-poses <機器チェックのposes.csv> \
//     --marker-key <マーカーに置いたトラッカーのkeyの一部> \
//     --trial-poses <試行のposes.csv> \
//     [--trial-speeds <試行のspeeds.csv>] [--trial-offset] \
//     [--marker-select 0,1,2] [--out-prefix <出力先prefix>]
//
// 手順:
//   1. 機器チェックの poses.csv から、マーカーに置かれたトラッカーの静止位置を検出する
//      （マーカーの上では数秒静止させる。検出順＝置いた順としてマーカーと対応付ける）
//   2. フィールド座標のマーカー3点と実測3点から、2次元剛体変換（回転＋平行移動）を最小二乗で求める
//   3. 試行の車体（Chassis）軌跡をフィールド座標へ変換し、基準線の最も近い区間までの
//      符号付き距離（進行方向の左が正）を各時刻について計算する
//   4. 試行全体と区間ごとに RMS・平均絶対値・最大ズレ・所要時間・平均速度を集計する
//   5. speeds.csv があれば「ほぼ停止」（左右の平滑速度が両方とも閾値未満）の回数と合計時間も数える
//
// 座標: フィールド座標は床面の (x, z)。x はスタート方向を向いたときの右、z はスタート方向。
// 基準線が閉ループの場合は、終端に始点をもう一度書くこと。

import { readFileSync, writeFileSync } from 'node:fs';

export interface Vec2 {
  x: number;
  z: number;
}

export interface PoseSample extends Vec2 {
  t: number;
}

export interface ReferencePoint extends Vec2 {
  segment: string;
}

export interface MarkerPoint extends Vec2 {
  name: string;
}

export interface Transform2D {
  cos: number;
  sin: number;
  tx: number;
  tz: number;
}

// ---------- CSV ----------

// 最小限のRFC 4180風パーサ（引用符とカンマのエスケープに対応）
export function parseCsv(text: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let field = '';
  let quoted = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (quoted) {
      if (c === '"') {
        if (text[i + 1] === '"') {
          field += '"';
          i++;
        } else {
          quoted = false;
        }
      } else {
        field += c;
      }
    } else if (c === '"') {
      quoted = true;
    } else if (c === ',') {
      row.push(field);
      field = '';
    } else if (c === '\n') {
      row.push(field);
      field = '';
      rows.push(row);
      row = [];
    } else if (c !== '\r') {
      field += c;
    }
  }
  if (field !== '' || row.length > 0) {
    row.push(field);
    rows.push(row);
  }
  return rows.filter((r) => r.length > 1 || (r.length === 1 && r[0] !== ''));
}

function columnIndex(header: string[], name: string): number {
  const i = header.indexOf(name);
  if (i < 0) throw new Error(`CSVに列 ${name} がありません`);
  return i;
}

// poses.csv から、指定した role（と任意で key の部分一致）の有効サンプルだけを取り出す
export function readPoseSamples(text: string, role: string, keyIncludes?: string): PoseSample[] {
  const rows = parseCsv(text);
  const header = rows[0];
  const it = columnIndex(header, 'acquired_time_s');
  const irole = columnIndex(header, 'role');
  const ikey = columnIndex(header, 'key');
  const ivalid = columnIndex(header, 'tracking_valid');
  const ix = columnIndex(header, 'x_m');
  const iz = columnIndex(header, 'z_m');
  const out: PoseSample[] = [];
  for (const r of rows.slice(1)) {
    if (r[irole] !== role) continue;
    if (keyIncludes && !(r[ikey] ?? '').includes(keyIncludes)) continue;
    if (r[ivalid] !== 'true') continue;
    if (r[ix] === '' || r[iz] === '') continue;
    const x = Number(r[ix]);
    const z = Number(r[iz]);
    const t = Number(r[it]);
    if (!Number.isFinite(x) || !Number.isFinite(z) || !Number.isFinite(t)) continue;
    out.push({ t, x, z });
  }
  return out.sort((a, b) => a.t - b.t);
}

export function readReference(text: string): ReferencePoint[] {
  const rows = parseCsv(text);
  const header = rows[0];
  const ix = columnIndex(header, 'x_m');
  const iz = columnIndex(header, 'z_m');
  const iseg = columnIndex(header, 'segment');
  return rows.slice(1).map((r) => {
    const p = { x: Number(r[ix]), z: Number(r[iz]), segment: r[iseg] ?? '' };
    if (!Number.isFinite(p.x) || !Number.isFinite(p.z)) throw new Error('基準線に数値でない座標があります');
    return p;
  });
}

export function readMarkers(text: string): MarkerPoint[] {
  const rows = parseCsv(text);
  const header = rows[0];
  const iname = columnIndex(header, 'name');
  const ix = columnIndex(header, 'x_m');
  const iz = columnIndex(header, 'z_m');
  return rows.slice(1).map((r) => {
    const p = { name: r[iname] ?? '', x: Number(r[ix]), z: Number(r[iz]) };
    if (!Number.isFinite(p.x) || !Number.isFinite(p.z)) throw new Error('マーカーに数値でない座標があります');
    return p;
  });
}

// ---------- 静止点の検出 ----------

export interface StationaryOptions {
  windowS?: number;
  radiusM?: number;
  minDurationS?: number;
  mergeDistanceM?: number;
}

// 静止している区間の重心を、現れた順に返す。マーカー上に数秒置かれたトラッカーの位置を得るためのもの
export function detectStationaryPoints(samples: PoseSample[], opts: StationaryOptions = {}): Vec2[] {
  const windowS = opts.windowS ?? 0.5;
  const radiusM = opts.radiusM ?? 0.02;
  const minDurationS = opts.minDurationS ?? 1.5;
  const mergeDistanceM = opts.mergeDistanceM ?? 0.3;
  if (samples.length === 0) return [];
  const still: boolean[] = new Array(samples.length).fill(false);
  let j = 0;
  for (let i = 0; i < samples.length; i++) {
    if (j < i) j = i;
    while (j + 1 < samples.length && samples[j + 1].t <= samples[i].t + windowS) j++;
    if (samples[j].t - samples[i].t >= windowS * 0.9) {
      const dx = samples[j].x - samples[i].x;
      const dz = samples[j].z - samples[i].z;
      still[i] = dx * dx + dz * dz <= radiusM * radiusM;
    }
  }
  const centroids: Vec2[] = [];
  let start = -1;
  const flush = (end: number) => {
    if (start < 0) return;
    if (samples[end].t - samples[start].t >= minDurationS) {
      let x = 0;
      let z = 0;
      let n = 0;
      for (let k = start; k <= end; k++) {
        x += samples[k].x;
        z += samples[k].z;
        n++;
      }
      centroids.push({ x: x / n, z: z / n });
    }
    start = -1;
  };
  for (let i = 0; i < samples.length; i++) {
    if (still[i] && start < 0) start = i;
    if (!still[i] && start >= 0) flush(i - 1);
  }
  if (start >= 0) flush(samples.length - 1);
  const merged: Vec2[] = [];
  for (const c of centroids) {
    const found = merged.some(
      (m) => (m.x - c.x) * (m.x - c.x) + (m.z - c.z) * (m.z - c.z) <= mergeDistanceM * mergeDistanceM,
    );
    if (!found) merged.push(c);
  }
  return merged;
}

// ---------- 剛体変換 ----------

// from（実測）→ to（フィールド）への2次元剛体変換を Kabsch 法で求める
export function fitRigid2D(
  from: Vec2[],
  to: Vec2[],
): { transform: Transform2D; residualsM: number[]; rmsResidualM: number } {
  if (from.length !== to.length || from.length < 2) {
    throw new Error('変換には同数の点の組が2点以上必要です');
  }
  const n = from.length;
  const cf = { x: from.reduce((s, p) => s + p.x, 0) / n, z: from.reduce((s, p) => s + p.z, 0) / n };
  const ct = { x: to.reduce((s, p) => s + p.x, 0) / n, z: to.reduce((s, p) => s + p.z, 0) / n };
  let dot = 0;
  let cross = 0;
  for (let i = 0; i < n; i++) {
    const ax = from[i].x - cf.x;
    const az = from[i].z - cf.z;
    const bx = to[i].x - ct.x;
    const bz = to[i].z - ct.z;
    dot += ax * bx + az * bz;
    cross += ax * bz - az * bx;
  }
  const theta = Math.atan2(cross, dot);
  const cos = Math.cos(theta);
  const sin = Math.sin(theta);
  const transform: Transform2D = {
    cos,
    sin,
    tx: ct.x - (cos * cf.x - sin * cf.z),
    tz: ct.z - (sin * cf.x + cos * cf.z),
  };
  const residualsM = from.map((p, i) => {
    const q = applyTransform(transform, p);
    return Math.hypot(q.x - to[i].x, q.z - to[i].z);
  });
  const rmsResidualM = Math.sqrt(residualsM.reduce((s, r) => s + r * r, 0) / n);
  return { transform, residualsM, rmsResidualM };
}

export function applyTransform(tr: Transform2D, p: Vec2): Vec2 {
  return {
    x: tr.cos * p.x - tr.sin * p.z + tr.tx,
    z: tr.sin * p.x + tr.cos * p.z + tr.tz,
  };
}

// ---------- ズレ ----------

// 基準線の最も近い区間までの符号付き距離（進行方向の左が正）と、その区間名を返す
export function nearestDeviation(
  reference: ReferencePoint[],
  p: Vec2,
): { deviationM: number; segment: string } {
  let bestD2 = Infinity;
  let bestSigned = 0;
  let bestSegment = '';
  for (let i = 0; i + 1 < reference.length; i++) {
    const a = reference[i];
    const b = reference[i + 1];
    const vx = b.x - a.x;
    const vz = b.z - a.z;
    const len2 = vx * vx + vz * vz;
    let s = len2 > 0 ? ((p.x - a.x) * vx + (p.z - a.z) * vz) / len2 : 0;
    s = Math.max(0, Math.min(1, s));
    const wx = p.x - (a.x + s * vx);
    const wz = p.z - (a.z + s * vz);
    const d2 = wx * wx + wz * wz;
    if (d2 < bestD2) {
      bestD2 = d2;
      const cross = vx * wz - vz * wx;
      bestSigned = Math.sqrt(d2) * (cross >= 0 ? 1 : -1);
      bestSegment = a.segment;
    }
  }
  return { deviationM: bestSigned, segment: bestSegment };
}

export interface DeviationRow {
  t: number;
  x: number;
  z: number;
  deviationM: number;
  segment: string;
  dtS: number;
  stepM: number;
}

export function computeDeviationSeries(
  reference: ReferencePoint[],
  transform: Transform2D,
  samples: PoseSample[],
  trialOffset = false,
): DeviationRow[] {
  let { tx, tz } = transform;
  if (trialOffset && samples.length > 0) {
    const first = applyTransform(transform, samples[0]);
    tx += reference[0].x - first.x;
    tz += reference[0].z - first.z;
  }
  const tr: Transform2D = { ...transform, tx, tz };
  const rows: DeviationRow[] = [];
  let prev: Vec2 | null = null;
  for (let i = 0; i < samples.length; i++) {
    const p = applyTransform(tr, samples[i]);
    const { deviationM, segment } = nearestDeviation(reference, p);
    const dtS = i + 1 < samples.length ? samples[i + 1].t - samples[i].t : 0;
    const stepM = prev ? Math.hypot(p.x - prev.x, p.z - prev.z) : 0;
    rows.push({ t: samples[i].t, x: p.x, z: p.z, deviationM, segment, dtS, stepM });
    prev = p;
  }
  return rows;
}

export interface Aggregate {
  segment: string;
  samples: number;
  durationS: number;
  rmsM: number;
  maeM: number;
  maxAbsM: number;
  meanSpeedMps: number;
}

export function aggregateDeviations(rows: DeviationRow[]): Aggregate[] {
  const groups = new Map<string, DeviationRow[]>();
  for (const r of rows) {
    const g = groups.get(r.segment) ?? [];
    g.push(r);
    groups.set(r.segment, g);
  }
  const summarize = (segment: string, g: DeviationRow[]): Aggregate => {
    const durationS = g.reduce((s, r) => s + r.dtS, 0);
    const pathM = g.reduce((s, r) => s + r.stepM, 0);
    return {
      segment,
      samples: g.length,
      durationS,
      rmsM: Math.sqrt(g.reduce((s, r) => s + r.deviationM * r.deviationM, 0) / g.length),
      maeM: g.reduce((s, r) => s + Math.abs(r.deviationM), 0) / g.length,
      maxAbsM: Math.max(...g.map((r) => Math.abs(r.deviationM))),
      meanSpeedMps: durationS > 0 ? pathM / durationS : 0,
    };
  };
  const out = [...groups.entries()].map(([segment, g]) => summarize(segment, g));
  if (rows.length > 0) out.push(summarize('ALL', rows));
  return out;
}

// ---------- ほぼ停止 ----------

export interface SpeedRow {
  t: number;
  valid: boolean;
  leftFilteredRps: number;
  rightFilteredRps: number;
}

export function readSpeedRows(text: string): SpeedRow[] {
  const rows = parseCsv(text);
  const header = rows[0];
  const it = columnIndex(header, 'software_time_s');
  const ivalid = columnIndex(header, 'valid');
  const il = columnIndex(header, 'left_filtered_rps');
  const ir = columnIndex(header, 'right_filtered_rps');
  const out: SpeedRow[] = [];
  for (const r of rows.slice(1)) {
    const left = Number(r[il]);
    const right = Number(r[ir]);
    out.push({
      t: Number(r[it]),
      valid: r[ivalid] === 'True' || r[ivalid] === 'true',
      leftFilteredRps: Number.isFinite(left) ? left : NaN,
      rightFilteredRps: Number.isFinite(right) ? right : NaN,
    });
  }
  return out.sort((a, b) => a.t - b.t);
}

// 左右の平滑速度が両方とも閾値未満の状態が minDurationS 以上続いた回数と合計時間
export function countNearStops(
  rows: SpeedRow[],
  opts: { thresholdRps?: number; minDurationS?: number } = {},
): { episodes: number; totalS: number; durationsS: number[] } {
  const threshold = opts.thresholdRps ?? 0.03;
  const minDuration = opts.minDurationS ?? 0.5;
  const durationsS: number[] = [];
  let startT: number | null = null;
  let lastT = 0;
  const flush = () => {
    if (startT !== null && lastT - startT >= minDuration) durationsS.push(lastT - startT);
    startT = null;
  };
  for (const r of rows) {
    const stopped =
      r.valid &&
      Number.isFinite(r.leftFilteredRps) &&
      Number.isFinite(r.rightFilteredRps) &&
      Math.abs(r.leftFilteredRps) < threshold &&
      Math.abs(r.rightFilteredRps) < threshold;
    if (stopped) {
      if (startT === null) startT = r.t;
      lastT = r.t;
    } else {
      flush();
    }
  }
  flush();
  return {
    episodes: durationsS.length,
    totalS: durationsS.reduce((s, d) => s + d, 0),
    durationsS,
  };
}

// ---------- CLI ----------

function argValue(args: string[], name: string): string | undefined {
  const i = args.indexOf(name);
  return i >= 0 ? args[i + 1] : undefined;
}

export function main(argv: string[]): void {
  const referencePath = argValue(argv, '--reference');
  const markersPath = argValue(argv, '--markers');
  const checkPosesPath = argValue(argv, '--check-poses');
  const markerKey = argValue(argv, '--marker-key');
  const trialPosesPath = argValue(argv, '--trial-poses');
  if (!referencePath || !markersPath || !checkPosesPath || !markerKey || !trialPosesPath) {
    throw new Error(
      '必須の引数: --reference --markers --check-poses --marker-key --trial-poses（詳細はファイル冒頭の使い方）',
    );
  }
  const reference = readReference(readFileSync(referencePath, 'utf8'));
  const markers = readMarkers(readFileSync(markersPath, 'utf8'));
  const checkSamples = readPoseSamples(
    readFileSync(checkPosesPath, 'utf8'),
    argValue(argv, '--check-role') ?? 'Unassigned',
    markerKey,
  );
  let measured = detectStationaryPoints(checkSamples);
  const select = argValue(argv, '--marker-select');
  if (select) {
    const indices = select.split(',').map((s) => Number(s.trim()));
    measured = indices.map((i) => {
      const p = measured[i];
      if (!p) throw new Error(`--marker-select の index ${i} に対応する静止点がありません`);
      return p;
    });
  }
  if (measured.length !== markers.length) {
    throw new Error(
      `静止点が ${measured.length} 個検出されましたが、マーカーは ${markers.length} 個です。` +
        `検出位置: ${JSON.stringify(measured)}（--marker-select で使う静止点を選べます）`,
    );
  }
  const { transform, residualsM, rmsResidualM } = fitRigid2D(measured, markers);
  const trialSamples = readPoseSamples(readFileSync(trialPosesPath, 'utf8'), 'Chassis');
  const series = computeDeviationSeries(reference, transform, trialSamples, argv.includes('--trial-offset'));
  const aggregates = aggregateDeviations(series);
  const speedsPath = argValue(argv, '--trial-speeds');
  const nearStops = speedsPath
    ? countNearStops(readSpeedRows(readFileSync(speedsPath, 'utf8')))
    : undefined;
  const outPrefix = argValue(argv, '--out-prefix');
  if (outPrefix) {
    const lines = ['t_s,x_m,z_m,deviation_m,segment'];
    for (const r of series) {
      lines.push(
        `${r.t.toFixed(3)},${r.x.toFixed(4)},${r.z.toFixed(4)},${r.deviationM.toFixed(4)},${r.segment}`,
      );
    }
    writeFileSync(`${outPrefix}-timeseries.csv`, lines.join('\n') + '\n');
  }
  console.log(
    JSON.stringify(
      {
        markers: markers.map((m, i) => ({
          name: m.name,
          field: { x: m.x, z: m.z },
          measured: measured[i],
          residualM: residualsM[i],
        })),
        rmsResidualM,
        trialSamples: trialSamples.length,
        aggregates,
        nearStops,
      },
      null,
      2,
    ),
  );
}

if (process.argv[1] && import.meta.url === new URL(`file://${process.argv[1]}`).href) {
  main(process.argv.slice(2));
}
