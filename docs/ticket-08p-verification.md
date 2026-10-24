# Ticket 08p verification: Water points and the few running showroom taps

## What was built

- `WorldSnapshot.WaterPoints`: `WaterPoint(Kind, Position, Running, FurnitureId)` for every Restroom washbasin (vanity units of a
  Restroom), the fountain court (its centre), the Camping ground's lake (`Landmark.Lake`, new) and every showroom sink.
- Showroom sinks: each Bathroom Display's vanity units and the first non-pantry kitchen cabinet of each Kitchen Display.
  `WaterConfig.RunningTapShare` (on `WorldConfig.Water`, default 5 %, rounded up) of them run; a seeded partial shuffle (stream 9) picks
  exactly that many, once at generation, so it never changes with time.
- Dev view: the Store overview (F4, zoomed out) shows W (blue, always there), T (green, running tap) and t (grey, dry tap) over every
  Water point (`LandmarkMarkers`). The normal presentation is untouched, so a running tap looks like a dry one.

## Decisions

- No new sink model: a "washbasin" and bathroom sink is the existing `VanityUnit`, a kitchen sink the Kitchen Display's first cabinet.
- Removing Furniture through the scenario builder also drops the Water point on it.
- "Same seed on every layout" is tested as: the same seed gives the same taps in each layout, and another seed gives others.

## Tests

`WaterPointTests` (8, Release). Full suite: 624 passed, 0 failed. Godot headless build and `dotnet build game`: no errors.

## Needs human review

The overview markers (F4, zoom out) are unchecked visually.
