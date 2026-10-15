"""Validate a fresh NUnit 3 report; an exit code alone cannot prove tests ran."""

import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def validate(path: Path) -> int:
    root = ET.parse(path).getroot()
    if root.tag != "test-run":
        raise ValueError("NUnitのtest-run要素がありません。")
    cases = root.findall(".//test-case")
    total = int(root.attrib.get("total", "0"))
    passed = int(root.attrib.get("passed", "0"))
    if total < 1 or not cases:
        raise ValueError("実行されたテストが0件です。")
    if len(cases) != total or passed != total:
        raise ValueError(f"テスト数が一致しません: total={total}, passed={passed}, cases={len(cases)}")
    unsuccessful = [case for case in cases if case.attrib.get("result") != "Passed"]
    if root.attrib.get("result") != "Passed" or unsuccessful or any(
        int(root.attrib.get(name, "0")) != 0 for name in ("failed", "skipped", "inconclusive")
    ):
        names = ", ".join(case.attrib.get("fullname", "unknown") for case in unsuccessful[:5])
        raise ValueError("全テストが成功していません。 " + names)
    return total


if __name__ == "__main__":
    try:
        if len(sys.argv) != 2:
            raise ValueError("Usage: unity-results.py RESULTS.xml")
        count = validate(Path(sys.argv[1]))
    except (OSError, ValueError, ET.ParseError) as error:
        print(f"Unityテスト結果を確認できません: {error}", file=sys.stderr)
        sys.exit(1)
    print(f"Unityテスト: {count}件成功（結果: {sys.argv[1]}）")
