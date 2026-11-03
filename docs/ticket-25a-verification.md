# Ticket 25a verification: Bands at Closing time and quiet night work

## Automated

- `BandNightTests` (core, facade level): cautious Band hides (ring of 8 bookcases, one awake on watch), bold Band moves on, builder Band barricades; a silent hiding Band is passed by a Horde within sight range, a Wounded (groaning) member draws it; a Taken member switches the Band's strategy in the morning (Hide -> Barricade), surviving a save/load round-trip; a cautious Village starts no noisy work at Closing time, a desperate one (a member Wounded) builds, is heard and draws Staff.
- Godot: `--villager-sandbox --sandbox-preset=empty --pause --band-night-check=<dir>` (after `--`) checks the strategy on the Communities panel and the Band marker, the coming switch, and both chronicle stories; captures `hiding.png` and `morning.png`.

## Decisions

- Desperate Village = one with a Wounded member (the Staff have struck). Low Morale was tried first, but sandbox Villages sink to 0 Morale within two nights, so nearly every Village would have built at night.
- The noisy-work gate sits where construction tasks start (`construction: true`, now also Furniture-yard hauling); planning stays quiet and goes on at night. Any Furniture moved at Closing time is heard.
- Quiet work: crafting at Closing time needs light (brightness >= 0.2). Cooking and sorting were not gated.
- A Band's shelter ring is built instantly at the start of Closing time (shortcut, no carrying animation); in the morning the Band pushes the pieces 1.5 m outwards to walk out, and they stay there.
- A Taken member always switches strategy; a Wounded one does half the time (Band random stream). Cycle: Hide -> Barricade -> Move on -> Hide.
- A noise from a source shut in behind Furniture used to make a Horde give up; it now searches from the near side.
- Beliefs do not exist in the core yet, so no Belief forms from a bad night (left for the Belief ticket).

## Needs human review

- The ring of Furniture round a hiding or barricading Band, in a lit area (the capture at Closing time is dark).
- Whether a hiding Band in a real Store picks a sensible room-set Display (the sandbox has none; untested automatically).
- No new models, props or textures were added.
