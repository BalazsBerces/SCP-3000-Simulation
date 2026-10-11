# 120 fps render budget

The target is at least 120 fps at 1080p on the reference machine (RTX 2070 Super, Ryzen 7 3700X, 16 GB). This rules out SDFGI and unlimited shadowed lights despite the lamp-heavy look: only a small budget of nearby lamps cast shadows, the rest are unshadowed clustered lights; volumetric fog runs at reduced resolution; Furniture is instanced; the short haze-limited view distance is exploited with occlusion culling and LOD; and the simulation runs on its own thread, decoupled from the frame rate.
