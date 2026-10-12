# Lamp pools are lit inside the opaque materials

Thousands of display Lamps are visible at once, but only a few hundred real lights fit the render budget ([ADR 0004](0004-120-fps-render-budget.md)). The light of every Lamp beyond that budget (its **pool**) is computed inside the opaque materials (the Store floor, Furniture and Pillars) and added as emission, from a world-space grid of the Lamps' bulbs that `LampPools` shares as global shader parameters. Each material shades the pools with its own albedo, normal (normal map included), roughness and metallic, with Godot's omni attenuation, Burley diffuse and Schlick-GGX specular, so a pool is lit the way the real light it complements would be, and Godot fogs it at the surface like any other lit colour.

We rejected the alternative the ticket first sketched: keeping the pools as a screen-space additive pass that reconstructs normals from depth and estimates albedo from the rendered colour. The rendered colour mixes albedo with ambient light, other Lamps and fog in-scattering, so the estimate breaks down exactly where the boundary has to vanish (inside Fog banks and where real lights fade into pools), and the pass cost overdraw for every Lamp in view. Measured against real lights on the same view, the in-material pools differ by at most 1–3/255 at the 99th percentile, apart from shadows.

## Consequences

- Furniture parts no longer use StandardMaterial3D but one `furniture.gdshader` that reproduces the features they use. Every new opaque material that Lamps can light must include `lamp_pools.gdshaderinc` and add `lamp_pools(...)` to its emission, or it stays dark where only pools reach it. Transparent and unshaded materials get no pool light.
- The pool BRDF copies Godot 4.7's light code (attenuation, the doubled light specular amount, the roughness limiter); an engine upgrade that changes it would bring back a visible difference between real lights and pools.
- Pools cast no shadows, like the unshadowed real lights.
