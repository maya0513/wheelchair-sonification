# 署名付きAPKのビルドと公開

対象はVIVE Focus VisionのAndroid／OpenXR単体実行。[確認状況](../status.md)と[開発環境](README.md)を確認する。

配布物を使う場合は[導入手順](../operations/setup.md)へ進む。ここではAPKを生成・検査・公開する手順を扱う。

## 開発環境と署名

Unity Hubで6000.6.4f1とAndroid Build Support／SDK・NDK・OpenJDKを用意する。対象SDKは36。HTC OpenXR 2.5.1の公式配布tgzを`just dev unity-prepare`が固定URL・SHA-256で取得し、Git管理外へキャッシュする。手動インポートやGit LFSは不要。

```sh
just setup
just dev android-signing-init  # 初回だけ。既存の配布鍵を作り直さない
just dev unity-build-apk
```

既存の配布鍵はGit管理外の`.local/signing/`と`.local/android-signing.json`にある。バックアップは安全な別場所へ保存し、鍵・パスワードをGit・チャット・Releaseへ入れない。別環境では`UNITY_ANDROID_KEYSTORE`、`UNITY_ANDROID_KEY_ALIAS`、`UNITY_ANDROID_KEYSTORE_PASSWORD`、`UNITY_ANDROID_KEY_ALIAS_PASSWORD`から読み込める。

バージョンを指定する例を示す。

```sh
UNITY_BUILD_VERSION=0.7.0 UNITY_ANDROID_VERSION_CODE=7 just dev unity-build-apk
```

出力は`.local/builds/android-release/`。署名、ARM64、IL2CPP、非デバッグ、XR Manifest、ローカルHTTP・権限、SDK・ライセンスを検査する。前のAPKと証明書が違えば更新互換性を認めない。実機動作は別途確認する。

## 公開する

```sh
just dev release-apk v0.7.0 7
```

公開時はGitHub認証・公開先・クリーンなソース・GitHubにあるコミット、未使用のタグを確認する。署名付きAPKを新規ビルド・検査し、DraftへAPK一件だけを添付する。ファイル名・サイズを照合してプレリリース公開する。既存Releaseは上書きしない。アップロード失敗はDraftで止め、ローカル成果物と復旧案内を残す。

参加者の来訪は30分。任意の90分耐久試験はそれと別で、実時間の待機を通常ビルド・公開の必須条件にしない。実機インストール、左右の知覚、速度精度、出力遅延、現地の単独実施、正式保管先・倫理は成功証拠が得られるまで未確認と明記する。

Releaseの添付物は`WheelchairSonification.apk`だけ。ソースコミット・SHA-256・実機未確認事項はRelease本文に記載する。`build-info.json`と`SHA256SUMS.txt`はローカル出力に残す。PCは同じ版のリポジトリから`just start`で起動する。画像・音は[プレビュー](xr-preview.md)で独立して確認し、公開のたびには生成しない。
