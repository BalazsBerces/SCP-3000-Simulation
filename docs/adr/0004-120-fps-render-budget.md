# 120 fps render budget

The target is at least 120 fps at 1080p on the reference machine (RTX 2070 Super, Ryzen 7 3700X, 16 GB). This rules out SDFGI and unlimited shadowed lights despite the lamp-heavy look: only a small budget of nearby lamps cast shadows, the rest are unshadowed clustered lights; volumetric fog runs at reduced resolution; Furniture is instanced; the short haze-limited view distance is exploited with occlusion culling and LOD; and the simulation runs on its own thread, decoupled from the frame rate.

## Amendment (ticket 05d): Lamp light budget

The real-light budget is **256 clustered OmniLight3Ds, of which 4 cast shadows**, chosen by distance from the camera (it was 48, chosen around the floor focus). The benchmark also gates the 1% low: every measurement must reach **120 fps average and 75 fps 1% low** with MSAA 4x and TAA. The final reference-machine run with 256 lights measured 177.3 / 85.1 (open), 186.8 / 95.9 (Closing time) and 136.7 / 104.1 fps (farthest zoom), so the fps budget is met with headroom. The benchmark enforces the resolution and both antialiasing settings as well as frame rates; evidence is in [ticket 05d verification](../ticket-05d-verification.md).

The budget could grow because every other Lamp's light is now computed in the opaque materials ([ADR 0005](0005-lamp-pools-in-materials.md)), which replaced 5,000+ screen-space pool boxes and their overdraw. More real lights now buy little: pools are shaded like real lights, so the budget mainly decides which nearby Lamps may cast shadows. (Ticket 06i raised the shadow budget to 64 and replaced the distance-based shadow fade with a time-based one; see the amendment below.)

## Amendment (lighting review, 2026-10-14): 120 fps is the target, 90 fps the gate

Realistic shadows and Lamp switching matter more to the look than the last frames above 90 fps, so the benchmark now fails only below **90 fps average or 60 fps 1% low** (still at 1080p with MSAA 4x and TAA). **120 fps average stays the target**: each measurement reports whether it met it, and optimisations aim to reach it without visible quality loss.

Reference-machine results with soft Store-light shadows and SSAO (ticket 06h; full 20 s run, 1080p, MSAA 4x, TAA), average / 1% low: **Open 135.8 / 71.7 fps** (190.1 / 96.8 before shadows), **Closing time 180.7 / 89.2**, **Closing time far 108.0 / 88.6**, **Closing time at the LOD switch 109.2 / 87.5**. All meet the 90 / 60 gate; Open meets the 120 target, the two far Closing-time views miss it as they did before this ticket (107.2 / 108.4). To get there the shadows use filtered blur instead of PCSS (PCSS gave Open a 54 fps 1% low), their reach is capped at 180 m, SSAO runs at low quality and is off while the Store lights are out. Evidence: [ticket 06h verification](../ticket-06h-verification.md).

## Amendment (ticket 06i): 64 shadow-casting Lamps

The shadow budget is now **64 of the 256 real lights**, the largest that passed every benchmark run (32 failed the 1% low in Godot's default mixed-size shadow atlas, 96 failed with 512 px slots). Instead of fading towards the nearest unshadowed Lamp, which left the four shadows faint, each shadow slot fades over 0.4 s: a Lamp that drops out of the 64 fades its shadow out before the slot passes to the next Lamp, which fades in. The nearest Lamps therefore cast full-opacity shadows, and no visible shadow appears or vanishes at once. The positional shadow atlas is 8192 px with each quadrant split 4×4, 64 slots of 1024 px in all (about 128 MB), so lights never move between quadrants of different sizes (each move redraws a shadow map), and a real light is moved only when its Lamp changes.

Reference-machine results (full 20 s run, 1080p, MSAA 4x, TAA), average / 1% low: **Open 152.7 / 75.6 fps**, **Closing time 155.8 / 66.9** (183.9 / 84.8 with 4 shadows), **Closing time far 124.3 / 97.0**, **Closing time at the LOD switch 121.2 / 90.4**. All pass the 90 / 60 gate and meet the 120 target. The shadow passes cost CPU render time (1.8 → 3.1 ms at Closing time), so the Closing-time 1% low is now the tightest number. Evidence: [ticket 06i verification](../ticket-06i-verification.md).
