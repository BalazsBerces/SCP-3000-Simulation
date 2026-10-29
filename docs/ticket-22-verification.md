# Ticket 22: Light and power

Issue: #23. Spec: #1, including the ticket's display-Lamp switching comment. Fixed point: `9dbdc201f306586376a1b5d273836349c415c7b6`.

## Built

- Moving a Lamp away from its display disconnects Store power. At Closing time the nearest Village within 24 m supplies batteries, four Store hours per battery per Lamp, or power from a Villager cranking a hand-cranked generator. Generator power takes priority and preserves the remaining battery charge. Rotation alone does not disconnect a Lamp.
- A healthy Villager holding the existing generator Gear cranks within 2 m of home. Cranking reserves their work time and costs an additional quarter of their rest per Store hour. Hunger, thirst, danger, exhaustion, leaving home or other reserved work stops production. No electricity is stored by the generator. Its existing Materials, tools, Recipe and electrics bench requirements remain in use.
- The existing Candle Gear burns over four Store hours of Closing time, with brightness 0.3 and radius 4 m. Carried flashlights and lanterns retain the existing battery consumption. All expose Lit areas with position, height, radius and brightness, alongside lit Furniture Lamps.
- Local brightness extends Staff detection from 12 m in darkness towards 40 m in full light, respecting the existing Furniture, Pillar and Fog bank sight checks. Lit Villagers continue their ordinary community work and lose less morale. No Staff movement or attraction rule depends on light. Noisy construction keeps its existing open-hours restriction.
- Awake Villagers within 4 m of a nearby display Lamp switch it on during open hours without a Village battery or generator; it switches off again when nobody is nearby. Automatic display-Lamp switching around Closing time remains unchanged. Moved, wrecked and dead-zone Lamps never receive Store power.
- The existing Lamp renderer now reads power and switching from snapshots. Village Lamps and carried lights share its real-light/shadow budget, material pools and halos, including Ghost copies. A carried light's source halo remains readable at far zoom while the Candle's surface illumination stays weak. Cranking reuses the existing working pose.
- Save format 15 retains power disconnection, remaining battery hours, display switches, cranking action and Candle durability. Loading validates Lamp references, uniqueness and battery bounds, and reconstructs Lit areas. Previous save versions are rejected with the existing explicit incompatible-version error.
- The full Store benchmark exposed a pre-existing Crop-renderer scan of all 415,000 Furniture pieces every frame. Its lookup now follows the existing reference-cache pattern and refreshes when Furniture changes. This small prerequisite fix restored the performance gate.

## Decisions for the unattended run

The generator is held Gear, rather than a new placed appliance. Candles also reuse their existing carried Gear representation. A Lamp's Store disconnection is permanent once carried away; reconnecting a returned Lamp and power wiring are not introduced. Multiple moved Lamps can use one Village generator while its holder cranks. These are reversible rules and tuning choices, so no ADR was added.

Reuse only: existing FloorLamp Furniture, `goods_candles`, CampLantern and generator Gear meshes. No new model, prop, texture or procedural geometry was added; no asset search/download/conversion or new credits were needed under ADR 0006. The existing generator mesh already combines downloaded Gear meshes. Generated items added by this ticket: **none**.

The Horde subsystem belongs to the next stage. Detection is tested against an approaching group of two Staff using the facade's existing patrol movement, including a comparison proving their positions are unchanged by light.

## Verification

Full command output and earlier failing checks are retained under `.ticket-logs/22-*`.

- Eleven new facade cases cover battery depletion, cranking production and rest cost, Candle consumption, earlier detection, unchanged Staff movement, continued work and morale, three save/load continuations, daytime switching, light across a Seam away from a Village, and removal of saved Lamp power. All pass in Release. The earlier focused lighting/persistence run passed 29 cases.
- Windowed `--villager-sandbox --sandbox-preset=empty --pause --power-check=<absolute directory>` passes all checks. Battery and cranked Lamps and a Candle render near and at maximum zoom; near sources use real lights and far sources retain halos and material pools. Rendered Candle source pixels exceed the visibility threshold at both zooms (red channel 0.796 near, 0.620 far). Exhausting all three sources removes their real lights. Godot reports a non-failing ObjectDB shutdown warning for six resource instances after this check.
- Screenshots, inspected once after the final correction: [near](screenshots/22/power-near.png), [maximum zoom](screenshots/22/power-far.png), both 1280 × 720. The two floor pools and the weaker Candle source are visible; maximum zoom shows all three repeated across the sandbox Seams.
- Solution Release build passes with zero warnings/errors; Godot 4.7.2 .NET headless build passes.
- Full 20-second-per-view benchmark passes at 1920 x 1080 on RTX 2070 SUPER, MSAA 4x and TAA: Open 136.3 / 68.1 fps, Closing time 155.3 / 77.7, far 120.9 / 87.5, LOD transition 107.4 / 75.7 (average / 1% low). Every view clears the ADR 0004 gate of 90 average / 60 low; the LOD transition remains below the 120 fps target.
- Final whole Release suite passes: 944 passed, zero failed, zero skipped, 5 m 44 s. The final focused PowerTests run also passes all 11 cases, including immediate generator interruption when its Villager is sent away.

Human review remains: Candle brightness and halo size, the existing generator model and reused cranking pose, transitions while moving lights, and lighting/work/rest pacing in populated Villages. Large crowds carrying moving lights rebuild the material pool grid; incremental cell updates are deferred until such crowds justify them. No new audio was added or assessed.

Suggested rule: Cache presentation Furniture lookups by the snapshot's Furniture reference and rebuild them only when it changes. Scanning all 415,000 pieces every render frame made the full Store benchmark fail even without the new lights.
