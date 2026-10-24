# APKの導入とビルド

対象はVIVE Focus VisionのAndroid／OpenXR単体実行。実施者のPCブラウザーから操作し、参加者はコントローラーを使わない。[PC操作](experiment-console.md)・[確認状況](remaining-work.md)

## 配布物から導入する

1. [Releases](https://github.com/maya0513/wheelchair-sonification/releases)または配布された一式から`WheelchairSonification.apk`をWindows PCへダウンロードする。Source codeのZIPは不要。
2. Focus VisionをUSB接続し、ゴーグルでファイル転送を許可する。APKを「内部共有ストレージ → Download」へコピーする。
3. 本体のファイルブラウザーでAPKを開き、不明のアプリの許可を確認してインストールする。[HTC公式手順](https://www.vive.com/jp/support/focusvision/category_howto/installing-apk-on-headset.html)
4. 同じ配布物の`operator-console.zip`を実施者用Ubuntu PCへ展開する。Node.js 24以降を事前に用意し`sh start.sh`。PCのホットスポットへHMDを接続する。
5. 装着前に6桁番号をPCへ入力し、着座・停止状態でパススルー、左右の音・ゲージ、記録と回収を確認する。実機未検証の項目は[残作業表](remaining-work.md)に従う。

`ui-preview.zip`は展開して`index.html`を開くと、Unity画像・PC画像・WAV見本を閲覧できる。`build-info.json`はソース・設定・検査結果、`SHA256SUMS.txt`は照合用。これらをHMDへ入れる必要はない。回収はアプリ内の[記録ページ](research-records.md)から行う。

同じアプリID・署名鍵で更新する。署名不一致をアンインストールで回避すると元データを失うので、鍵と版を先に確認する。通信形式2のAPKとコンソールを使い、旧接触角版と混ぜない。

## 開発環境と署名

Unity Hubで6000.6.4f1とAndroid Build Support／SDK・NDK・OpenJDKを用意する。対象SDKは36。HTC OpenXR 2.5.1の公式配布tgzを`just unity-prepare`が固定URL・SHA-256で取得し、Git管理外へキャッシュする。手動インポートやGit LFSは不要。

```sh
mise install
just unity-prepare
just android-signing-init  # 初回だけ。既存の配布鍵を作り直さない
just unity-build-release
```

既存の配布鍵はGit管理外の`.local/signing/`と`.local/android-signing.json`にある。バックアップは安全な別場所へ保存し、鍵・パスワードをGit・チャット・Releaseへ入れない。別環境では`UNITY_ANDROID_KEYSTORE`、`UNITY_ANDROID_KEY_ALIAS`、`UNITY_ANDROID_KEYSTORE_PASSWORD`、`UNITY_ANDROID_KEY_ALIAS_PASSWORD`から読み込める。

版を指定する例：

```sh
UNITY_BUILD_VERSION=0.4.0 UNITY_ANDROID_VERSION_CODE=4 just unity-build-apk
```

出力は`.local/builds/android-release/`。署名、ARM64、IL2CPP、非デバッグ、XR Manifest、ローカルHTTP・権限、SDK・ライセンスを検査する。前のAPKと証明書が違えば更新互換性を認めない。実機動作は別途確認する。

## 公開する

```sh
just release-apk v0.4.0 4
```

公開時はGitHub認証・公開先・クリーンなソース・GitHubにあるコミット、未使用のタグを確認する。Unity画像・WAV、PC画像、署名付きAPKを同じソースから新規生成し、DraftへAPK・コンソール・プレビュー・ビルド情報・チェックサムを添付してプレリリース公開する。既存Releaseは上書きしない。アップロード失敗はDraftで止め、ローカル成果物と復旧案内を残す。

90分継続試験は任意。実際の20分待機や90分稼働を通常ビルド・公開の必須条件にしない。実機インストール、左右の知覚、速度精度、出力遅延、現地の単独実施、正式保管先・倫理は成功証拠が得られるまで未確認と明記する。
