# Changelog

All notable changes to this package will be documented in this file.

## [0.3.0] - 2026-07-07

### Changed — v0.3 Wiring-First Differentiation (Patch Spec v0.3, [A]+[B])
**[A] Formatter reorder + header summary** — no information removed, static reorder only:
- Section order changed: `## Event Wiring` promoted from 9th to right after Project Overview; serialized reference fields split out of "Public API (Custom Scripts)" into a new standalone `## Wired References` section placed right after Event Wiring (moved, not duplicated — Public API keeps fields/properties/methods only). All other sections retained, pushed down.
- Header now includes an honest one-line summary under the title: "This snapshot captures {N} Inspector-wired event binding(s) and {M} serialized reference field(s)…" — counts come straight from the scan (0 is shown as 0).
**[B] PromptComposer Focus layer (A-2, emphasis-only)** — language-neutral, rule-based, deterministic:
- Identifiers actually present in CLAUDE.md (from Event Wiring / Wired References / Public API / Hierarchy lines) are reverse-matched against the goal text (exact + surface normalization only; no semantic synonyms). Matches produce a `# Focus (auto-detected)` block listing related wiring items first, then Public API, then Hierarchy (capped, `(+N more)`).
- Invariant: `# Project Context` always contains the full CLAUDE.md unchanged. Zero matches, no candidates, or extraction failure → Focus omitted and output is byte-identical in shape to the v0.2 Option B template (safe failure). Request block mentions Focus only when Focus is present.
- Fixed over-matching: Focus selection now matches per-candidate on **whole identifiers** owned by that candidate (identifiers keep underscores and are not split further). Removed the previous sub-token intersection and the wiring→PublicAPI derived-token expansion, which let identifiers sharing a suffix (e.g. `botMode`/`friendMode`) or a common type token (e.g. `GameManager`) cross-select each other. Regression tests cover both directions.
- Fixed fragment over-matching: matching now resolves goal identifiers with **longest-match-first + span occupancy** — identifiers are tested longest-first and a shorter one is rejected where it overlaps a span already claimed by a longer match. So when `botMode` is matched whole, its fragments `bot`/`mode` no longer independently pull in unrelated candidates (`GameManager.mode`, `GameManager.Bot()`, `ModeManager.Bot()`). Deterministic (length desc, ordinal tie-break); regression test added.

### Added — v0.2 Wiring Capture (schema 1.4) + Prompt Builder
**Wiring Capture** — "what exists" → "what is wired to what" (same Pipeline/IR/Formatter structure):
- Multi-scene scan: a Full scan now additively opens every enabled Build Settings scene, scans, and restores your original open-scene setup (never saves to disk). If any open scene has unsaved changes, the run is blocked with a message to save first (or use current-scene scan).
- UnityEvent capture: persistent calls (e.g. `Button.onClick`) are read from serialized `m_PersistentCalls` and surfaced in a new `## Event Wiring` section (between Scene/Hierarchy and Key Components). Runtime `AddListener` calls are not serialized and are out of scope by design.
- Reference-field capture: object-reference serialized fields (public and private `[SerializeField]`, reference types only — scalars still excluded) now resolve the target's scene/asset path. Reconciled with the existing `keyRefs` capture and surfaced as a `Wired references` line under each script in "Public API (Custom Scripts)"; the standalone "Key References" subsection is retired (no duplication).
- Prefab wiring: the same event/reference capture is applied to prefab contents (root component summary unchanged).
- Declared reference fields: serialized reference fields are now surfaced even when unassigned or broken (marked `(unassigned)` / `(missing)`), so a private `[SerializeField]` reference slot no longer falls invisibly between the public-only "Public API" fields and the assigned-only wired refs. The per-script line is renamed "Wired references" → "Reference fields" to reflect that it lists declared slots (wired or not).

**Prompt Builder** (Feature Spec v0.2, Option B: wrap-only) — rerun of the cut "purpose input" idea:
- New `Tools > EngineContext > Build Prompt` menu + an in-window tab (no new window). `PromptComposer` wraps your goal + optional instructions + the current `CLAUDE.md` (inserted whole, no selection/compression) into a fixed rule-based template, shown as a read-only preview with Copy to Clipboard. Reads the existing `CLAUDE.md` via a single IO entry point (no re-extraction); guides you to Generate Context first if it's missing.

