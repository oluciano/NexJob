#!/usr/bin/env python3
"""
Synchronizes and converts customer-facing documentation from the mintlify-docs
repository into NexJob's MkDocs Material structure (docs/wiki/).

Usage:
    python3 docs/site/sync-from-mintlify.py
    python3 docs/site/sync-from-mintlify.py --src /path/to/mintlify-docs
"""

import argparse
import os
import re
import shutil
import sys
from pathlib import Path


def convert_content(text: str, rel_depth: int, file_path_str: str) -> str:
    """Converts Mintlify MDX tags into MkDocs Material native Markdown syntax."""
    # Admonitions
    def repl_warning(m):
        inner = m.group(1).strip()
        indented = "\n".join("    " + line if line.strip() else "" for line in inner.splitlines())
        return f"!!! warning\n{indented}\n"

    text = re.sub(r"<Warning>\s*(.*?)\s*</Warning>", repl_warning, text, flags=re.DOTALL)

    def repl_note(m):
        inner = m.group(1).strip()
        indented = "\n".join("    " + line if line.strip() else "" for line in inner.splitlines())
        return f"!!! note\n{indented}\n"

    text = re.sub(r"<Note>\s*(.*?)\s*</Note>", repl_note, text, flags=re.DOTALL)

    def repl_tip(m):
        inner = m.group(1).strip()
        indented = "\n".join("    " + line if line.strip() else "" for line in inner.splitlines())
        return f"!!! tip\n{indented}\n"

    text = re.sub(r"<Tip>\s*(.*?)\s*</Tip>", repl_tip, text, flags=re.DOTALL)

    # Accordions
    def repl_accordion(m):
        title = m.group(1)
        inner = m.group(2).strip()
        indented = "\n".join("    " + line if line.strip() else "" for line in inner.splitlines())
        return f'???+ "{title}"\n{indented}\n'

    text = re.sub(r'<Accordion title="(.*?)">\s*(.*?)\s*</Accordion>', repl_accordion, text, flags=re.DOTALL)

    # Cards & CardGroups
    def repl_card(m):
        title = m.group(1)
        href = m.group(2) if m.group(2) else ""
        inner = m.group(3).strip()
        if href:
            clean_href = href.lstrip("/")
            if not clean_href.endswith(".md"):
                clean_href += ".md"
            if rel_depth > 0:
                clean_href = ("../" * rel_depth) + clean_href
            return f"-   [**{title}**]({clean_href})\n\n    {inner}\n"
        return f"-   **{title}**\n\n    {inner}\n"

    text = re.sub(r'<Card title="(.*?)"(?: icon=".*?")?(?: href="(.*?)")?>\s*(.*?)\s*</Card>', repl_card, text, flags=re.DOTALL)
    text = re.sub(r"<CardGroup.*?>", r'<div class="grid cards" markdown>', text)
    text = re.sub(r"</CardGroup>", r"</div>", text)

    # Steps
    def repl_step(m):
        title = m.group(1)
        inner = m.group(2).strip()
        return f"#### {title}\n\n{inner}\n"

    text = re.sub(r'<Step title="(.*?)">\s*(.*?)\s*</Step>', repl_step, text, flags=re.DOTALL)
    text = re.sub(r"<Steps>", r"", text)
    text = re.sub(r"</Steps>", r"", text)

    # Tabs
    def repl_tab(m):
        title = m.group(1)
        inner = m.group(2).strip()
        indented = "\n".join("    " + line if line.strip() else "" for line in inner.splitlines())
        return f'=== "{title}"\n{indented}\n'

    text = re.sub(r'<Tab title="(.*?)">\s*(.*?)\s*</Tab>', repl_tab, text, flags=re.DOTALL)
    text = re.sub(r"<Tabs>", r"", text)
    text = re.sub(r"</Tabs>", r"", text)

    # Relative Links: [Label](/path) -> [Label](relative/path.md)
    def repl_link(m):
        label = m.group(1)
        target = m.group(2)
        if target.startswith(("http://", "https://", "#", "mailto:")):
            return f"[{label}]({target})"
        if target.startswith("/"):
            target_clean = target.lstrip("/")
            parts = target_clean.split("#", 1)
            path_part = parts[0]
            anchor_part = f"#{parts[1]}" if len(parts) > 1 else ""

            if not path_part or path_part == "index":
                path_part = "index.md"
            elif not path_part.endswith(".md"):
                path_part += ".md"

            if rel_depth > 0:
                final_path = ("../" * rel_depth) + path_part + anchor_part
            else:
                final_path = path_part + anchor_part
            return f"[{label}]({final_path})"
        return m.group(0)

    text = re.sub(r"\[(.*?)\]\((.*?)\)", repl_link, text)

    # NexJob architectural enhancements
    if "mental-model" in file_path_str and "## Broker Queues vs NexJob Queues" not in text:
        extra_section = """
## Broker Queues vs NexJob Queues

Do not confuse an external message broker queue (RabbitMQ, Kafka, AWS SQS) with a NexJob queue:

- **Message Broker Queue (Transport):** A broker queue or topic transports messages across network boundaries between services. Messages are transient and consumed off the network buffer.
- **NexJob Queue (Governance & Execution):** A NexJob queue is a persistent logical partition inside your storage database (PostgreSQL, MongoDB, SQL Server, Redis). It governs execution concurrency, rate throttling (`[Throttle]`), execution windows (e.g. 22:00 – 06:00), and automated **Queue Circuit Breaking**.

When using broker triggers (e.g. `NexJob.RabbitMQ`), the trigger consumes off the RabbitMQ transport queue and enqueues into a NexJob storage queue. This protects your downstream services: if an external API fails, NexJob's **Circuit Breaker** automatically pauses the logical queue without dropping messages or overwhelming your message broker.
"""
        text = text.replace("## Next steps", extra_section + "\n## Next steps")

    if "rabbitmq" in file_path_str and "## Architectural Distinction" not in text:
        extra_rabbit = """
## Architectural Distinction: RabbitMQ Queue vs NexJob Queue

A crucial architectural principle when designing distributed systems with NexJob and RabbitMQ:

1. **RabbitMQ Queue = Network Transport Buffer:** Decouples producer and consumer across your microservices network. Once NexJob's trigger receives and acknowledges the message, it is safely in your database.
2. **NexJob Queue = Resilient Execution & Governance:** Once enqueued in NexJob, the job is governed by storage-backed ACID guarantees, per-queue concurrency, throttling, retries with exponential backoff, and **Queue Circuit Breaking**.
"""
        text = text.replace("## Basic Setup", extra_rabbit + "\n## Basic Setup")

    return text


