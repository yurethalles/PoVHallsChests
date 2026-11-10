# PoV Halls Chests

**PoV Halls Chests** is a server-side compatibility mod for **Path of Valheiman 6.0.6**.

Path of Valheiman has a special reward route for Halls of Oaths. That route can generate equipment directly and bypass the mod's usual `Equipment from chests` switch. This mod filters that special route so Halls rewards cannot contain equipment.

Currencies, runes, gems, ores, materials, and other non-equipment rewards remain available.

## What it removes

- Weapons
- Armor
- Shields
- Capes
- Accessories
- Tools
- Unique equipment

The filter covers Halls chest, trial, breakable, and encounter/boss reward rolls.

## What it preserves

- PoV currencies
- Runes
- Gems
- Ores and materials
- Other non-equipment reward items

## Requirements

- Valheim
- BepInEx 5
- Path of Valheiman **6.0.6**

## Server installation

1. Install Path of Valheiman 6.0.6 on the server and on every client, as normally required by PoV.
2. Copy `PoVHallsChests.dll` to the server's `BepInEx/plugins/` folder.
3. Restart the server.

This mod is server-side. Players do **not** need to install it for the filter to work.

## Configuration

On first server start, BepInEx creates `BepInEx/config/Dorn.PoVHallsChests.cfg`.

- `Enabled`: turns the Halls reward filter on or off.
- `Filter scope`: choose `AllEquipment`, `WeaponsOnly`, `ArmorOnly`, or `WeaponsAndArmor`.
- `Log removed items`: writes removed item prefab names to the server log for validation.

### Empty reward replacement

By default, `Replace empty rolls` is enabled. If every item in one Halls reward roll is removed as equipment, PoV Halls Chests adds one replacement instead of leaving that roll empty:

- A PoV currency, according to `Currency replacement chance` (default: `0.50`); or
- A material appropriate to the Hall progression level.

The replacement is only created when equipment removal would otherwise leave that individual roll empty. Existing non-equipment rewards are never replaced or increased.

The default is `AllEquipment`, which removes every equipment type while preserving currencies, runes, gems, and materials.

## Required PoV settings

Keep these Path of Valheiman settings disabled to block its normal equipment-loot routes as well:

```ini
Equipment from chests = false
Equipment from enemies = false
Equipment from bosses = false
Uniques from chests = false
```

PoV Halls Chests covers the separate Halls of Oaths reward route. It does not alter vanilla or other mods' own loot tables outside the Halls.

## Compatibility

Built and tested against Path of Valheiman 6.0.6. A future PoV update that changes its internal reward code may require an update to this mod.
