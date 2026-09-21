# Sandbox Server

Authoritative TCP game server.

Default development bind is `127.0.0.1:7777`. Bind address and port are
configurable. The protocol does NOT depend on localhost.

## Run

```text
dotnet run -- --host 127.0.0.1 --port 7777
```

`--host` must be an IP address (`127.0.0.1` for local-only, `0.0.0.0` to
listen on all IPv4 interfaces). Press Ctrl+C to stop.

## Architecture

- `ClientConnection` is a TCP session. It is not a player and is not a world.
- `PlayerRegistry` owns process-local player identity (`JOIN`).
- `PlayerStore` owns session inventory slots and Gem balance. It is not world
  membership and is discarded on disconnect.
- `World` owns one named in-memory world's occupants, 3-layer tile grid,
  stationary world-item entities, and the authoritative player simulation.
- `WorldDirectory` get-or-creates worlds and unloads them when empty.
- `SimulationScheduler` ticks occupied worlds at 60 Hz on one worker. Snapshots
  go out at 20 Hz. The client is never authoritative for pose, drops, inventory,
  or currency.
- A connected identified player is not in a world until `ENTER`.
- Broadcasts are scoped to members of that world. Inventory and Gems are
  owner-only.

There is no persistence. Restarting the server destroys all worlds.

## Simulation

Player position is the collision AABB bottom-center (feet), not a tile index.

- AABB is 0.8 wide × 0.95 tall.
- Spawn feet are `(spawnCellX + 0.5, spawnCellY)` standing on generated ground.
- Held left/right set horizontal speed. Jump is a one-shot edge, latched if
  multiple `INPUT` lines arrive before the next tick.
- Gravity, grounded jump, and AABB vs foreground solids run on the server.
- Dirt/Grass/Stone/Marker/Bedrock foreground tiles are solid. Deco is not.
  Sky is not a block (id 1 is unused). Background never collides. Bedrock is
  unbreakable and not player-placeable. Punching empty air is `empty_cell`.
- `LastProcessedSeq` / `ackSeq` is the highest input seq consumed in a tick
  (coalesced samples are covered by that max seq).
- Damaged blocks restore to catalog max health after 180 ticks (~3 s) and
  broadcast `TILE`. Destroyed (empty) cells do not regen.
- Destroying a block resolves `DropCatalog` once and spawn-or-merges world
  items under the world lock. Pickup is a tick overlap scan (drop point vs
  inflated feet AABB). Lowest overlapping player id wins that entity that tick.

## Protocol

Newline-delimited UTF-8. Temporary scaffolding.

Client to server:

```text
JOIN Alice
ENTER START
INPUT 1 0 1 0
INPUT 2 0 1 1
BREAK 50 41
SELECT 5
PLACE 51 41
CHAT hello there
LEAVE
```

`JOIN` registers identity only. `ENTER`/`LEAVE` are world membership.
`INPUT <seq> <left> <right> <jump>` is sequenced movement intent. `seq` is a
client-incremented uint. `left` / `right` / `jump` are `0` or `1`. Jump is
pressed-this-sample, not held. The server coalesces held left/right to the
latest sample and latches jump until the simulation consumes it.
`BREAK <x> <y>` is a punch intent. The server chooses the target layer:
Foreground if that layer is non-empty, otherwise Background. HyperForeground
is not auto-punched. Clients never send a layer, a damage amount, or remaining
health.
`SELECT <slot>` chooses a stable inventory slot index. Slots 0–1 are Fist and
Wrench (always occupied). Collectible slots start at 2. Empty collectible
slots cannot be selected (`empty_slot`). Consuming the last item in a
collectible slot auto-selects Fist.
`PLACE <x> <y>` places from the selected slot. Fist/Wrench and seeds are
`unplaceable`. The server derives block id and layer. Clients never send a
block id, layer, or `TILE`.
`CHAT <text>` is rest-of-line (spaces allowed). Rejected if empty, over 120
characters, or containing control characters/newlines (`invalid_chat`).
Broadcast only to members of the sender's world as
`CHAT <playerId> <name> <text>`. Extra lines in a short window are dropped.

Edit reach is authoritative Chebyshev distance:
`max(|x - Floor(feetX)|, |y - Floor(feetY)|) <= 3`.

Server to client:

