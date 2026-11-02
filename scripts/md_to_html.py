# -*- coding: utf-8 -*-
"""Конвертер Markdown -> HTML для документов из docs/руководства.

Использование:
    python scripts/md_to_html.py docs/руководства/INSTALL_v1.6.5.2.md [out.html]
"""
from __future__ import annotations

import html
import re
import sys
from pathlib import Path

STYLE = """
:root { color-scheme: light; }
@page { margin: 16mm; }
body{font-family:'Segoe UI',Arial,sans-serif;font-size:11pt;line-height:1.5;
color:#222;background:#fff;margin:0;padding:24px 16px;}
main{max-width:900px;margin:0 auto;}
h1{font-size:22pt;margin:0 0 14px;}
h2{font-size:15pt;margin:26px 0 10px;border-bottom:1px solid #ccc;padding-bottom:4px;}
h3{font-size:12.5pt;margin:18px 0 6px;}
h4{font-size:11.5pt;margin:14px 0 6px;}
p{margin:8px 0;}
table{border-collapse:collapse;width:100%;margin:10px 0 16px;}
th,td{border:1px solid #bbb;padding:6px 9px;text-align:left;vertical-align:top;}
th{background:#f3f3f3;}
code{font-family:Consolas,'Courier New',monospace;font-size:9.5pt;
background:#f4f4f4;border:1px solid #e2e2e2;border-radius:3px;padding:0 3px;}
pre{font-family:Consolas,'Courier New',monospace;font-size:9.5pt;background:#f6f6f6;
border:1px solid #ddd;padding:10px;overflow-x:auto;}
pre code{background:none;border:none;padding:0;}
ul,ol{margin:8px 0 12px;padding-left:26px;}
li{margin:4px 0;}
li > ul, li > ol{margin:4px 0;}
ul.task{list-style:none;padding-left:4px;}
hr{border:none;border-top:1px solid #ccc;margin:22px 0;}
a{color:#14568c;}
blockquote{margin:10px 0;padding:6px 12px;border-left:3px solid #ccc;color:#444;}
@media print{body{padding:0;} a{color:#222;text-decoration:none;}}
"""

LIST_RE = re.compile(r"^(\s*)(?:([-*+])|(\d+)[.)])\s+(.*)$")
TASK_RE = re.compile(r"^\[([ xX])\]\s*(.*)$")


def inline(text: str) -> str:
    """Инлайн-разметка. Код прячется в плейсхолдеры, чтобы `**` вокруг `кода`
    и ссылки внутри кода не ломали разбор."""
    text = html.escape(text)
    codes: list[str] = []

    def stash(m: re.Match[str]) -> str:
        codes.append(m.group(1))
        return f"\x00{len(codes) - 1}\x00"

    text = re.sub(r"`([^`]+)`", stash, text)
    text = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', text)
    text = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", text)
    text = re.sub(r"(?<![*\w])\*([^*\n]+)\*(?!\*)", r"<em>\1</em>", text)
    text = re.sub(r'(?<![">])(https?://[^\s<)]+)', r'<a href="\1">\1</a>', text)
    return re.sub(r"\x00(\d+)\x00", lambda m: f"<code>{codes[int(m.group(1))]}</code>", text)


