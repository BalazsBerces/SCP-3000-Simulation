# 120 fps render budget

The target is at least 120 fps at 1080p on the reference machine (RTX 2070 Super, Ryzen 7 3700X, 16 GB). This rules out SDFGI and unlimited shadowed lights despite the lamp-heavy look: only a small budget of nearby lamps cast shadows, the rest are unshadowed clustered lights; volumetric fog runs at reduced resolution; Furniture is instanced; the short haze-limited view distance is exploited with occlusion culling and LOD; and the simulation runs on its own thread, decoupled from the frame rate.

## Amendment (ticket 05d): Lamp light budget

The real-light budget is **256 clustered OmniLight3Ds, of which 4 cast shadows**, chosen by distance from the camera (it was 48, chosen around the floor focus). The benchmark also gates the 1% low: every measurement must reach **120 fps average and 75 fps 1% low** with MSAA 4x and TAA. The final reference-machine run with 256 lights measured 177.3 / 85.1 (open), 186.8 / 95.9 (Closing time) and 136.7 / 104.1 fps (farthest zoom), so the fps budget is met with headroom. The benchmark enforces the resolution and both antialiasing settings as well as frame rates; evidence is in [ticket 05d verification](../ticket-05d-verification.md).

The budget could grow because every other Lamp's light is now computed in the opaque materials ([ADR 0005](0005-lamp-pools-in-materials.md)), which replaced 5,000+ screen-space pool boxes and their overdraw. More real lights now buy little: pools are shaded like real lights, so the budget mainly decides which nearby Lamps may cast shadows. Shadows fade out towards the nearest Lamp without one, so the four shadows are faint when many Lamps stand about as near as each other; that is the price of a shadow budget without a visible shadow boundary.

## Amendment (lighting review, 2026-10-14): 120 fps is the target, 90 fps the gate

Realistic shadows and Lamp switching matter more to the look than the last frames above 90 fps, so the benchmark now fails only below **90 fps average or 60 fps 1% low** (still at 1080p with MSAA 4x and TAA). **120 fps average stays the target**: each measurement reports whether it met it, and optimisations aim to reach it without visible quality loss.
