# EngineContext

**One click → your Unity project becomes a `CLAUDE.md` that AI coding agents actually understand.**

EngineContext scans your open Unity project (read-only), builds a vendor-neutral snapshot, and generates a curated `CLAUDE.md` at the project root — so Claude Code / Cursor and other AI agents know your scenes, components, prefabs, ScriptableObjects, packages, and architecture conventions **without you re-explaining them every session**.

## Install

Add the package to your Unity project (2022.3 LTS ~ Unity 6):

- **git URL**: `Window > Package Manager > + > Add package from git URL : https://github.com/shin3010/engineContext.git`
- **local**: `Window > Package Manager > + > Add package from disk...` → select `package.json`

Installation succeeded when the menu item appears: `Tools > EngineContext > Generate Context`.

## Usage

1. `Tools > EngineContext > Generate Context` — no settings, no questions.
2. Wait while it scans (read-only; cancellable).
3. Review the generated draft (read-only preview).
4. Click **Apply to Project Root** — `CLAUDE.md` is written. Done.
5. When the project changes, run the same menu again (regenerate & overwrite).

## What it captures

Scene hierarchy & components (existing types only — prevents AI hallucinating components), prefab inventory + usage, ScriptableObject field schemas (names/types only, never values), folder & asmdef map, installed packages, curated project settings, and rule-based inferred conventions (DI container / MVVM·MVP·MVC tendencies / naming rules — always with evidence, never asserted without it).

## Guarantees

- **Read-only.** The tool never modifies your project (except writing `CLAUDE.md` on explicit Apply).
- **Deterministic.** Same project state → byte-identical output (git-diff friendly).
- **Local only.** No server, no LLM, no account, no telemetry. Rule/template based — zero cost.
- **Editor only.** Zero runtime code.

## Roadmap

v0.2+ will introduce an intermediate **Context Model** layer between the snapshot (IR) and formatters, enabling additional output formats (MCP Resource, `.cursorrules`, `AGENTS.md`). See the TODO in `Editor/Formatting/ClaudeMarkdownFormatter.cs`.

## engineContext landing page
https://shin3010.github.io/engineContext-website/
