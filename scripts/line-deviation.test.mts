import test from 'node:test';
import assert from 'node:assert/strict';
import {
  applyTransform,
  aggregateDeviations,
  computeDeviationSeries,
  countNearStops,
  detectStationaryPoints,
  fitRigid2D,
  readPoseSamples,
  type MarkerPoint,
  type PoseSample,
  type ReferencePoint,
  type SpeedRow,
  type Vec2,
} from './line-deviation.mts';

// フィールド座標 → 追跡座標の既知の変換（回転25度＋平行移動）で合成データを作る
const THETA = (25 * Math.PI) / 180;
const COS = Math.cos(THETA);
const SIN = Math.sin(THETA);
const T = { x: 2.5, z: -1.0 };
function toTracking(p: Vec2): Vec2 {
  return { x: COS * p.x - SIN * p.z + T.x, z: SIN * p.x + COS * p.z + T.z };
}

const REFERENCE: ReferencePoint[] = [
  ...[0, 1, 2, 3, 4, 5].map((z) => ({ x: 0, z, segment: 'A' })),
  ...[0, 1, 2, 3].map((x) => ({ x, z: 6, segment: 'B' })),
];
const MARKERS: MarkerPoint[] = [
  { name: 'M1', x: 0, z: 0 },
  { name: 'M2', x: 0, z: 6 },
  { name: 'M3', x: 3, z: 6 },
];

function noise(i: number): Vec2 {
  return { x: 0.002 * Math.sin(i * 1.7), z: 0.002 * Math.cos(i * 2.3) };
}

test('fitRigid2D recovers a known transform with zero residual', () => {
  const measured = MARKERS.map(toTracking);
  const { transform, residualsM, rmsResidualM } = fitRigid2D(measured, MARKERS);
  assert.ok(rmsResidualM < 1e-9, `rms=${rmsResidualM}`);
  assert.ok(residualsM.every((r) => r < 1e-9));
  const p = applyTransform(transform, toTracking({ x: 1.5, z: 3 }));
  assert.ok(Math.abs(p.x - 1.5) < 1e-9 && Math.abs(p.z - 3) < 1e-9);
});

test('stationary detection finds the three marker placements in order', () => {
  const samples: PoseSample[] = [];
  let t = 0;
  let i = 0;
  const dt = 1 / 90;
  MARKERS.forEach((m, mi) => {
    const c = toTracking(m);
    for (let k = 0; k < 270; k++, i++, t += dt) {
      const n = noise(i);
      samples.push({ t, x: c.x + n.x, z: c.z + n.z });
    }
    if (mi + 1 < MARKERS.length) {
      const next = toTracking(MARKERS[mi + 1]);
      for (let k = 0; k < 90; k++, i++, t += dt) {
        const s = k / 90;
        samples.push({ t, x: c.x + (next.x - c.x) * s, z: c.z + (next.z - c.z) * s });
      }
    }
  });
  const points = detectStationaryPoints(samples);
  assert.equal(points.length, 3);
  MARKERS.forEach((m, mi) => {
    const expected = toTracking(m);
    assert.ok(Math.abs(points[mi].x - expected.x) < 0.005, `marker ${m.name} x`);
    assert.ok(Math.abs(points[mi].z - expected.z) < 0.005, `marker ${m.name} z`);
  });
});

test('deviation series reproduces known lateral offsets per segment', () => {
  const measured = MARKERS.map(toTracking);
  const { transform } = fitRigid2D(measured, MARKERS);
  const fieldPath: Vec2[] = [];
  for (let z = 0; z <= 6; z += 0.05) fieldPath.push({ x: -0.1, z }); // A: 左へ0.10 m
  for (let x = 0.05; x <= 3; x += 0.05) fieldPath.push({ x, z: 6.05 }); // B: 左へ0.05 m
  const samples: PoseSample[] = fieldPath.map((p, i) => {
    const q = toTracking(p);
    return { t: i * 0.05, x: q.x, z: q.z };
  });
  const series = computeDeviationSeries(REFERENCE, transform, samples);
  const aggregates = aggregateDeviations(series);
  const a = aggregates.find((g) => g.segment === 'A');
  const b = aggregates.find((g) => g.segment === 'B');
  assert.ok(a && b);
  assert.ok(Math.abs(a.rmsM - 0.1) < 0.01, `A rms=${a.rmsM}`);
  assert.ok(Math.abs(a.maeM - 0.1) < 0.01, `A mae=${a.maeM}`);
  assert.ok(Math.abs(b.rmsM - 0.05) < 0.01, `B rms=${b.rmsM}`);
  const aRows = series.filter((r) => r.segment === 'A');
  assert.ok(aRows.every((r) => r.deviationM > 0.08), 'Aのズレは左側（正）のはず');
});

test('readPoseSamples keeps only valid rows of the requested role', () => {
  const text = [
    'acquired_time_s,input_system_time_s,role,key,runtime_device_id,tracking_valid,x_m,y_m,z_m,qx,qy,qz,qw,invalid_reason',
    '2.0,,Chassis,k1,21,true,0.5,0.9,3.0,0,0,0,1,',
    '1.0,,LeftWheel,k2,22,true,0.1,0.3,3.0,0,0,0,1,',
    '3.0,,Chassis,k1,21,false,,,,,,,,missing_pose',
  ].join('\n');
  const samples = readPoseSamples(text, 'Chassis');
  assert.equal(samples.length, 1);
  assert.equal(samples[0].t, 2.0);
  assert.equal(samples[0].x, 0.5);
});

test('countNearStops counts only stops at or above the minimum duration', () => {
  const rows: SpeedRow[] = [];
  const push = (t0: number, t1: number, left: number, right: number) => {
    for (let t = t0; t <= t1; t += 0.01) {
      rows.push({ t, valid: true, leftFilteredRps: left, rightFilteredRps: right });
    }
  };
  push(0, 1, 0.2, 0.2); // 走行
  push(1.01, 2.21, 0, 0); // 1.2秒の停止 → 数える
  push(2.22, 3, 0.2, 0.2); // 走行
  push(3.01, 3.31, 0, 0.01); // 0.3秒の停止 → 数えない
  push(3.32, 4, -0.2, -0.2); // 走行
  const result = countNearStops(rows);
  assert.equal(result.episodes, 1);
  assert.ok(Math.abs(result.totalS - 1.2) < 0.05, `total=${result.totalS}`);
});
