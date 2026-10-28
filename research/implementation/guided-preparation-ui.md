# 案内付き準備とXR UIの根拠

対象：Unity 6000.6.4f1、XRI 3.4.1、VIVE OpenXR 2.5.1。確認日：2026-10-28。公式資料の内容と、本研究で選んだ設計を区別する。

| 資料で確認したこと | 本アプリでの採用 |
|---|---|
| [UnityのXR UI設定](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.4/manual/ui-setup.html)：World Space Canvas、XR UI Input Module、追跡機器向けのUI入力 | 既存XRIでポインター・トリガー選択。PCとHMDは共通の準備状態を表示する |
| [XRI Starter Assets](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.4/manual/samples-starter-assets.html)：典型的なXR操作のサンプルと再利用可能な部品 | 操作・文字・余白を揃えた空間パネル。別のUI SDKは追加しない |
| [Microsoftの快適性指針](https://learn.microsoft.com/en-us/windows/mixed-reality/design/comfort)：近すぎる表示、奥行きの頻繁な変化、首・腕の負担に配慮 | 停止中に読めるパネル、動作中の簡潔な案内、コントローラーを置ける準備時間 |
| [HTCのトラッカー説明](https://developer.vive.com/resources/openxr/unity/tutorials/tracker-overview/vivexrtracker/)：SDKの機器区別と姿勢取得 | SDK番号を身体の役割として扱わず、動作から候補を出して実物で照合する |

Microsoftの数値はHoloLens等の条件を含む。Focus Visionでの最適距離・可読性の実証として転用しない。本アプリの表示距離・視角・判定閾値は試作用設定で、実機確認が必要。

前後進による自動候補生成は本研究の実装上の提案。上の資料が、この識別手法の精度を検証したものではない。複数候補や欠測では自動確定せず、実物選択へ移る。機能チェックと独立参照による角度・速度・出力遅延の検証を分ける。

設計の正本は[装着・チェック](../../docs/development/tracker-measurement.md)、責務の境界は[開発ガイド](../../docs/development/guide.md)、研究用性能は[検証計画](../../docs/specifications/validation.md)を参照。
