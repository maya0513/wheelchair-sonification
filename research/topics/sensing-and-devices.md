# センサー・装置実装の読み方

## 安価さと必要な情報を対応させる

[L16](../papers/L16-guo-optipush-2011.md)は力学センサー＋角エンコーダ、[L25](../papers/L25-ojeda-2014.md)は身体加速度、[L27](../papers/L27-fathian-timing-2024.md)は手のIMU、[L50](../papers/L50-niehorster-2017.md)はViveの位置姿勢を検証している。それぞれが測るものは異なる。低価格の位置計測装置で、力・トルク・接触をすべて直接測れるわけではない。

| 実装の層 | 借りられる具体例 |
|---|---|
| 取り付け・校正 | 輪径、車輪中心、センサー軸、手とセンサーのずれ、動画参照 |
| 取得 | [L05](../papers/L05-fujii-2016.md) 200 Hz、[L16](../papers/L16-guo-optipush-2011.md) 200 Hz、[L28](../papers/L28-fathian-pattern-2025.md) 100 Hzなど。手元の実測周波数を別に記録 |
| イベント検出 | トルク1 Nmの例は[L16](../papers/L16-guo-optipush-2011.md)。位置だけの検出に同じ閾値は使えない |
| 安定化 | [L16](../papers/L16-guo-optipush-2011.md)の5ストローク平均、[L52](../papers/L52-vandenberghe-2021.md)の5歩平均、[L33](../papers/L33-kimura-2016.md)の数状態への離散化 |
| 音生成 | Pure Data [L13](../papers/L13-gref-2016.md)、STK [L11](../papers/L11-scholz-2016.md)、OSC＋Max/MSP [L52](../papers/L52-vandenberghe-2021.md)、Unity [D03](../papers/D03-unity-audio.md) |
| 検証 | 回数・頻度・角度・欠損・遅延を別々に確認する |

## オンラインと事後解析を分ける

ゼロ位相・no-lagフィルタは未来サンプルを使う場合があり、[L28](../papers/L28-fathian-pattern-2025.md)のオフライン処理をそのまま逐次処理へ移せない。[L25](../papers/L25-ojeda-2014.md)の試行全体の平均・SDに基づく閾値も同様。オンラインは因果的な処理、事後の精査は別系列としてログから再現する。

「処理周期10 ms」「90 Hz描画」「Bluetooth接続」は、身体の動きから聞こえる音までの遅延ではない。[L05](../papers/L05-fujii-2016.md) [L50](../papers/L50-niehorster-2017.md)。音のプリロード、DSPバッファ、無線、描画更新、検出窓の遅れを区別する。[D03](../papers/D03-unity-audio.md)。同期には[L45](../papers/L45-kothe-2025.md) [D05](../papers/D05-lsl-time.md)が使えるが、機器内部遅延は残る。

## 推奨する作業順

実物の型番・個数確認 → 実際の取得周期と追跡可能範囲の計測 → 座標・中心・接触推定の検証 → 1変数のFB → 記録・同期 → 本計測。音楽・3D音場・自動パターン分類の追加は、この順序の後に置く。

比較表は[装置比較](device-comparison.md)、本研究の要件は[FB仕様](../../docs/specifications/feedback.md)と[検証計画](../../docs/specifications/validation.md)。仮の検出法は[区間終了後FBの検討案](../../docs/archive/terminal-feedback-system.md)に分けている。開発の進め方は[開発ガイド](../../docs/development/guide.md)を参照する。
