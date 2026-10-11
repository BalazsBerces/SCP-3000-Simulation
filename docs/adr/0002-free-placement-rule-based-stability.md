# Free placement of furniture with rule-based stability

Every piece of Furniture is an individual world object that can be moved, used or disassembled, and Villages can build upward (towers, structures climbing Pillars). We chose free placement (any position and rotation, no grid) over a 2.5D cell stack or a voxel grid, because the look of improvised furniture fortifications matters more than simulation simplicity. To keep this tractable, structural stability is rule-based (load capacity, weight and damage per piece, with a collapse animation when rules fail) rather than rigid-body physics, so the AI can predict and plan what it builds.

## Consequences

- Navigation needs a navmesh rebuilt locally (per chunk) whenever Furniture moves, with climbable pieces (ladders, stairs) as navigation links.
- AI construction cannot be pure free-form; it needs planned layouts that resolve to concrete piece placements.
