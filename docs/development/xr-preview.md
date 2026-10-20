# VR画面を実機なしで確認する

APKと共通のUI・日本語SDFフォントをUnityで描画し、画像で配置を確認する。HTMLは撮影した画像の一覧であり、別のUI実装ではない。

## 画像一覧を作る

Unity Editorを閉じ、リポジトリ直下で実行する。

```sh
just unity-preview
```

完了後に`.local/ui-preview/index.html`をブラウザーで開く。Unityなしでも閲覧でき、画像をクリックすると元の大きさで開く。`.local/ui-preview/contact-sheet.png`は全画面の一覧。見出し・操作ボタン・ゲージの切り出しは、元の画像の画素を保持する。生成物はGit管理外とし、配布用には画像・HTML・撮影条件だけをZIPにまとめる。

起動、各ページ、校正、数値入力、視覚3状態、聴覚・無FBの無表示、開始前、追跡喪失、取り出し成功・失敗、頭部回転、再配置を確認する。`manifest.json`には撮影条件、Unity版、ソースコミット、未コミット変更の有無、検査結果を残す。古い成功画像は`.previous`へ退避し、今回失敗した画像を成功品として採用しない。

視野角は**垂直70°・90°という仮定**。1枚1440×1440、明暗の背景を使い、機器の実際の視野やカメラ映像を再現したとは扱わない。空間に固定された操作パネルが頭を30°回したとき視野から外れる例は意図的な確認で、初期状態・再配置後の見切れとは区別する。

## Editorで操作する

```sh
just unity-preview-open
```

公式のXR Interaction Simulatorを、この入口からだけ起動する。通常のAPKにはEditor用コードを含めず、実機の準備完了へ自動置換しない。

| キー | 操作 |
|---|---|
| H | 頭部の操作へ切替 |
| [ / ] | 左／右コントローラーの操作へ切替 |
| W/A/S/D・Q/E | 選択した機器の移動 |
| Spaceを押しながらマウス | 選択した機器の向きを変える |
| 左クリック | コントローラーのトリガー |
| M / R | 操作欄の切替／正面への再配置 |
| Backspace | シミュレーターの姿勢リセット |

実測トラッカーが接続されていない状態では、接続済みとして表示しない。画像撮影時の5台は明示的な合成データで、実機取得の証拠にはならない。

## テストと実機の差

| 確認方法 | 分かること | 分からないこと |
|---|---|---|
| 固定入力・時刻のテスト | 判定、取消、欠測、記録の再現性 | 人の接触を正しく推定できるか |
| 同じUIの描画画像 | 文字欠け、重なり、見切れ、色、状態表示 | レンズ越しの可読性・目への負担 |
| XR Interaction Simulator | 頭・手の操作、ポインター、画面遷移 | 実際の遮蔽・無線・追跡誤差 |
| 実機APK | 端末の描画、パススルー、音、処理負荷 | 別会場・全参加者での成立性 |

Editorと実機の差を一律に「何％」とは表せない。PCとAndroidでは描画経路・CPU/GPU・実行環境が異なる。実機では、今回のAPKの文字とポインターを座位で確認した後、現地の表示・計測・実出力と90分稼働を確認する。APKがビルドできても[研究用の受入れ](../specifications/validation.md)は未完了である。

画像生成では文字の領域超過と正面のパネルの画角を検査する。生成後は実際に画像を開き、日本語・余白・ゲージを目視する。GPUやフォント処理の微差を考慮し、全画素の完全一致だけを品質基準にしない。

## 根拠

公式資料の確認日：2026-10-20。Unity 6000.6.4f1、URP 17.6.0、XR Interaction Toolkit 3.4.1。

- [XR Interaction Simulator](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.4/manual/xr-interaction-simulator-overview.html)：キーボード等から機器入力を模擬する。
- [URPのRender Request](https://docs.unity3d.com/6000.6/Documentation/Manual/urp/User-Render-Requests.html)：同じカメラとUIをRenderTextureへ描画する方法。
- [Profilerの対象](https://docs.unity3d.com/6000.6/Documentation/Manual/profiler-profiling-applications.html)：Editorと対象端末の性能測定を分ける。
