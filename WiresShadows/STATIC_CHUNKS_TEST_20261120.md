# Static shadow chunks, local experiment, 2026-11-20

Baseline: current unreleased Shadows source (the handoff calls it 0.3.1; Plugin.cs still declares 0.3.0).
No version was bumped. Do NOT publish this experiment without benchmark and visual confirmation.

What changed
- New src/StaticShadowChunks.cs creates 8m spatial chunk buckets from real Piece.Awake/OnDestroy events. Eligibility is intentionally conservative: static opaque MeshRenderer pieces only; exclude items, lights, Animator, Rigidbody, doors, machines, LOD groups, alpha cutouts, per-renderer property overrides, non-static-batch-compatible meshes, and snow variants.
- Combine eligible submeshes into chunks of up to 24,000 source vertices using Mesh.CombineMeshes. Combine work is spread over frames only while a build queue exists; no idle per-frame scanning. Source renderers are unchanged during normal gameplay.
- Cached shadow face only: suppress individual source renderers temporarily, then submit one front/back (two shader passes) per combined mesh using the EXISTING WireShadowFrontBack shader and existing CameraEvent.AfterForwardOpaque command buffer. Existing DrawFaceCore finally restores the original forceRenderingOff flags. This leaves normal game/sun rendering unaffected.
- If a chunk is dirty/unbuilt/too small, or any source renderer is hidden or has left the camera's static layer mask, use the original native renderers instead. On mesh combine failure, the entire chunk reverts to native rendering.
- WearNTear mesh-health changes mark chunks dirty and call the existing PieceChanged invalidation. Piece placement/destruction mark chunks dirty, avoid stale chunks and allow world unloading. On shutdown, meshes and queues are released.
- New setting General > Static shadow batching (default On) provides an immediate A/B toggle, no restart. Disable to compare with previous native caster rendering.
- The Camera.RenderToCubemap operation itself was not removed. This patch aims to lower its submitted draw calls, not the fixed native camera overhead.

Evidence
- 2026-11-19 profiler capture at the base: Shadows 2.584ms/frame, DrawFaceCore 1.693ms/frame, ~13.5 faces/s, average ~5ms/capture, frame 19.51ms (51.3fps). User reports a previous isolate ~0.7ms camera overhead, not independently remeasured.
- Compilation: success, 0 errors, 1 PREEXISTING CS0618 warning in Bench.cs. No evidence of performance improvement until a settled in-world A/B capture.

Testing
- Test base after FarLands settled. Toggle General > Static shadow batching and capture the same camera view with Keypad9. Compare shadows' self CPU, static face time and camera/main rendering time. Toggle off if worse.
- Check walls, roofs, snapped beams, cutouts, damaged and repaired WearNTear pieces, placed/broken pieces, doors, food, lights, teleport, and normal sun shadows.
- Check that the effective number of eligible meshes is nontrivial. If no meshes are readable or no chunk meets the threshold, this patch will do little and should not be published.

Backups
- backup/STATIC_CHUNKS_20261120 contains original ShadowCache.cs/Plugin.cs/OnePass.cs and original installed WiresShadows.dll.
- Run Restore.cmd with Valheim closed to restore the DLL to Enngard AND package, staged through .new.
- Run RestoreSource.cmd to restore the source, moving StaticShadowChunks.cs into src/_removed (not deleting it).

Final safety refinement: WearNTear events only rebuild a chunk when its visual health tier crosses the 75%/25% threshold, to avoid continual redraws on periodic health updates. Snapshot the piece list before yielding during chunk construction, and clean up partially built meshes at shutdown. Exclude animated machines and snow variants. On failure to combine, fall back to native per-object rendering. Existing build has one pre-existing CS0618 warning in Bench.cs.
Final local build SHA256: 0C3CAB76FE0FFFBBE402DE1833FCC8983262E0704769F781179579E33A4C9B71.
Startup smoke-test: Wire's Shadows 0.3.0 loaded to main menu without relevant patch or loader errors. No in-world measured speedup claimed.

