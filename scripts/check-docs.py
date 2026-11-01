#!/usr/bin/env python3
"""Check repository Markdown links, bibliography coverage and retained PDF hashes."""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
LINK = re.compile(r'!?\[[^\]\n]*\]\((?:<([^>]+)>|([^\s)]+))(?:\s+"[^"]*")?\)')


def tracked_files(root: Path) -> list[Path]:
    output = subprocess.check_output(
        ["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard"], cwd=root
    )
    return sorted({root / name for name in output.decode().split("\0") if name and (root / name).is_file()})


def without_code(text: str) -> str:
    return re.sub(r"(^```[^\n]*\n).*?^```[^\n]*$", "", text, flags=re.M | re.S)


def anchors(text: str) -> set[str]:
    result = set(re.findall(r'\bid=["\']([^"\']+)["\']', text))
    counts: dict[str, int] = {}
    for line in without_code(text).splitlines():
        if not re.match(r"^#{1,6}\s", line):
            continue
        heading = re.sub(r"^#+\s+|\s+#+$", "", line)
        heading = re.sub(r"\[([^\]]+)\]\([^)]*\)", r"\1", heading).replace("`", "")
        slug = "".join(c for c in heading.lower() if c.isalnum() or c in " _-").replace(" ", "-")
        count = counts.get(slug, 0)
        result.add(slug + ("-" + str(count) if count else ""))
        counts[slug] = count + 1
    return result


def inspect(root: Path) -> dict:
    files = tracked_files(root)
    markdown = [p for p in files if p.suffix == ".md"]
    errors: list[str] = []
    links = 0
    for file in markdown:
        text = without_code(file.read_text(encoding="utf-8"))
        for match in LINK.finditer(text):
            href = match[1] or match[2]
            parsed = urlsplit(href)
            if parsed.scheme or href.startswith("//"):
                continue
            links += 1
            target = (file.parent / unquote(parsed.path)).resolve() if parsed.path else file
            label = str(file.relative_to(root)) + ": " + href
            if not target.exists():
                errors.append(label + " (missing target)")
            elif target.suffix == ".md" and parsed.fragment:
                if unquote(parsed.fragment) not in anchors(target.read_text(encoding="utf-8")):
                    errors.append(label + " (missing heading)")
    research = root / "research"
    catalog = (research / "catalog.md").read_text(encoding="utf-8")
    entries = re.findall(r"^\| \[([LD]\d+)\]\(([^)]+)\)", catalog, re.M)
    bib = (research / "references.bib").read_text(encoding="utf-8")
    bib_ids = re.findall(r"@\w+\s*\{\s*([LD]\d+)\s*,", bib)
    if len(bib_ids) != len(set(bib_ids)):
        errors.append("references.bib: duplicate IDs")
    catalog_ids = {key for key, _ in entries}
    for key, path in entries:
        if key not in bib_ids:
            errors.append("catalog.md: missing BibTeX entry " + key)
        if not (research / path).is_file():
            errors.append("catalog.md: missing note " + key)
    for note in (research / "papers").glob("*.md"):
        key = note.name.split("-", 1)[0]
        if key not in catalog_ids:
            errors.append("catalog.md: missing paper " + key)
    pdfs = json.loads((research / "pdfs/manifest.json").read_text(encoding="utf-8"))
    for entry in pdfs:
        file = research / entry["file"]
        if not file.is_file():
            errors.append("PDF missing: " + entry["file"])
        elif file.stat().st_size != entry["bytes"] or hashlib.sha256(file.read_bytes()).hexdigest() != entry["sha256"]:
            errors.append("PDF changed: " + entry["file"])
    return {"passed": not errors, "markdown": len(markdown), "local_links": links,
            "catalog_entries": len(entries), "retained_pdfs": len(pdfs), "errors": errors}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args()
    report = inspect(ROOT)
    if args.json:
        print(json.dumps(report, ensure_ascii=False, indent=2))
    else:
        print(f"Markdown {report['markdown']}件 / 内部リンク {report['local_links']}件 / 文献 {report['catalog_entries']}件 / 保存PDF {report['retained_pdfs']}件")
        for error in report["errors"]:
            print(error)
        print("整合確認: " + ("成功" if report["passed"] else "失敗"))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
