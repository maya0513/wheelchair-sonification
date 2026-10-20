# scripts

- `unity-mcp.mts`: MCPサーバーを起動し、停止時に接続を閉じる。
- `unity.test.mts`: Unity起動スクリプトのテスト。
- `unity.sh`: Unity Editorの起動、コンパイル、Edit／Play Modeテスト、Linuxビルドを行う。
- `unity-release.mts`: Windows/LinuxでUnityから署名付きの非開発APKを作成し、今回の成果物・完了記録とハッシュを確認する。
- `unity-release.test.mts`: APKビルドの事前条件、起動引数、失敗・古い成果物の扱いを確認する。
- `unity-editor.mts`: プロジェクト版に対応するUnity実行ファイルを解決する。
- `unity-xr-sdk.mts`: 固定したHTC SDKを公式配布元から取得し、SHA-256で検証する。
- `android-signing.mts`: ローカルの配布用署名鍵の初回作成・読み込み。鍵やパスワードを表示しない。
- `android-apk-validation.mts`: Android SDKでAPKの署名、ABI、Manifest、必要ファイルを検証する。
- `github-apk-release.mts`: 新しく検証したAPKと情報をDraftへ添付してから、GitHubのプレリリースとして公開する。
- `unity-xr-sdk.test.mts`・`github-apk-release.test.mts`: SDK・署名設定・APK検証・公開失敗のテスト。実際のReleaseを作成しない。
- `unity-results.py`: NUnit結果XMLを検査し、失敗・結果なし・0件・件数不一致を検出する。
- `unity-results.test.mts`: XML検査の正常・異常系のテスト。
- `unity-mcp.test.mts`: MCPサーバーの停止処理のテスト。

Unityの操作は`just unity-open`、`just unity-check`、`just unity-test`、`just unity-build`から実行する。Editorを閉じてからバッチ確認を行う。Play Modeでは描画を有効にし、`UNITY_GRAPHICS_API=opengl`でVulkanから切り替えられる。結果検査のPythonは必要に応じて`UNITY_RESULTS_PYTHON`で指定できる。

Unityの起動・ビルドにMCPサーバーは不要。`just check`はNodeとPython、`just unity-test`はPythonだけをmiseで解決し、無関係なツールの自動導入を要求しない。デモの操作と生成物は[使い方](../docs/development/feedback-demo.md)を参照する。

`just unity-build-release`（別名`just unity-build-apk`）はWindowsでも使えるFocus Vision向けの入口。SDKは自動取得し、AndroidのXR Sceneだけをビルドする。`just android-signing-init`で署名鍵を初回作成でき、Ubuntuでは`just release-apk v0.3.0 3`でGitHubへ公開する。配布先PCではReleasesからAPKを取得すれば再ビルドは不要。[Androidビルドと導入](../docs/development/android-build.md)を参照する。`just unity-open`など`unity.sh`を呼ぶ既存コマンドはLinux向け。

`just unity-preview`はAPKと共通のSDF UIをUnityからPNGへ描画し、切り出し・一覧・HTMLを`.local/ui-preview/`へ出力する。`just unity-preview-open`はEditor専用のXR Interaction Simulatorを起動する。`just unity-replay /path/to/trial`は元の時刻・校正・位置姿勢で推定と判定を再解析する。[画像確認](../docs/development/xr-preview.md)・[記録再解析](../docs/development/research-records.md)

- `unity-preview.mts` / `unity-preview.test.mts`: 実描画の起動、撮影条件の検査、画像ギャラリー、公開ZIP。実験ログをZIPへ含めない。
- `unity-state.mts`: ビルド・画像確認後に設定と動的フォントのシリアライズを戻す。
- `preview-test-fixture.mts`: 公開・ファイル検査のテスト専用データ。実描画の代用にはしない。

Releaseは新しいUnity画像と署名APKの検証後、4ファイルを添付する。実機性能と画面の画像確認は別に記録する。
