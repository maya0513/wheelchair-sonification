# scripts

- `unity-mcp.mts`: MCPサーバーを起動し、停止時に接続を閉じる。
- `unity.test.mts`: Unity起動スクリプトのテスト。
- `unity.sh`: Unity Editorの起動、コンパイル、Edit／Play Modeテスト、Linuxビルドを行う。
- `unity-release.mts`: Windows/LinuxでUnityから署名付きの非開発APKを作成し、今回の成果物・完了記録とハッシュを確認する。
- `unity-release.test.mts`: APKビルドの事前条件、起動引数、失敗・古い成果物の扱いを確認する。
- `unity-results.py`: NUnit結果XMLを検査し、失敗・結果なし・0件・件数不一致を検出する。
- `unity-results.test.mts`: XML検査の正常・異常系のテスト。
- `unity-mcp.test.mts`: MCPサーバーの停止処理のテスト。

Unityの操作は`just unity-open`、`just unity-check`、`just unity-test`、`just unity-build`から実行する。Editorを閉じてからバッチ確認を行う。Play Modeでは描画を有効にし、`UNITY_GRAPHICS_API=opengl`でVulkanから切り替えられる。結果検査のPythonは必要に応じて`UNITY_RESULTS_PYTHON`で指定できる。

Unityの起動・ビルドにMCPサーバーは不要。`just check`はNodeとPython、`just unity-test`はPythonだけをmiseで解決し、無関係なツールの自動導入を要求しない。デモの操作と生成物は[使い方](../docs/development/feedback-demo.md)を参照する。

`just unity-build-release`（別名`just unity-build-apk`）はWindowsでも使えるAndroid向けの入口。Nodeと指定版UnityのAndroid Build Support、署名鍵を事前に用意する。Windows向けの初回設定、生成物、機器の型番確認は[Androidビルドと導入](../docs/development/android-build.md)を参照する。`just unity-open`など`unity.sh`を呼ぶ既存コマンドはLinux向け。
