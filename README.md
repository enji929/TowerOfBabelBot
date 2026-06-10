# TowerOfBabelBot

A BepInEx IL2CPP mod for **Tower of Babel v1.0.3a** that fully automates player movement and combat using a potential field algorithm.

Toggle the bot on/off with **F1** at any time.

---

## Features

### Movement & Kiting
- **Potential field algorithm** — enemies repel, loot attracts, resulting in smooth automatic kiting
- **Engagement distance** scales dynamically with current HP — plays more aggressively at high health, more defensively at low health
- **Circling** — orbits the nearest enemy cluster rather than running straight away
- **Rear-threat detection** — detects enemies sneaking up from behind and injects a lateral push to escape

### Projectile Evasion
- Tracks all known enemy projectile types in real time:  
  `MonsterProjectile`, `IceBullet_Wizard`, `IceBall_Wizard`, `EliteAtk_FireBall`, `EliteAtk_LightningBall`, `DoomThunder_ThunderBall`, `DoomThunder_LightningProjectile`, `DoomThunder_LightningWave`, `Hellblossom_Fireball`, `Hellblossom_ZigzagFireball`, `FrostJudge_IceBallProjectile`, `FrostJudge_IceBeam`, `LordNightmares_Projectile`, `SwampPrincess_Projectile`, `FinalBoss_PoisonProjectile`, `FinalBoss_SkullBeam`
- **24-direction escape sampler** — evaluates 24 candidate directions over a 0.55 s lookahead horizon and picks the one with maximum clearance from incoming projectiles
- Urgency system — smoothly blends escape direction with combat kiting based on how close the nearest projectile threat is

### Loot Collection
- Tracks 11 loot types: EXP, Gold, Jewel, Gem Powder, Magnet, Rune, Insight Token, Greed Essence, Arcane Ball, Food, Sealing Stone
- **Sweep mode** — when all enemies are dead the bot locks onto the nearest item and moves straight to it, then picks the next nearest. No oscillation
- **Combat mode** — loot attraction is active during combat but capped so it never overrides kiting logic
- Path-clearance check on EXP orbs — won't run through an enemy cluster to grab a distant orb
- Magnets are treated as high-priority loot (5× weight)

### HP-Based Behavior Phases
| Phase | Trigger | Behavior |
|---|---|---|
| **Normal** | HP > 45% | Standard kiting |
| **Caution** | HP 22–45% | Wider engage distance |
| **Flee** | HP < 22% | Pure retreat + food priority |

### Skill Automation
- **Auto-attack / Auto-aim** always enabled
- **RMB skill** held automatically when mana ≥ 35% and enemies are within range
- **Level-up window** — automatically selects a skill after a short delay (configurable priority table in `BotController.cs`)

### Performance
- Zero per-frame heap allocations in the hot path — all collections are pre-allocated and reused
- IL2CPP singleton wrappers (`GM`, `VM`, `SVM`) cached as static fields — eliminates ~350 GC-finalizer objects/sec that caused lag spikes after 2–3 minutes
- Enemy/projectile cache refreshes every 0.4 s; full `FindObjectsOfType` projectile scan only every 3 s

---

## Installation

1. Install [BepInEx 6 IL2CPP](https://github.com/BepInEx/BepInEx) into your Tower of Babel directory
2. Copy `GameDir.props.example` to `GameDir.props` and set the path to your game installation
3. Build in Release:
   ```
   dotnet build -c Release
   ```
   The DLL is automatically copied to `BepInEx\plugins\` on successful build
4. Launch the game and press **F1** to toggle the bot

---

## Credits

**Author:** [enji929](https://github.com/enji929)

Movement algorithm ported from [brotato-full-autobot](https://github.com/nicholasosto/brotato-full-autobot) by HelpFreedom (GPL-3.0).

---

## License

[GNU General Public License v3.0](LICENSE)
