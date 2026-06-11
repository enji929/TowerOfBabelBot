# TowerOfBabelBot

A BepInEx IL2CPP mod for **Tower of Babel v1.0.3a** that fully automates player movement and combat using a potential field algorithm.

Toggle the bot on/off with **F1** at any time.

![TowerOfBabelBot in action](preview.png)

---

## Описание (Russian)

BepInEx IL2CPP мод для **Tower of Babel v1.0.3a**, который полностью автоматизирует движение и бой игрока с помощью алгоритма потенциальных полей.

Включить/выключить бота — клавиша **F1**.

### Возможности

#### Движение и кайтинг
- **Алгоритм потенциальных полей** — враги отталкивают, лут притягивает, результат — плавный автоматический кайтинг
- **Дистанция боя** динамически масштабируется в зависимости от текущего HP: агрессивнее при полном здоровье, осторожнее при низком
- **Обход по кругу** — бот огибает ближайшую группу врагов вместо прямого отступления
- **Обнаружение угрозы сзади** — если враг заходит в тыл, бот делает боковой рывок
- **Обнаружение застревания** — если бот не двигается 1.5 с, включается случайный вектор побега (75% от желаемого направления)

#### Уклонение от снарядов
- Отслеживает все известные типы вражеских снарядов в реальном времени
- **Сэмплер 36 направлений** — оценивает 36 направлений побега на горизонте 0.6 с (10 временных сэмплов, сгущённых вблизи) и выбирает направление с максимальным расстоянием до снарядов
- **TTI-система срочности** — по времени до столкновения (time-to-impact) плавно смешивает побег с кайтингом
- **Штраф за путь к врагу** — снижает оценку направлений, ведущих в гущу врагов

#### Фаза боя с боссом
- Обнаруживает всех известных боссов (`Boss_DoomThunder`, `Boss_FinalBoss`, `Boss_FrostJudge`, `Boss_Hellblossom`, `Boss_LordNightmares`, `Boss_SkullKing`, `Boss_SwampPrincess`)
- Во время боя с боссом: дистанция боя ×1.4, радиус угрозы снарядов ×1.35

#### Сбор лута
- Отслеживает 11 типов предметов: опыт, золото, самоцветы, порошок, магнит, руна, жетон, жадность, аркана, еда, запечатывающий камень
- **Режим сбора (sweep)** — когда враги мертвы, бот фиксируется на ближайшем предмете и идёт к нему без осцилляций
- **Боевой режим** — притяжение лута активно, но ограничено до 55% от боевой силы
- Проверка пути к опыту — не побежит сквозь толпу за дальним шариком опыта
- Магниты — наивысший приоритет (×5), аркана ×3 при мане < 30%

#### Фазы поведения по HP
| Фаза | Условие | Поведение |
|---|---|---|
| **Нормальная** | HP > 45% | Стандартный кайтинг |
| **Осторожность** | HP 22–45% | Увеличенная дистанция боя |
| **Побег** | HP < 22% | Чистое отступление + приоритет еды |

#### Автоматизация скиллов
- **Автоатака и автоприцеливание** всегда включены
- **Правая кнопка мыши** удерживается при мане ≥ 35% и врагах в радиусе
- **Окно повышения уровня** — автоматически выбирает лучший скилл по системе приоритетов:
  - Апгрейд существующего скилла (+60 + 40×уровень) всегда предпочтительнее нового
  - **Учёт благословений экипировки** — читает `BlessLevel*` надетых предметов и даёт +80 к скорам скиллов класса, поддерживаемого экипировкой (Physical→11XX, Fire→12XX, Ice→13XX, Thunder→14XX, Light→15XX, Dark→16XX)
  - Тир-таблица: высокий приоритет — атакующие скиллы (×3), средний — пассивки (×2), низкий — поддержка (×1)

#### Производительность
- Нулевые аллокации в горячем пути — все коллекции предварительно выделены и переиспользуются
- IL2CPP-обёртки синглтонов (`GM`, `VM`, `SVM`) кешируются — устраняет ~350 GC-объектов/сек
- Кеш врагов/снарядов обновляется каждые 0.4 с; полный `FindObjectsOfType` раз в 3 с

### Установка

