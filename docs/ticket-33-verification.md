# Ticket 33 verification: Splits, new Villages and new eras

Core only; checked by `SplitTests` (overcrowded and low-morale splits, large and distant Outposts, no split for a content Village, new era after the last fall with Wanderers still arriving, era kept in a save).

Decisions made in the unattended run:
- Overcrowded = more than 12 people beyond the Village's covered sleeping places, so building Dwellings lifts the limit (no size cap). Low morale = average morale below 0.25. Half the Village leaves (newest arrivals when crowded, the least content in conflict); each side needs at least 3 people; a Village splits at most once a Store day.
- A crowded Village with an active Outpost first sends its overflow there; an Outpost of 5 people, or one over 64 m from home that has stood a Store day, becomes a Village at its own position. Its barricade stays an abandoned Outpost wall of the old Village.
- Leavers take their share of the Goods (Materials, Gear and buildings stay), the Known map, Recipes, Personality and Horde Belief; the new site is the best known one at least 48 m from every standing Village.
- Splits are a hidden tuning like Arrivals and Horde nights: on by default, off in sandboxes and showcases unless `WorldConfig.Splits` is given, because morale runs down within a few Store days in untended sandbox Villages and would split the other features' test Villages.
- The new era begins at the fall of the last peopled Village (`EraBegan`, `WorldSnapshot.Era`, chronicle line).

Human review: new Villages are drawn by the existing Village renderer (no new models, props or textures, so no model search was needed); check in a long run that a split Village appears and its people walk to it.