### Added (custom script public API summary — same Pipeline/IR/Formatter structure)
- Snapshot schema 1.3: added `customApis` — for each custom `MonoBehaviour` under `Assets/`, the `public` fields/properties/method signatures declared directly on the type (inherited Unity members excluded). Field values and method bodies are never included; this is the same "schema only" level already used for ScriptableObject `fields`.
- Extracted in `AssemblyFolderExtractor` (same pass that already scans `MonoScript` assets for `customComponentTypes`) via reflection with `Public | DeclaredOnly` binding flags. Property accessors, operators, and compiler-generated members are excluded. Types with zero public members (e.g. empty stub scripts) get no entry.
- Rendered as a new "### Public API (Custom Scripts)" subsection **inside** the existing "## Key Components" section (no new top-level section, no change to the fixed 10-section order), grouped per type as `**TypeName**` with `- Fields:` / `- Properties:` / `- Methods:` bullet lines, each only shown when non-empty.

### Changed (real-project review follow-up — same Pipeline/IR/Formatter structure)
- Snapshot schema 1.2: added `project.buildScenes` (scenes registered in `EditorBuildSettings`, build-index order preserved).
- `customComponentTypes` is now collected from all `MonoScript` assets under `Assets/` (via `AssemblyFolderExtractor`), not only from components attached in the currently open scene/prefabs. A custom script that exists in the project but isn't placed on any GameObject yet is no longer silently missing from "Project scripts (custom)".
- Key References (`components[].keyRefs`) are now collected only for non-engine (custom) `MonoBehaviour`s. Built-in serialized references that are identical across virtually every project (`Image.m_Sprite`, `Button.m_TargetGraphic`, TMP font/material, `Volume.sharedProfile`, etc.) are no longer collected, so this section only shows references that matter for understanding project-specific logic.
- Project Overview now shows "Scenes in build" from `EditorBuildSettings` (disabled scenes marked separately), independent of and in addition to the existing open-scene Hierarchy Summary.

### Changed (output curation — higher signal for AI, same architecture)
- Snapshot schema 1.1: added `customComponentTypes` (project/third-party scripts vs engine built-ins, classified by namespace).
- Tech Stack section now omits `com.unity.modules.*`, editor infrastructure packages (`ide.*`, `collab-proxy`, `test-framework`, `nuget.*`, etc.) and the tool itself, with an omitted count notice. IR still records all packages.
- ScriptableObject Catalog now lists only user-defined ScriptableObjects; built-in engine assets (URP settings, InputActionAsset, etc.) are omitted with a count notice in Notes.
- Key Components section now separates **Project scripts (custom)** (listed first) from **Unity / package built-ins**, and explicitly states when no custom scripts exist. Key References are ordered custom-first.
- Semantic inference (DI traces, MVVM/MVP/MVC suffix distribution) now considers custom types only — prevents false positives from built-ins like `CharacterController`.
- Done-state first-task suggestion prefers extractor-classified custom scripts.

## [0.1.0] - 2026-07-04

### Added
- Single menu entry point: `Tools > EngineContext > Generate Context` (zero-config).
- Single editor window with state machine: Generating → Review → Done (+ Error/Fallback).
- Read-only extractors: Project Settings, Packages, Folders & Assemblies (asmdef), Scene Hierarchy & Components (+ key references), Prefabs (+ usedBy), ScriptableObject field schemas.
- Vendor-neutral Snapshot (IR), JSON-serializable, deterministically sorted.
- Rule-based semantic inference: DI container (VContainer/Zenject traces), architecture pattern tendencies (MVVM/MVP/MVC suffix distribution), naming conventions — evidence required, no false assertions.
- Rule-based `CLAUDE.md` formatter: fixed section order, per-section caps, `(+N more)` truncation, total size budget with auto-summarize fallback.
- Review gate: nothing is written to disk before explicit Apply; overwrite confirmed once.
- Error handling for E1–E11 (unsupported version, no open scene, large-project fallback to current scene, broken refs skip & report, write failure with draft preserved, cancel with no file changes, etc.).
- EditMode tests: IR serialization roundtrip, formatter determinism & fixed section order.