def main():
    parser = argparse.ArgumentParser(description="Sync docs from mintlify-docs repo to docs/wiki/")
    parser.add_argument(
        "--src",
        type=Path,
        default=Path.home() / "git" / "mintlify-docs",
        help="Path to mintlify-docs repository (default: ~/git/mintlify-docs)",
    )
    parser.add_argument(
        "--dest",
        type=Path,
        default=Path(__file__).resolve().parent.parent / "wiki",
        help="Destination wiki directory (default: docs/wiki)",
    )
    args = parser.parse_args()

    src_dir = args.src.resolve()
    dest_dir = args.dest.resolve()

    if not src_dir.exists():
        print(f"Error: Source directory {src_dir} does not exist.", file=sys.stderr)
        sys.exit(1)

    # Pull latest changes from remote mintlify-docs repo if it is a git repo
    if (src_dir / ".git").exists():
        print(f"Pulling latest documentation from git in {src_dir}...")
        import subprocess
        try:
            subprocess.run(["git", "pull", "--ff-only"], cwd=src_dir, check=True)
        except Exception as ex:
            print(f"Warning: git pull failed in {src_dir}: {ex}. Proceeding with local files.")

    print(f"Syncing from {src_dir} -> {dest_dir}...")

    # Clear destination wiki directory safely
    dest_dir.mkdir(parents=True, exist_ok=True)
    for item in dest_dir.iterdir():
        if item.is_dir():
            shutil.rmtree(item)
        elif item.is_file():
            item.unlink()

    count = 0
    for root, _, files in os.walk(src_dir):
        for f in sorted(files):
            if f.endswith(".mdx"):
                src_file = Path(root) / f
                rel_path = src_file.relative_to(src_dir)
                out_rel_path = rel_path.with_suffix(".md")
                out_file = dest_dir / out_rel_path
                out_file.parent.mkdir(parents=True, exist_ok=True)

                rel_depth = len(out_rel_path.parts) - 1
                content = src_file.read_text(encoding="utf-8")
                converted = convert_content(content, rel_depth, str(rel_path))
                out_file.write_text(converted, encoding="utf-8")
                count += 1

    print(f"Successfully converted and synced {count} documentation files into {dest_dir}.")


if __name__ == "__main__":
    main()
