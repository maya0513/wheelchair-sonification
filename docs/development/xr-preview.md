# 実機前の画面画像・模擬操作

被験者用画面はAPKと共通のUnity UI・SDFフォント・提示処理から撮影する。実施者用画面は配布するHTML/CSS/JavaScriptをChromeで描画して撮影する。HTMLでHMD画面を描き直す方式は使わない。

## 画像を作る・開く

```sh
just unity-preview
just experiment-console-preview
```

Unity Editorは閉じてから実行する。実施者用の撮影にはこのPCのGoogle Chromeを使い、必要なら`CHROME_BIN`で実行ファイルを指定する。通常のブラウザープロファイルを変更せず、一時プロファイルで撮影する。

| 出力 | 内容 |
|---|---|
| `.local/ui-preview/index.html` | 被験者用の全体画像・文字・ゲージの切り出し、拡大表示 |
| `.local/ui-preview/contact-sheet.png` | Unity画像の一覧 |
| `.local/ui-preview/manifest.json` | 仮の視野角・背景、ソース、文字溢れ等の検査結果 |
| `.local/console-preview/index.html` | PC画面、主ボタン・文字の切り出し |
| `.local/console-preview/overview.png` | PC画面の一覧 |
| `.local/console-preview/manifest.json` | Chromeの画面寸法・ソース・合成入力の明示 |

Releaseの`ui-preview.zip`には、入口の`index.html`と`headset/`・`operator/`を入れる。展開後の閲覧にはUnity・Node・ネット接続が不要である。実験記録や撮影用のログ・入力fixtureは画像ZIPへ含めない。

## 見る画面

| 被験者用：Unity | 実施者用：Chrome |
|---|---|
| 装着前の接続番号、停止中の短い待機表示 | 接続承認、実施前設定、研究用ID・入力・役割・校正 |
| V群の3状態、ラベルだけの期間、A・N群の無表示 | 次の課題と主操作、10秒準備、走行中、目標・理解確認・割付 |
| 8の字・評価・停止未確認時の無表示 | 質問紙、20分休憩、通信・追跡・保存異常、回収 |
| 仮の垂直視野角70°／90°、明暗背景、頭部回転・正面 | 1440×1100と1024×800等のPC画面、スクロールを含む配置 |

通常の被験者用画面には操作パネル、数値パッド、群名、ID、診断、保存先を出さない。ゲージ以外の追従ウィンドウも置かない。停止確認ができない異常では大きな診断画面を突然出さず、実施者が口頭で停止を指示する。

画像は生成するだけでなく、見切れ、文字欠け、重なり、切り出し、選択状態、ボタンと次の操作の一致を目視する。自動検査の成功だけで可読性を合格にしない。

## Editorで操作を試す

```sh
just experiment-console
just unity-preview-open
```

Editor専用のXR Interaction Simulatorを明示的に起動する。PCコンソールで接続承認し、実機なしの確認では合成入力を明示的に選ぶ。頭・左右コントローラーの仮想入力はシミュレーターで動かせるが、研究の進行操作はPCへ分離する。通常のAndroid実行にシミュレーターを含めない。

旧HMD設定パネルはEditorの技術確認にだけ残している。これを被験者へ使わせたり、本番の操作設計と混同したりしない。

## 実機との差

| 方法 | 確認する範囲 | 別途残る確認 |
|---|---|---|
| 固定時計・合成入力の自動テスト | 計算、判定、順序、取消、中断、保存 | 実測の誤差・実機の時間特性 |
| Unityの実描画画像 | 配置、文字、色、視野の仮定に対する見切れ | レンズ越しの可読性、実視野、パススルー |
| PC画面とEditor模擬操作 | 選択、画面遷移、接続、進行 | 追跡、実コントローラー、本体OS |
| PCとLinux Unityの実時間通し | 規定時間、通信・保存・回収の継続 | Android性能、5台、Wi-Fi範囲、電池、実際の被験者 |
| Focus Vision APK | 本体環境での成立 | 会場・走行中の精度、知覚、負担 |

差を一律の割合では表さない。パススルー映像の画質とUnityの文字描画も分ける。画像・PC通し試験は[実機の受入れ](../specifications/validation.md)や[予備実験](../operations/study-operations.md#本計測開始条件)の代わりにならない。

## 根拠

確認日2026-10-21。Unity 6000.6.4f1、XR Interaction Toolkit 3.4.1、TextMeshProは固定されたUGUI依存を使う。撮影の寸法と背景は本プロジェクトの確認条件で、機器の視野・光学系を保証する仕様ではない。

- [Unity：XR Interaction Simulator](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.4/manual/xr-interaction-simulator-overview.html)
- [Unity：URPのRender Requests](https://docs.unity3d.com/6000.6/Documentation/Manual/urp/User-Render-Requests.html)
- [Chrome：Headless mode](https://developer.chrome.com/docs/automation-and-testing/headless)
- [Unity：実行環境のProfiler確認](https://docs.unity3d.com/6000.6/Documentation/Manual/profiler-profiling-applications.html)
