# Godot 4 as the engine

The target look is realistic and atmospheric (Frostpunk-like: volumetric haze, many real-time lamp lights, PBR furniture from CC0 packs), which rules out a browser Three.js build. Unity HDRP and Unreal 5 look stronger but are editor-bound and hard to drive from code; Godot 4 (Forward+) gives volumetric fog and clustered lights while keeping text-based scenes that can be built and tested from code. Code is C# (.NET), so the simulation core can be unit-tested outside the engine.
