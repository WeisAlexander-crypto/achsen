---
date: 2026-11-21T03:19:35.079Z
project: achsen
session: c4e4a8c6
schema_version: 1
next_steps_count: 3
has_blockers: false
keywords: catia v5, vb.net, axis systems, curvature split, grenzraster, catiasession, achsen
headline: Goal: CATIA V5 app reading curves (selection or geoset), analyzing curvature radius, splitting curves into sections per 
---

# Session Handoff Snapshot
> **Generated:** 11/21/2026, 3:19:35 AM

## 🎯 High-Level Objective
* **Goal:** Goal: CATIA V5 app reading curves (selection or geoset), analyzing curvature radius, splitting curves into sections per configurable radius rules, placing points per rule spacing, creating surface-normal lines + axis systems (Z=normal, X=tangent of section), X/Z reversible. Intent: automate frame generation along curves for downstream use (purpose not yet stated by user).

## 📌 Current State & Next Steps
* **Status:** Done: full pipeline steps 1-7 in VB.NET, builds clean, 16 tests green, pushed. Remaining: real CATIA validation (sampling via Evaluate, split side, axis enum direction); optional improvements proposed but not built (lean/export-only mode, chordal-deviation spacing rule, split off by default) — awaiting user feedback on what axis systems are used for.
* **Next Action:** Await user's real CATIA V5 test results; fix based on log errors (Evaluate sampling, split side, axis direction enums)

### Remaining Queue
- [ ] Ask/learn what axis systems are used for (DELMIA/robot export vs CAD) to prioritize: lean/export-only output mode, chordal-deviation spacing rule, split off by default
- [ ] If user wants integer-mm boundaries by default, set grenzraster 1 in config/regeln_standard.json

## 🛠️ Modified Files Delta
* src/Achsen.Core/Settings.vb: Rule/Settings, JSON (System.Text.Json), validation, gaps, Grenzraster option
* src/Achsen.Core/Geometry.vb: circumradius, median filter, classify, runs/merge, SnapBoundaries, PlacePoints, Analyze, Segment.ShortLabel
* src/Achsen.App/CatiaSession.vb: selection, VBScript sampling, split with probe, points/normals/tangents/projection/axis systems, CheckSegmentLengths, ToggleAxes, RefreshDisplay restore
* src/Achsen.App/MainForm.vb: code-built UI (rules grid, PropertyGrid, results grid, log, reverse buttons, CSV export)
* src/Achsen.App/Program.vb, Achsen.App.vbproj, Achsen.Core.vbproj, Achsen.sln, build.bat, .gitignore
* tests/Achsen.Tests/*: 16 xUnit tests incl. Grenzraster
* config/regeln_standard.json: default rules + grenzraster 0
* README.md: German docs for VB.NET version

## 🚫 Failed Approaches (DO NOT RETRY)
* Approach: UseWindowsForms in SDK project on Linux → Result: WindowsDesktop targets missing → Lesson: for net48 plain System.Windows.Forms/System.Drawing references suffice
* Approach: xUnit 2.9.2 with VB tests → Result: BC30649 unsupported type (Span overloads) → Lesson: pin xunit 2.4.2 + runner 2.4.5
* Approach: run net48 exe under Ubuntu mono-runtime → Result: missing netstandard facade and Microsoft.VisualBasic → Lesson: install mono-devel, copy VB runtime dll from NuGet into scratchpad copy only (smoke test only)
* Approach: GroupBox AutoSize with Dock=Fill children → Result: stretched/collapsed layout → Lesson: use Anchor + fixed heights in AutoSize TableLayoutPanel

## 🔑 Crucial Context & Implicit Rules
* User communicates in German; UI, README, commit messages in German (ASCII umlaut transliteration in code strings)
* Develop on branch claude/catia-curve-point-axes-94gj2h only; no PR unless asked
* dotnet-sdk-8.0 installed via apt in container (not persistent); NuGet reachable
* CATIA enum constants CAT_AXIS_SAME=1 / CAT_AXIS_OPPOSITE=2 written from memory — may need swapping
* Run tests: dotnet test Achsen.sln from repo root

## Key Decisions
- Decision: VB.NET WinForms targeting net48 — Reason: user asked for VB.NET; net48 preinstalled on CATIA workstations
- Decision: COM late binding (Option Strict Off in CatiaSession.vb only) — Reason: no CATIA interop DLLs needed, works across V5 releases
- Decision: curve sampling inside CATIA via SystemService.Evaluate VBScript moving one point — Reason: one COM roundtrip, avoids GetPoint array marshalling issues and thousands of temp features
- Decision: split side chosen by probe midpoint min-distance — Reason: length-based choice ambiguous when both pieces equal length
- Decision: rules half-open [min,max); gaps -> nearest rule (finer on tie); above all -> abstand_sonst — Reason: user's example ranges 10-15/16-30 have gaps
- Decision: median smoothing + min section length merge — Reason: prevent class flicker at transitions
- Decision: X tangent projected onto surface tangent plane (option) — Reason: guarantee X perpendicular to Z
- Decision: Grenzraster default 0 (off) — Reason: keep prior behavior; user can set 1 for integer-mm boundaries
- Decision: Core multi-targets netstandard2.0;net48 — Reason: app avoids netstandard facade dependency

## 🙋 User Contribution (human-authored)
Specified full 7-step requirements (curve selection/geoset, radius analysis, sectioning, rule-based points, normal lines, axis systems with Z on normal and X on section, adjustable rules, reversible axes). Requested VB.NET port. Uploaded own console Program.vb/vbproj/README for review and asked to take only its better parts.

## 🙋 User Decisions (made by the human)
- **Implement as VB.NET instead of Python**
  - Rejected: Python + pywin32 version
- **Integrate only the better ideas from uploaded program into existing app**
  - Rejected: Fixing uploaded program standalone

## Summary
Built Python version first, then ported to VB.NET on user request (Python removed). Solution: Achsen.Core (rules+geometry), Achsen.App (WinForms, CATIA COM late binding), Achsen.Tests (xUnit, 16 pass). Smoke-tested UI under Mono/Xvfb. Reviewed user-uploaded console Program.vb (curvature split at integer mm): found GetPoint Double() array bug + length-only split ambiguity. Integrated its good ideas (Grenzraster, length-sum check, class names in section names, restore RefreshDisplay, backup hint). Pushed commit 596b483 to branch claude/catia-curve-point-axes-94gj2h. Nothing tested in real CATIA.

---
*A short hint surfaces on session start; full context loads only if your next prompt matches a keyword above, or via manual `/handoff-resume`. The same record is available as a sibling `.json` file for external tools.*