1. Установи [BepInEx 6 IL2CPP](https://github.com/BepInEx/BepInEx) в папку с игрой
2. Скопируй `GameDir.props.example` в `GameDir.props` и укажи путь к своей установке игры
3. Собери в Release:
   ```
   dotnet build -c Release
   ```
   DLL автоматически копируется в `BepInEx\plugins\` после успешной сборки
4. Запусти игру и нажми **F1** для включения бота

---

## Features

### Movement & Kiting
- **Potential field algorithm** — enemies repel, loot attracts, resulting in smooth automatic kiting
- **Engagement distance** scales dynamically with current HP — aggressive at full health, wide at low health
- **Circling** — orbits the nearest enemy cluster rather than retreating straight back
- **Rear-threat detection** — detects enemies coming from behind and injects a lateral push
- **Stuck detection** — if the bot hasn't moved enough for 1.5 s, a random escape vector kicks in (75% blend)

### Projectile Evasion
- Tracks all known enemy projectile types in real time:  
  `MonsterProjectile`, `IceBullet_Wizard`, `IceBall_Wizard`, `EliteAtk_FireBall`, `EliteAtk_LightningBall`, `DoomThunder_ThunderBall`, `DoomThunder_LightningProjectile`, `DoomThunder_LightningWave`, `Hellblossom_Fireball`, `Hellblossom_ZigzagFireball`, `FrostJudge_IceBallProjectile`, `FrostJudge_IceBeam`, `LordNightmares_Projectile`, `SwampPrincess_Projectile`, `FinalBoss_PoisonProjectile`, `FinalBoss_SkullBeam`
- **36-direction escape sampler** — scores 36 candidate directions over a 0.6 s lookahead horizon (10 front-loaded time samples) and picks the direction with maximum clearance from incoming projectiles
- **TTI urgency** — time-to-impact determines how strongly escape overrides kiting; full urgency when impact < 0.12 s
- **Alignment bonus** — among directions with similar clearance, those aligned with the kiting vector score up to 30% higher
- **Enemy-path penalty** — directions that run toward an enemy cluster are scored down

### Boss Phase
- Detects all known bosses: `Boss_DoomThunder`, `Boss_FinalBoss`, `Boss_FrostJudge`, `Boss_Hellblossom`, `Boss_LordNightmares`, `Boss_SkullKing`, `Boss_SwampPrincess`
- During boss fights: engagement distance ×1.4, projectile threat radius ×1.35

### Loot Collection
- Tracks 11 loot types: EXP, Gold, Jewel, Gem Powder, Magnet, Rune, Insight Token, Greed Essence, Arcane Ball, Food, Sealing Stone
- **Sweep mode** — when all enemies are dead, locks onto the nearest item and moves straight to it without oscillation, then picks the next nearest
- **Combat mode** — loot attraction is active but hard-capped at 55% of combat force magnitude
- Path-clearance check on EXP orbs — ignores orbs that require running through an enemy cluster
- Magnets highest priority (×5); Arcane Ball gets ×3 when mana < 30%

### HP-Based Behavior Phases
| Phase | Trigger | Behavior |
|---|---|---|
| **Normal** | HP > 45% | Standard kiting |
| **Caution** | HP 22–45% | Wider engage distance |
| **Flee** | HP < 22% | Pure retreat + food priority (×6) |

### Skill Automation
- **Auto-attack / Auto-aim** always enabled
- **RMB skill** held automatically when mana ≥ 35% and enemies are within range
- **Level-up window** — automatically selects the best available skill:
  - Upgrading an existing skill is strongly preferred (+60 + 40 per existing level)
  - **Equipment-bless-aware** — reads `BlessLevel*` arrays from the stat manager to detect which skill school is boosted by the current gear, then adds +80 to skills of the matching class (Physical→11XX, Fire→12XX, Ice→13XX, Thunder→14XX, Light→15XX, Dark→16XX)
  - Tier table: attacking skills (×3), passives (×2), support (×1)

### Performance
- Zero per-frame heap allocations in the hot path — all collections are pre-allocated and reused
- IL2CPP singleton wrappers (`GM`, `VM`, `SVM`) cached as static fields — eliminates ~350 GC-finalizer objects/sec
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
