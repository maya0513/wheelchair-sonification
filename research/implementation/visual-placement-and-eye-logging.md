# 走行中の表示配置と視線ログ

確認日：2026-11-02。現行のUnity 6000.6.4f1・VIVE OpenXR 2.5.1を対象とする。研究結果、メーカー指針、本研究で選ぶ設定を区別する。

## 配置・透明度の根拠

| 一次資料と確認位置 | 確認したこと | 適用の限界 |
|---|---|---|
| [Rice et al. 2010](../papers/L59-rice-2010.md)、Methods・Figure 3 | SmartWheelと正面モニターで縦バーによる速度等の提示。専用測定輪のリアルタイム値と目標を比較 | 固定ダイナモメータ、1例。HMDでの配置や透明度の検証ではない。既存ノート[L59](../papers/L59-rice-2010.md) |
| [Rzayev et al. 2018](../papers/L67-rzayev-2018.md)、本文Apparatus・Results。[大学公開本文](https://teaching.vda.univie.ac.at/hci/20s/materials/Lit-Tests/LT2-Rzayev2018.pdf) | 24名、HoloLensで座位・歩行中の読字を比較。上右・中央・下中央を比較し、上右では理解と負担に不利な結果 | 文章を読む課題。左右の連続ゲージの比較ではなく、車いすの最適配置を証明しない |
| [Shin & Park 2024](../papers/L68-shin-park-2024.md)、全文Sections 3–4 | 29名、Quest Pro、25 mの往復歩行中に動画。中央／下方27°、不透明度33／66／100%。位置の有意差なし、66%は33%より満足度・負担で良い評価 | 50 cm先の動画画面、主観評価中心。細いバーに66%をそのまま最適値として移せない |
| [He et al. 2025](../papers/L69-he-2025.md)、本文Sections 3, 5 | 20名の自転車走行、HoloLensで上・右・下の短い通知を比較。下は見つけやすさ等で好評価。一方、表示なしに比べ外部音への反応が遅延 | 通知と連続FBは異なる。障害物を置かないコースで、コーンや路面を隠す問題を否定しない |
| [Microsoft Coordinate systems](https://learn.microsoft.com/en-gb/windows/mixed-reality/design/coordinate-systems)、Avoid head-locked content | 頭部に強く固定するHUDを避け、ユーザーや物体・空間に関連付ける指針 | メーカーの一般指針で、Focus Visionの車いす試験の結果ではない |

## 本研究で選ぶ設計

左右の速度を見比べるため、離れた隅に分散せず、中央を空けて下部中央付近に細い縦バーを置く。大きな背景を除き、薄いトラックと、読める濃さの塗り・輪郭を分ける。透明なら注意を奪わない、下部なら安全、という主張はしない。

位置は座位の頭部基準で初期設定し、車体トラッカーへの相対姿勢として保存する。一般的な車の計器のように、車体とともに動き、頭を横へ向けると視野から外れる。視線に応じた移動・表示濃度の変更は行わない。設定値の正本は[FB仕様](../../docs/specifications/feedback.md)。予備確認で視認・負担を確認し、本計測前に固定する。

## 視線データの公式経路

[HTC Eye Tracker公式チュートリアル](https://developer.vive.com/resources/openxr/unity/tutorials/face-data/getting-the-data-of-eye-tracker/)のSupported PlatformsはFocus Vision AIOをSDK 2.5.0以降の対応対象とする。現在の2.5.1の`ViveEyeTracker.cs`と`ViveEyeTrackerHelper.cs`も確認した。

- Androidで`ViveEyeTracker`／`XR_HTC_eye_tracker`を有効にする。既存のSDKを使い、別の眼球SDKは追加しない。
- 公開配列APIの`GetEyeGazeData`、`GetEyePupilData`、`GetEyeGeometricData`を個別に呼ぶ。SDKの有効フラグ・API成功を区別する。片眼・瞳孔だけの欠測もありうる。
- SDKヘルパーに従いOpenXR座標をUnity座標へ変換する。瞳孔径はmm、瞳孔位置は[0,1]のセンサー座標（+X右、+Y上）、眼の開き等はSDK値。
- 配列APIにはネイティブ時刻が露出しない。内部は予測表示時刻を利用するため、Unityで値を得た時刻をセンサー測定時刻と称さない。取得周期・描画周期を記録し、120 Hzで取得できたとは主張しない。
- [HTC標準校正手順](https://www.vive.com/us/support/focusvision/category_howto/eye-tracking.html)は参加者ごとの校正と、IPD・HMD位置変更後の再校正を案内している。アプリの「校正確認済み」は実施者による確認で、SDKの精度判定ではない。

後の群比較に備えて、全群・全時点で同じ取得処理を使う。眼球だけでなく、頭部・追跡座標・バーの配置・表示有無を残す。視線欠測は補完しない。視線データだけから注意・認知負荷を断定せず、実世界の何を見ていたかの分類には別の参照情報が必要になる。今回の実装は保存と取得品質の要約まで。[保存形式](../../docs/development/research-records.md)
