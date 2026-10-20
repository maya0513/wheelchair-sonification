#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
    echo "Usage: $0 {path|libraries|open|check|test-edit|test-play|build}" >&2
    exit 2
fi
case "$1" in
    path|libraries|open|check|test-edit|test-play|build) ;;
    *) echo "Usage: $0 {path|libraries|open|check|test-edit|test-play|build}" >&2; exit 2 ;;
esac

if [[ "$1" == open || "$1" == test-play ]]; then
    graphics_api=${UNITY_GRAPHICS_API:-vulkan}
    case "$graphics_api" in
        vulkan) graphics_args=(-force-vulkan) ;;
        opengl) graphics_args=(-force-glcore) ;;
        *) echo "UNITY_GRAPHICS_APIにはvulkanまたはopenglを指定してください: $graphics_api" >&2; exit 2 ;;
    esac
fi

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
project="$root/wheelchair-sonification"
version_file="$project/ProjectSettings/ProjectVersion.txt"

if [[ ! -f "$version_file" ]]; then
    echo "Unityプロジェクトがありません: $version_file" >&2
    exit 1
fi
version=$(sed -n 's/^m_EditorVersion: //p' "$version_file" | tr -d '\r')
if [[ -z "$version" ]]; then
    echo "ProjectVersion.txtからEditorの版を読めません。" >&2
    exit 1
fi

editor=${UNITY_EDITOR:-"$HOME/Unity/Hub/Editor/$version/Editor/Unity"}
if [[ ! -x "$editor" ]]; then
    echo "Unity Editor $version が見つかりません: $editor" >&2
    echo "Hubで同じ版を導入するか、UNITY_EDITORで実行ファイルを指定してください。" >&2
    exit 1
fi

if [[ "$1" == path ]]; then
    printf '%s\n' "$editor"
    exit 0
fi

if ! libraries=$(ldd "$editor" 2>&1); then
    echo "Unityの共有ライブラリを確認できません: $editor" >&2
    printf '%s\n' "$libraries" >&2
    exit 1
fi
if [[ "$libraries" == *"not found"* ]]; then
    echo "Unityの共有ライブラリが不足しています: $editor" >&2
    printf '%s\n' "$libraries" >&2
    exit 1
fi
if [[ "$1" == libraries ]]; then
    echo "Unityの共有ライブラリを解決できました（Editor未実行）。"
    exit 0
fi

if [[ -e "$project/Temp/UnityLockfile" ]]; then
    echo "Unity Editorを閉じてから just unity-$1 を実行してください。" >&2
    exit 1
fi
mkdir -p "$root/.local/logs"
node "$root/scripts/unity-xr-sdk.mts"

if [[ "$1" == open ]]; then
    exec "$editor" "${graphics_args[@]}" -projectPath "$project" -logFile "$root/.local/logs/unity-editor.log"
fi

log="$root/.local/logs/unity-$1.log"
# 古いログを今回の結果として扱わない。直前のログは残す。
if [[ -e "$log" ]]; then
    mv -- "$log" "$log.previous"
fi
status=0
results=''
case "$1" in
    test-edit|test-play)
        results="$root/.local/logs/unity-$1.xml"
        if [[ -e "$results" ]]; then mv -- "$results" "$results.previous"; fi
        if [[ "$1" == test-edit ]]; then
            test_platform=EditMode
            test_assembly=WheelchairSonification.EditModeTests
            test_graphics=(-nographics)
        else
            test_platform=PlayMode
            test_assembly=WheelchairSonification.PlayModeTests
            test_graphics=("${graphics_args[@]}")
        fi
        # -quit can terminate the test runner before it finishes; the runner exits itself.
        "$editor" -batchmode "${test_graphics[@]}" -projectPath "$project" -logFile "$log" \
            -runTests -testPlatform "$test_platform" -assemblyNames "$test_assembly" -testResults "$results" || status=$?
        ;;
    build)
        "$editor" -batchmode -nographics -quit -projectPath "$project" -logFile "$log" \
            -executeMethod WheelchairSonification.Editor.DemoBuildTools.BuildLinux || status=$?
        ;;
    check)
        "$editor" -batchmode -nographics -quit -projectPath "$project" -logFile "$log" || status=$?
        ;;
esac
if [[ "$status" -ne 0 ]]; then
    echo "Unityバッチ確認に失敗しました（終了コード: $status、ログ: $log）。" >&2
    if [[ -s "$log" ]]; then
        tail -n 60 -- "$log" >&2
    else
        echo "今回のログは生成されませんでした。" >&2
    fi
    exit "$status"
fi
if [[ ! -s "$log" ]]; then
    echo "Unityの終了コードは0ですが、今回のログがありません: $log" >&2
    exit 1
fi
if grep -Eq 'error CS[0-9]+|Scripts have compiler errors|Compilation failed' "$log"; then
    echo "コンパイルエラーがあります: $log" >&2
    exit 1
fi
if [[ -n "$results" ]]; then
    "${UNITY_RESULTS_PYTHON:-python3}" "$root/scripts/unity-results.py" "$results"
    exit $?
fi
if ! grep -Fq 'Exiting batchmode successfully now!' "$log" ||
    ! grep -Fq 'Application will exit with return code 0' "$log"; then
    echo "Unityの正常終了記録がありません: $log" >&2
    tail -n 60 -- "$log" >&2
    exit 1
fi
if [[ "$1" == build ]]; then
    if ! grep -Fq 'Feedback demo built:' "$log" || [[ ! -x "$root/.local/builds/feedback-demo/FeedbackDemo.x86_64" ]]; then
        echo "今回のLinuxビルドの成功を確認できません: $log" >&2
        exit 1
    fi
    printf 'Linuxデモ: %s\n' "$root/.local/builds/feedback-demo/FeedbackDemo.x86_64"
else
    printf 'Unity %s: 依存解決・コンパイル成功（ログ: %s）\n' "$version" "$log"
fi
