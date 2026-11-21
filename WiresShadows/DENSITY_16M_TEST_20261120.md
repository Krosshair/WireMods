# Static batching density trial, 2026-11-20

Based on the 19:16:52 OFF and 19:17:25 ON five-second captures at (766,39,-518).
The ON capture found 58 tracked 8m cells, zero active combined cells,
48 sparse rejections, four discarded builds, and zero cached shadow-face renders.
Consequently there was no measured static batching benefit.

Local trial:
- Widen spatial grouping from 8m to 16m in X/Y/Z.
- Reduce the minimum piece and eligible-renderer threshold from 12 to 8.
- Preserve the minimum 3:1 expected submission reduction.
- Keep existing opaque-only, immobile, LOD/alpha and other safety restrictions.
- In Wire's Profiler output, report aggregate tracked pieces, scanned/eligible
  renderers, eligible source submeshes, rejected cells by reason, and
  ten densest cells with their estimated combined-mesh counts.
- No permanent per-frame worker. Existing one-shot chunk preparation remains.
- No changes to Shadows' version, shader files, other mods or public packages.

Profiler status codes: 0 pending; 1 <8 pieces; 2 <8 eligible renderers;
3 <3:1 estimated submission reduction; 4 active combined chunk; 5 combine failure.

The next ON and OFF captures should be at the same camera view. If the
caches are settled, per-face counters will be zero even if active chunks
exist. A forced cache rebuild is required for a per-face effectiveness test.

Rollback:
backup/DENSITY_16M_20261120/Restore.cmd
backup/DENSITY_16M_20261120/RestoreSource.cmd

Compile: passed, zero errors, existing Bench.cs CS0618 warning only.
Local installation verified by SHA256 F0E6012F5E46EFDC16D978C7ADF4F0791A9170675ED1B4538BB301980A4FA78E.
No in-world benchmark has been recorded for this trial.
