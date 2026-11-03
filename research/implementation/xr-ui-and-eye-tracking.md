# XR画面と視線記録の根拠

## 準備画面と操作の根拠

対象：Unity 6000.6.4f1、XRI 3.4.1、VIVE OpenXR 2.5.1。確認日：2026-10-28。公式資料の内容と、本研究で選んだ設計を区別する。

| 資料で確認したこと | 本アプリでの採用 |
|---|---|
| [UnityのXR UI設定](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.4/manual/ui-setup.html)：World Space Canvas、XR UI Input Module、追跡機器向けのUI入力 | 既存XRIでポインター・トリガー選択。PCとHMDは共通の準備状態を表示する |
| [XRI Starter Assets](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.4/manual/samples-starter-assets.html)：典型的なXR操作のサンプルと再利用可能な部品 | 操作・文字・余白を揃えた空間パネル。別のUI SDKは追加しない |
| [Microsoftの快適性指針](https://learn.microsoft.com/en-us/windows/mixed-reality/design/comfort)：近すぎる表示、奥行きの頻繁な変化、首・腕の負担に配慮 | 停止中に読めるパネル、動作中の簡潔な案内、コントローラーを置ける準備時間 |
| [HTCのトラッカー説明](https://developer.vive.com/resources/openxr/unity/tutorials/tracker-overview/vivexrtracker/)：SDKの機器区別と姿勢取得 | SDK番号を身体の役割として扱わず、動作から候補を出して実物で照合する |

Microsoftの数値はHoloLens等の条件を含む。Focus Visionでの最適距離・可読性の実証として転用しない。本アプリの表示距離・視角・判定閾値は試作用設定で、実機確認が必要。

前後進による自動候補生成は本研究の実装上の提案。上の資料がこの識別手法の精度を検証したものではない。複数候補や欠測では自動確定せず、実物選択へ移る。機能チェックと独立参照による角度・速度・出力遅延の検証を分ける。

設計の正本は[装着・チェック](../../docs/operations/setup.md)、責務の境界は[開発ガイド](../../docs/development/README.md)、研究用性能は[検証計画](../../docs/specifications/validation.md)を参照。

## 表示配置と視線記録の根拠

確認日：2026-11-02。現行のUnity 6000.6.4f1・VIVE OpenXR 2.5.1を対象とする。研究結果、メーカー指針、本研究で選ぶ設定を区別する。

### 配置・透明度の根拠

| 一次資料と確認位置 | 確認したこと | 適用の限界 |
|---|---|---|
| [Rice et al. 2010](../papers/L59-rice-2010.md)、Methods・Figure 3 | SmartWheelと正面モニターで縦バーによる速度等の提示。専用測定輪のリアルタイム値と目標を比較 | 固定ダイナモメータ、1例。HMDでの配置や透明度の検証ではない。既存ノート[L59](../papers/L59-rice-2010.md) |
| [Rzayev et al. 2018](../papers/L67-rzayev-2018.md)、本文Apparatus・Results。[大学公開本文](https://teaching.vda.univie.ac.at/hci/20s/materials/Lit-Tests/LT2-Rzayev2018.pdf) | 24名、HoloLensで座位・歩行中の読字を比較。上右・中央・下中央を比較し、上右では理解と負担に不利な結果 | 文章を読む課題。左右の連続ゲージの比較ではなく、車いすの最適配置を証明しない |
| [Shin & Park 2024](../papers/L68-shin-park-2024.md)、全文Sections 3–4 | 29名、Quest Pro、25 mの往復歩行中に動画。中央／下方27°、不透明度33／66／100%。位置の有意差なし、66%は33%より満足度・負担で良い評価 | 50 cm先の動画画面、主観評価中心。細いバーに66%をそのまま最適値として移せない |
| [He et al. 2025](../papers/L69-he-2025.md)、本文Sections 3, 5 | 20名の自転車走行、HoloLensで上・右・下の短い通知を比較。下は見つけやすさ等で好評価。一方、表示なしに比べ外部音への反応が遅延 | 通知と連続FBは異なる。障害物を置かないコースで、コーンや路面を隠す問題を否定しない |
| [Microsoft Coordinate systems](https://learn.microsoft.com/en-gb/windows/mixed-reality/design/coordinate-systems)、Avoid head-locked content | 頭部に強く固定するHUDを避け、ユーザーや物体・空間に関連付ける指針 | メーカーの一般指針で、Focus Visionの車いす試験の結果ではない |

### 本研究で選ぶ設計

背景と棒が混ざって長さを読み取りにくいという利用者の実機報告に合わせ、塗りとトラックの不透明度だけを上げる。位置・幅・形は維持し、外側への移動や曲線化は追加しない。数字・目標・大きな背景パネルも加えない。値と配置の正本は[FB仕様](../../docs/specifications/feedback.md)。調整後の画像は実装と同じPrefabから生成し、改善したかは実機で見比べる。

追加確認日：2026-11-03。

| 一次資料と閲覧範囲 | 判断に使うこと | 適用の限界 |
|---|---|---|
| [Pascale et al. 2018](https://pubmed.ncbi.nlm.nih.gov/30098632/)、PubMed抄録。Applied Ergonomics 73, 167–173、DOI: 10.1016/j.apergo.2018.06.002 | 単眼HWDでは周辺刺激の検出がモニターより難しく、外側・低コントラストほど不利。コントラストを高めることで一部軽減 | 抄録のみ確認。単眼透過型の検出課題で、Focus Visionの両眼VST・連続バーの最適値を示すものではない |
| [HTC Focus Vision仕様](https://www.vive.com/us/product/vive-focus-vision/overview/)、表示・パススルーの説明 | 片眼2448×2448、最大120°、90 Hz、ステレオカラーパススルー | 公称値は、レンズ越しの局所的な読みやすさや、背景とのコントラストを保証しない。最大FOVからバーの実効画素数を割り算しない |
| [Unity uGUI 2.0 Slider](https://docs.unity3d.com/Packages/com.unity.ugui@2.0/manual/script-Slider.html)、Fill Rect・Direction | 背景と伸縮する塗りを分け、下端固定で同じ尺度を示す既存UIの構成を参考にする | 本アプリは読み取り専用のImageを使う。操作ハンドルや別のUI SDKは加えず、公式サンプルを視認性の実証とは扱わない |

周辺に移せば自然に見える、曲げれば読みやすい、とは判断しない。位置や形を同時に変えると不透明度の調整を評価しにくくなるため、まず同じ配置で背景との混ざりを確かめる。走路・床・コーンが見えることと、左右の速度差を読み取れることを別々に確認する。

位置は座位の頭部基準で初期設定し、車体トラッカーへの相対姿勢として保存する。車体とともに動き、頭を横へ向けると視野から外れる。視線に応じた移動・表示濃度の変更は行わない。設定値の正本は[FB仕様](../../docs/specifications/feedback.md)。予備確認で視認・負担を確認し、本計測前に固定する。

### 視線データの公式経路

[HTC Eye Tracker公式チュートリアル](https://developer.vive.com/resources/openxr/unity/tutorials/face-data/getting-the-data-of-eye-tracker/)のSupported PlatformsはFocus Vision AIOをSDK 2.5.0以降の対応対象とする。現在の2.5.1の`ViveEyeTracker.cs`と`ViveEyeTrackerHelper.cs`も確認した。

- Androidで`ViveEyeTracker`／`XR_HTC_eye_tracker`を有効にする。既存のSDKを使い、別の眼球SDKは追加しない。
- 公開配列APIの`GetEyeGazeData`、`GetEyePupilData`、`GetEyeGeometricData`を個別に呼ぶ。SDKの有効フラグ・API成功を区別する。片眼・瞳孔だけの欠測もありうる。
- SDKヘルパーに従いOpenXR座標をUnity座標へ変換する。瞳孔径はmm、瞳孔位置は[0,1]のセンサー座標（+X右、+Y上）、眼の開き等はSDK値。
- 配列APIにはネイティブ時刻が露出しない。内部は予測表示時刻を利用するため、Unityで値を得た時刻をセンサー測定時刻と称さない。取得周期・描画周期を記録し、120 Hzで取得できたとは主張しない。
- [HTC標準校正手順](https://www.vive.com/us/support/focusvision/category_howto/eye-tracking.html)は参加者ごとの校正と、IPD・HMD位置変更後の再校正を案内している。アプリの「校正確認済み」は実施者による確認で、SDKの精度判定ではない。

後の群比較に備えて、全群・全時点で同じ取得処理を使う。眼球だけでなく、頭部・追跡座標・バーの配置・表示有無を残す。視線欠測は補完しない。視線データだけから注意・認知負荷を断定せず、実世界の何を見ていたかの分類には別の参照情報が必要になる。今回の実装は保存と取得品質の要約まで。[保存形式](../../docs/specifications/recording.md)
