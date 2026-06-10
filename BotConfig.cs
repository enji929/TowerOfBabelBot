// Constants ported from HelpFreedom/brotato-full-autobot (GPL-3.0).
// Source: mods-unpacked/BlackTriangle-FullAutoBot/bot/config.gd
//
// Distance-scaling note: Brotato uses pixel coordinates (~350 px/sec player
// speed); Tower of Babel uses Unity units (~3 u/sec, ~100× smaller numbers).
// WORLD_SCALE = 0.01 bridges them. But coefficient constants K that appear in
// formulas like K*d, K/d, K/d² must be re-scaled per their distance-dimension
// to preserve the RELATIVE weights of forces. Doing this wrong (as I did
// initially) makes loot dominate monster repulsion by 10000× because of the
// 1/d in loot vs +d in engagement.
//
//   force ∝ K * d^n  ⇒  K_units = K_pixels / WORLD_SCALE^n

namespace TowerOfBabelBot;

public static class BotConfig
{
    public const float WORLD_SCALE = 0.01f;

    // Pure distances — scale directly
    public const float CONTACT_DANGER = 70f * WORLD_SCALE;
    public const float MIN_ENGAGE_DISTANCE = 95f * WORLD_SCALE;
    public const float SAFE_FALLBACK_DISTANCE = 450f * WORLD_SCALE;
    public const float DEFAULT_ENGAGE_DISTANCE = 400f * WORLD_SCALE;
    public const float SAFETY_DISTANCE = 250f * WORLD_SCALE;

    // ── Engagement (weapon-aware kiting) ─────────────────────────────────────
    // force = K * (engage - d)  → d^1 → K_units = K_brotato / WORLD_SCALE
    public const float ENGAGE_SPRING_K = 0.012f / WORLD_SCALE;          // 1.2
    // force = K * unit_vector  → d^0 → K stays as-is (constant-magnitude pull)
    public const float ENGAGE_PULL = 0.22f;
    public const float ENGAGE_PULL_THRESHOLD = 1.5f;                    // dimensionless
    public const float ENGAGE_HP_HIGH = 0.75f;
    public const float ENGAGE_HP_LOW = 0.25f;
    // force = K / d²  → d^-2 → K_units = K_brotato * WORLD_SCALE²
    public const float CONTACT_REPULSION = 4000f * WORLD_SCALE * WORLD_SCALE; // 0.4

    public const float CIRCLING_STRENGTH = 0.6f;                        // dimensionless
    public const float BOSS_WEIGHT = 2.5f;

    // ── Loot / consumables ───────────────────────────────────────────────────
    // force = K / d  → d^-1 → K_units = K_brotato * WORLD_SCALE
    public const float EXP_ATTRACTION = 180f * WORLD_SCALE;          // 3.0 — top priority
    public const float LOOT_ATTRACTION = 60f * WORLD_SCALE;          // 0.6
    public const float CONSUMABLE_ATTRACTION = 80f * WORLD_SCALE;    // 0.8
    public const float MAGNET_WEIGHT = 5f;                           // dimensionless multiplier

    // ── Sweep mode (all enemies dead) ────────────────────────────────────────
    public const float SWEEP_LOOT_MULTIPLIER = 2.5f;

    // ── XP path-clearance kiting ─────────────────────────────────────────────
    // Minimum enemy clearance (Unity units) required on the straight-line path
    // from the player to an XP drop. Below this, XP attraction is scaled down
    // quadratically so the bot won't run through an enemy cluster to grab EXP.
    public const float EXP_COLLECT_CLEARANCE    = CONTACT_DANGER * 2.5f;
    // In combat, ignore XP drops beyond this distance (prevents chasing far orbs).
    public const float EXP_MAX_COMBAT_RANGE     = SAFE_FALLBACK_DISTANCE * 3f;
    // Loot force is capped at this fraction of combat force so accumulated drops
    // can never override kiting logic, regardless of how many items are on the ground.
    public const float LOOT_COMBAT_CAP          = 0.55f;

    // ── Predictive lookahead ─────────────────────────────────────────────────
    public const float ENEMY_LOOKAHEAD = 0.25f;                         // seconds

    // ── Projectile escape (24-direction sampler) ─────────────────────────────
    public const float PROJ_THREAT_RADIUS = 110f * WORLD_SCALE;
    public const int ESCAPE_DIRECTIONS = 24;
    public const float ESCAPE_HORIZON = 0.55f;                          // seconds
    public const int ESCAPE_TIME_SAMPLES = 6;
    public const float ESCAPE_SAFE_CLEARANCE = 130f * WORLD_SCALE;
    public const float ESCAPE_PANIC_CLEARANCE = 45f * WORLD_SCALE;
    public const float ESCAPE_ALIGN_BONUS = 18f * WORLD_SCALE;          // d^1 multiplier on dot product
    public const float ENEMY_AVOID_DIST = 95f * WORLD_SCALE;
    // penalty = (gap) * K  → d^1 → K_units = K_brotato / WORLD_SCALE
    public const float ENEMY_AVOID_PENALTY = 3f / WORLD_SCALE;          // 300

    // ── Panic dodge (mirror-bullet trap fallback) ────────────────────────────
    public const float PANIC_BODY_REACH = 120f * WORLD_SCALE;
    public const float PANIC_BULLET_REACH = 140f * WORLD_SCALE;
    public const float PANIC_MIN_MAGNITUDE = 5f * WORLD_SCALE;

    // ── Rear-threat escape ───────────────────────────────────────────────────
    // If an enemy is behind the bot (dot(move_dir, enemy_dir) > threshold) and
    // closer than this distance, inject a strong lateral or forward push.
    public const float REAR_THREAT_DIST    = 200f * WORLD_SCALE;
    public const float REAR_DOT_THRESHOLD  = 0.3f;   // cos ~72° — "behind or side"
    public const float REAR_REPULSION_K    = 3.5f;   // constant-magnitude push

    // ── Boss phase ────────────────────────────────────────────────────────────
    public const float BOSS_CHECK_INTERVAL    = 2f;
    public const float BOSS_ENGAGE_MULT       = 1.4f;   // stay farther from boss
    public const float BOSS_PROJ_RADIUS_MULT  = 1.35f;  // larger projectile threat radius
    public const float BOSS_ESCAPE_CLEAR_MULT = 1.35f;  // dodge sooner during boss fight

    // ── Stuck detection ───────────────────────────────────────────────────────
    public const float STUCK_CHECK_INTERVAL   = 0.5f;
    public const float STUCK_THRESHOLD        = 0.12f;  // min movement per check (Unity units)
    public const float STUCK_ESCAPE_AFTER     = 1.5f;   // seconds until escape kicks in
    public const float STUCK_ESCAPE_STRENGTH  = 0.75f;  // weight of escape vs desired dir

    // ── Smoothing ────────────────────────────────────────────────────────────
    public const float MOVE_SMOOTHING = 0.72f;

    // ── HP-based behaviour phases ────────────────────────────────────────────
    public const float HP_FLEE    = 0.22f;       // below → flee mode (pure retreat + food)
    public const float HP_CAUTION = 0.45f;       // below → caution (wider engage distance)
    public const float FLEE_ENGAGE_MULT = 2.2f;  // flee/caution engage = SAFE_FALLBACK * this
    public const float FOOD_FLEE_MULT   = 6f;    // food attraction multiplier when fleeing
}
