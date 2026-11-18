// ゼミ進捗報告スライド（2026-11-18）
// 2026-11-11 ゼミ進捗報告の体裁を継承。本文は常体で統一する
#set page(width: 297mm, height: 167.6mm, margin: (x: 18mm, top: 13mm, bottom: 11mm), fill: white)
#set text(font: "Noto Sans CJK JP", size: 12.5pt, fill: black, lang: "ja")
#set par(leading: 0.85em, spacing: 1.05em)
#set list(indent: 0.5em, body-indent: 0.6em, spacing: 0.5em, marker: [・])
#set table(stroke: 0.5pt + black, inset: 5.5pt)

#let slide(title, body) = {
  text(size: 19pt, weight: "bold")[#title]
  v(2.5mm)
  line(length: 100%, stroke: 0.7pt + black)
  v(4.5mm)
  body
  pagebreak()
}

#let note(body) = text(size: 10pt)[#body]
#let sub(body) = text(size: 14pt, weight: "bold")[#body]

// ---------- 1. 表紙 ----------
#v(30mm)
#text(size: 24pt, weight: "bold")[リアルタイム聴覚フィードバックを用いた\ 車いす推進技能学習の効果検証]
#v(9mm)
#line(length: 100%, stroke: 0.7pt + black)
#v(9mm)
#text(size: 14pt)[
  上山 颯太\
  ゼミ進捗報告　2026年11月18日
]
#pagebreak()

// ---------- 2. 課題の見直し ----------
#text(size: 19pt, weight: "bold")[課題の見直し]
#v(2.5mm)
#line(length: 100%, stroke: 0.7pt + black)
#v(4.5mm)
#[
  - タスクの変更: 線課題の追加（床に線を引き、線の中心を維持して走行し、軌跡からズレを評価する）
  - 線の位置をアプリケーションの起動毎に一致させる実装を考え中。起動時に数点にトラッカーを置き、点から座標変換を求める方法を検討中
]
#pagebreak()

// ---------- 3. 線課題と研究テーマ ----------
#text(size: 19pt, weight: "bold")[線課題と研究テーマ]
#v(2.5mm)
#line(length: 100%, stroke: 0.7pt + black)
#v(4.5mm)
#[
  - 線課題のズレは左右車輪速度の不均衡から生まれるため、FBが提示する変数の制御の上達を直接測れる
  - 誤差の減少で技能を測るのは運動学習研究の標準的な方法であり、テーマに合致する
  // - 制約: 主張できるのは要素技能（直進・経路の維持）の範囲までで、車いす操作全般の学習とは区別する
]
