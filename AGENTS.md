# AGENTS.md

<!-- handoff:learnings:begin -->
## Session Learnings (auto-updated by handoff)

### Implicit Rules
- User communicates in German; UI, README, commit messages in German (ASCII umlaut transliteration in code strings)
- Develop on branch claude/catia-curve-point-axes-94gj2h only; no PR unless asked
- dotnet-sdk-8.0 installed via apt in container (not persistent); NuGet reachable
- CATIA enum constants CAT_AXIS_SAME=1 / CAT_AXIS_OPPOSITE=2 written from memory — may need swapping
- Run tests: dotnet test Achsen.sln from repo root

### Key Decisions
- Decision: VB.NET WinForms targeting net48 — Reason: user asked for VB.NET; net48 preinstalled on CATIA workstations
- Decision: COM late binding (Option Strict Off in CatiaSession.vb only) — Reason: no CATIA interop DLLs needed, works across V5 releases
- Decision: curve sampling inside CATIA via SystemService.Evaluate VBScript: all sample points into temp geoset, ONE Part.Update, read coords, ONE delete of geoset (user request) — Reason: one COM roundtrip, avoids per-point updates and GetPoint array marshalling issues
- Decision: split side chosen by probe midpoint min-distance — Reason: length-based choice ambiguous when both pieces equal length
- Decision: rules half-open [min,max); gaps -> nearest rule (finer on tie); above all -> abstand_sonst — Reason: user's example ranges 10-15/16-30 have gaps
- Decision: median smoothing + min section length merge — Reason: prevent class flicker at transitions
- Decision: X tangent projected onto surface tangent plane (option) — Reason: guarantee X perpendicular to Z
- Decision: Grenzraster default 0 (off) — Reason: keep prior behavior; user can set 1 for integer-mm boundaries
- Decision: Core multi-targets netstandard2.0;net48 — Reason: app avoids netstandard facade dependency

<!-- handoff:learnings:end -->
