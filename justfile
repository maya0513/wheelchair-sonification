[unix]
set shell := ["bash", "-eu", "-o", "pipefail", "-c"]
[windows]
set shell := ["cmd.exe", "/D", "/C"]
set default-list := true

# 開発・検証用。just --list devで一覧を表示
mod dev 'tasks/dev.just'

# ロックされたプロジェクトの開発ツールを導入
setup:
    mise install --locked just node python uv typst pypi:mcpforunityserver npm:skills

# 実験用PC画面を起動（Wi-Fi設定は変更しない）
experiment-console:
    mise exec --locked node -- node scripts/experiment-console.mts

# 実機なしでPC画面を試す（模擬モード、確認番号123456）
experiment-console-demo:
    mise exec --locked node -- node scripts/experiment-console.mts --demo

# ゴーグル・PCの画像と音見本を生成（Unity Editorを閉じてから）
preview:
    just dev unity-preview
    just dev console-preview

# 本番用の署名付きFocus Vision APKを作成
unity-build-apk:
    mise exec --locked node -- node scripts/unity-release.mts

# 指定版を新規ビルドし、GitHubプレリリースとして公開
release-apk tag version_code:
    mise exec --locked node -- node scripts/github-apk-release.mts {{quote(tag)}} {{quote(version_code)}}

# このプロジェクトのyomiyasuを更新（.agents/とskills-lock.json）
skills-update:
    mise exec --locked node npm:skills -- skills update yomiyasu --project --yes