class Renderer:
    def __init__(self) -> None:
        self.out: list[str] = []
        self.table: list[list[str]] = []
        self.stack: list[list] = []  # [indent, tag, li_open]
        self.para: list[str] = []
        self.hard_break = False
        self.in_code = False

    # --- блоки ---------------------------------------------------------
    def flush_table(self) -> None:
        if not self.table:
            return
        self.out.append("<table>")
        for i, row in enumerate(self.table):
            if i == 1 and all(set(c.strip()) <= set("-: ") for c in row):
                continue
            tag = "th" if i == 0 else "td"
            cells = "".join(f"<{tag}>{inline(c.strip())}</{tag}>" for c in row)
            self.out.append(f"<tr>{cells}</tr>")
        self.out.append("</table>")
        self.table = []

    def flush_para(self) -> None:
        if self.para:
            self.out.append(f"<p>{''.join(self.para)}</p>")
            self.para = []
        self.hard_break = False

    def pop_list(self) -> None:
        _, tag, li_open = self.stack.pop()
        if li_open:
            self.out.append("</li>")
        self.out.append(f"</{tag}>")

    def close_lists(self) -> None:
        while self.stack:
            self.pop_list()

    def flush_blocks(self) -> None:
        self.flush_para()
        self.flush_table()
        self.close_lists()

    def open_list(self, indent: int, tag: str, task: bool) -> None:
        cls = " class='task'" if task else ""
        self.out.append(f"<{tag}{cls}>")
        self.stack.append([indent, tag, False])

    def list_item(self, indent: int, ordered: bool, body: str) -> None:
        self.flush_para()
        self.flush_table()
        tag = "ol" if ordered else "ul"
        task = TASK_RE.match(body)

        while len(self.stack) > 1 and indent < self.stack[-1][0]:
            self.pop_list()

        if not self.stack:
            self.open_list(indent, tag, bool(task))
        elif indent > self.stack[-1][0]:
            # вложенный список живёт внутри текущего <li>
            self.open_list(indent, tag, bool(task))
        else:
            if self.stack[-1][2]:
                self.out.append("</li>")
                self.stack[-1][2] = False
            if self.stack[-1][1] != tag:
                self.pop_list()
                self.open_list(indent, tag, bool(task))

        if task:
            box = "☑" if task.group(1).lower() == "x" else "☐"
            self.out.append(f"<li>{box} {inline(task.group(2))}")
        else:
            self.out.append(f"<li>{inline(body)}")
        self.stack[-1][2] = True

    # --- основной проход ------------------------------------------------
    def convert(self, md: str, title: str | None = None) -> str:
        lines = md.splitlines()
        if title is None:
            title = next(
                (ln[2:].strip() for ln in lines if ln.startswith("# ")), "Документ"
            )

        for line in lines:
            if line.strip().startswith("```"):
                if self.in_code:
                    self.out.append("</code></pre>")
                else:
                    self.flush_blocks()
                    self.out.append("<pre><code>")
                self.in_code = not self.in_code
                continue

            if self.in_code:
                self.out.append(html.escape(line))
                continue

            if line.startswith("|") and line.rstrip().endswith("|"):
                self.flush_para()
                self.close_lists()
                self.table.append(line.strip().strip("|").split("|"))
                continue

            if not line.strip():
                self.flush_para()
                self.flush_table()
                continue

            if re.fullmatch(r"\s*(---+|\*\*\*+|___+)\s*", line):
                self.flush_blocks()
                self.out.append("<hr>")
                continue

            heading = re.match(r"^(#{1,4})\s+(.*)$", line)
            if heading:
                self.flush_blocks()
                level = len(heading.group(1))
                self.out.append(
                    f"<h{level}>{inline(heading.group(2).strip())}</h{level}>"
                )
                continue

            item = LIST_RE.match(line)
            if item:
                indent = len(item.group(1).expandtabs(4))
                self.list_item(indent, item.group(3) is not None, item.group(4))
                continue

            if line.startswith("> "):
                self.flush_blocks()
                self.out.append(f"<blockquote>{inline(line[2:])}</blockquote>")
                continue

            self.flush_table()
            self.close_lists()
            if self.para:
                self.para.append("<br>" if self.hard_break else " ")
            self.hard_break = line.endswith("  ") or line.endswith("\\")
            self.para.append(inline(line.strip().rstrip("\\")))

        if self.in_code:
            self.out.append("</code></pre>")
        self.flush_blocks()

        head = [
            "<!DOCTYPE html>",
            '<html lang="ru"><head><meta charset="utf-8">',
            '<meta name="viewport" content="width=device-width, initial-scale=1">',
            f"<title>{html.escape(title)}</title>",
            f"<style>{STYLE}</style>",
            "</head><body><main>",
        ]
        return "\n".join(head + self.out + ["</main></body></html>", ""])


def main(argv: list[str]) -> int:
    if not argv:
        print(__doc__)
        return 2
    src = Path(argv[0])
    dst = Path(argv[1]) if len(argv) > 1 else src.with_suffix(".html")
    dst.write_text(
        Renderer().convert(src.read_text(encoding="utf-8")), encoding="utf-8"
    )
    print(f"{src} -> {dst} ({dst.stat().st_size} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
