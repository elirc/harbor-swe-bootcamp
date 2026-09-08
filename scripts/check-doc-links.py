"""Check local inline Markdown file links; remote URLs and anchors are out of scope.

Run from any directory: python scripts/check-doc-links.py
Uses only Python's standard library. Code fences are ignored so example links
do not need to point at real files. Reports every broken target before failing.
"""

import re
import sys
from pathlib import Path
from urllib.parse import unquote, urlsplit


ROOT = Path(__file__).resolve().parent.parent
LINK = re.compile(r"\[[^\]]*\]\((<[^>]+>|[^\s)]+)(?:\s+\"[^\"]*\")?\)")


def main() -> int:
    checked = 0
    errors = []
    files = sorted({ROOT / "README.md", *ROOT.glob("docs/**/*.md"), *ROOT.glob("astradocs/**/*.md")})
    for file in files:
        fence = None
        for line_number, line in enumerate(file.read_text(encoding="utf-8-sig").splitlines(), 1):
            stripped = line.lstrip()
            if stripped.startswith(("```", "~~~")):
                marker = stripped[:3]
                if fence is None:
                    fence = marker
                elif fence == marker:
                    fence = None
                continue
            if fence:
                continue
            for match in LINK.finditer(line):
                target = match.group(1).strip("<>")
                parsed = urlsplit(target)
                if parsed.scheme or parsed.netloc or not parsed.path:
                    continue
                checked += 1
                path = unquote(parsed.path)
                resolved = (ROOT / path.lstrip("/") if path.startswith("/") else file.parent / path)
                if not resolved.exists():
                    errors.append(f"{file.relative_to(ROOT)}:{line_number}: missing target {target}")
    for error in errors:
        print(error, file=sys.stderr)
    print(f"Checked {checked} local file links in {len(files)} Markdown files; {len(errors)} broken.")
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