```text
WELCOME <id> <name>
CONTENT_REV <revision>
GEMS <balance>
INVENTORY_BEGIN <capacity>
INV <slot> <itemId> <qty>
INVENTORY_END
SELECTED <slot>
WORLD_ENTERED <world> <id> <name> <x> <y>
WORLD_GRID <world> <originX> <originY> <width> <height>
TILE <x> <y> <bg> <fg> <hg> <bgHp> <fgHp> <hgHp>
WORLD_GRID_END
WORLD_ITEMS
ITEM <id> <item|gem> <typeId> <qty> <x> <y>
WORLD_ITEMS_END
PLAYER_JOINED <id> <name> <x> <y>
PLAYER_STATE <tick> <id> <x> <y> <vx> <vy> <grounded> <ackSeq>
ITEM_REMOVED <id>
PLAYER_LEFT <id>
WORLD_LEFT <world>
CHAT <playerId> <name> <text>
COLLECTED gem <qty>
COLLECTED item <itemId> <qty>
ERROR <reason>
```

Authoritative gameplay definitions live in `Content/*.json` (schemaVersion +
contentRevision). The server loads them at startup into dictionary catalogs
keyed by stable IDs (gaps and tombstones are allowed). `CONTENT_REV` is the
pack revision; the client may use it to detect a presentation mismatch. It is
not gameplay authority.

`JOIN` is followed by `CONTENT_REV`, `GEMS` and an inventory snapshot (`INVENTORY_BEGIN 32`,
occupied `INV` rows including Fist/Wrench in slots 0–1, `INVENTORY_END`,
`SELECTED 0`). Inventory and gems survive `ENTER`/`LEAVE` for the session.
Capacity is 32: two reserved tools plus 30 collectible slots. Tools cannot be
emptied, overwritten by pickup, consumed by `PLACE`, or dropped.

After `WORLD_ENTERED`, the server sends a sparse grid snapshot, then a world-item
snapshot (`WORLD_ITEMS`, `ITEM` rows, `WORLD_ITEMS_END`), then other members'
`PLAYER_JOINED`. `ITEM` is an upsert. `ITEM_REMOVED` deletes that entity.

`x` / `y` on player lines are feet in world units (invariant-culture floats).
World-item `x` / `y` are stationary world units. `grounded` is `0` or `1`.
`ackSeq` is that player's `LastProcessedSeq`. Receivers ignore ack unless the
id is local.

Accepted punches and places rebroadcast the same `TILE` cell line, including
remaining health per layer (`0` if that layer is empty). Destroy also
broadcasts `ITEM` upserts. Pickup broadcasts `ITEM` leftover or `ITEM_REMOVED`,
owner-only `INV` / `GEMS`, and owner-only `COLLECTED` for the quantity actually
transferred this tick. Do not infer pickups from `INV`/`GEMS` (those also sync
other future operations). JOIN snapshots and `PLACE` do not send `COLLECTED`.
Rejected edits send `ERROR` to the requester only. Successful place also sends
`SELECTED` so consuming the last stack returns the replica to Fist.

Block ids are integers. `0` is empty on that layer. Catalog `MaxHealth` is
definition data (Dirt/Grass/Stone: 3 punches; Bedrock: 1 and unbreakable).
Sky (id 1) is unused. Per-cell remaining health is world state, reset when a
block is placed or removed. One accepted punch deals 1 damage.

Default world size is origin `(0, -8)`, `100×68` (tiles `x` in `[0,99]`,
`y` in `[-8, 59]`). Dimensions live on `World`, not as protocol magic numbers.
Layout is the same for every world name: empty air above `y=40`, Grass FG +
Dirt BG at `y=40`, Dirt FG+BG on `y=0..39`, Bedrock FG + Dirt BG on
`y=-8..-1` (eight rows, no gap). Spawn feet are `(50, 41)` standing on Grass.
Edits persist in memory only while the world still has occupants.

Player and world names: `A-Za-z0-9_`, 1–16 characters. Player names are unique
ignoring case for this process. World names are unique ignoring case.

Additional error reasons: `invalid_world_name`, `already_in_world`,
`not_in_world`, `invalid_layer`, `out_of_bounds`, `out_of_range`, `empty_cell`,
`occupied`, `invalid_block`, `layer_not_allowed`, `unbreakable`, `unplaceable`,
`empty_slot`, `invalid_slot`, `invalid_chat`.

`ENTER` while already in a world is rejected. Leave first, then enter another
world. The client never sends a position, drop roll, inventory total, or
currency amount.
