#!/usr/bin/env python3
"""
Génère un rapport Markdown des contributions Git par auteur.
Périodes : dernière semaine, 2 dernières semaines, total.

Usage:
    python scripts/contributions.py
    python scripts/contributions.py --output CONTRIBUTIONS.md
"""

import subprocess
import argparse
from datetime import datetime, timedelta
from collections import defaultdict


# ── Git helpers ───────────────────────────────────────────────────────────────

def git(*args) -> str:
    result = subprocess.run(
        ["git"] + list(args),
        capture_output=True, text=True, check=True
    )
    return result.stdout.strip()


def get_authors() -> list[str]:
    raw = git("log", "--format=%aN")
    seen = set()
    authors = []
    for name in raw.splitlines():
        if name and name not in seen:
            seen.add(name)
            authors.append(name)
    return authors


def stats_for_period(author: str, since: datetime | None = None) -> dict:
    """
    Retourne les stats de commits, fichiers changés, insertions et suppressions
    pour un auteur donné, optionnellement depuis une date.
    """
    log_args = ["log", f"--author={author}", "--oneline"]
    diff_args = ["log", f"--author={author}", "--numstat", "--format="]

    if since:
        since_str = since.strftime("%Y-%m-%d")
        log_args += [f"--since={since_str}"]
        diff_args += [f"--since={since_str}"]

    commits_raw = git(*log_args)
    commits = len([l for l in commits_raw.splitlines() if l]) if commits_raw else 0

    numstat_raw = git(*diff_args)
    insertions = 0
    deletions = 0
    files_changed = set()

    for line in numstat_raw.splitlines():
        parts = line.split("\t")
        if len(parts) == 3:
            added, removed, filename = parts
            if added != "-":
                insertions += int(added)
            if removed != "-":
                deletions += int(removed)
            files_changed.add(filename)

    return {
        "commits":      commits,
        "files":        len(files_changed),
        "insertions":   insertions,
        "deletions":    deletions,
    }


def get_commit_list(author: str, since: datetime | None = None) -> list[str]:
    """Retourne la liste des messages de commit pour un auteur."""
    args = ["log", f"--author={author}", "--format=%s"]
    if since:
        args += [f"--since={since.strftime('%Y-%m-%d')}"]
    raw = git(*args)
    return [l for l in raw.splitlines() if l]


# ── Markdown builder ──────────────────────────────────────────────────────────

def stats_row(s: dict) -> str:
    return (
        f"| {s['commits']} | {s['files']} | "
        f"+{s['insertions']} / -{s['deletions']} |"
    )


def build_report(authors: list[str]) -> str:
    now = datetime.now()
    week1  = now - timedelta(weeks=1)
    week2  = now - timedelta(weeks=2)

    lines = []
    lines.append("# Rapport de contributions Git")
    lines.append(f"\n> Généré le {now.strftime('%Y-%m-%d à %H:%M')}\n")

    # ── Summary table ─────────────────────────────────────────────────────────
    lines.append("## Résumé\n")
    lines.append("| Auteur | Période | Commits | Fichiers | +Lignes / -Lignes |")
    lines.append("|--------|---------|---------|----------|-------------------|")

    periods = [
        ("Dernière semaine",      week1),
        ("2 dernières semaines",  week2),
        ("Total",                 None),
    ]

    for author in authors:
        for label, since in periods:
            s = stats_for_period(author, since)
            lines.append(f"| **{author}** | {label} {stats_row(s)}")

    # ── Per-author detail ─────────────────────────────────────────────────────
    lines.append("\n---\n")
    lines.append("## Détail par auteur\n")

    for author in authors:
        lines.append(f"### {author}\n")

        for label, since in periods:
            s = stats_for_period(author, since)
            lines.append(f"#### {label}\n")
            lines.append("| Commits | Fichiers touchés | +Lignes / -Lignes |")
            lines.append("|---------|-----------------|-------------------|")
            lines.append(f"| {s['commits']} | {s['files']} | +{s['insertions']} / -{s['deletions']} |\n")

            commits = get_commit_list(author, since)
            if commits:
                lines.append("**Commits :**\n")
                for msg in commits:
                    lines.append(f"- {msg}")
                lines.append("")
            else:
                lines.append("*Aucun commit sur cette période.*\n")

    # ── Repo-wide totals ──────────────────────────────────────────────────────
    lines.append("---\n")
    lines.append("## Totaux du dépôt\n")
    lines.append("| Période | Commits | Fichiers | +Lignes / -Lignes |")
    lines.append("|---------|---------|----------|-------------------|")

    for label, since in periods:
        log_args = ["log", "--oneline"]
        diff_args = ["log", "--numstat", "--format="]
        if since:
            since_str = since.strftime("%Y-%m-%d")
            log_args  += [f"--since={since_str}"]
            diff_args += [f"--since={since_str}"]

        total_commits = len([l for l in git(*log_args).splitlines() if l])
        ins = dels = 0
        files = set()
        for line in git(*diff_args).splitlines():
            parts = line.split("\t")
            if len(parts) == 3:
                a, d, f = parts
                if a != "-": ins  += int(a)
                if d != "-": dels += int(d)
                files.add(f)

        lines.append(
            f"| {label} | {total_commits} | {len(files)} | +{ins} / -{dels} |"
        )

    return "\n".join(lines) + "\n"


# ── Entry point ───────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="Rapport de contributions Git en Markdown.")
    parser.add_argument(
        "--output", "-o",
        metavar="FILE",
        help="Fichier de sortie (défaut : affiche dans le terminal)"
    )
    args = parser.parse_args()

    authors = get_authors()
    report  = build_report(authors)

    if args.output:
        with open(args.output, "w", encoding="utf-8") as f:
            f.write(report)
        print(f"Rapport écrit dans {args.output}")
    else:
        print(report)


if __name__ == "__main__":
    main()
