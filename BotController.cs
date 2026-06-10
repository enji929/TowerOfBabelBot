// Movement logic ported from HelpFreedom/brotato-full-autobot (GPL-3.0).
// Source: mods-unpacked/BlackTriangle-FullAutoBot/bot/potential_field.gd

using System.Collections.Generic;
using Il2CppInterop.Runtime.Injection;
using System.Runtime.InteropServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TowerOfBabelBot;

public class BotController : MonoBehaviour
{
    public static bool BotEnabled = false;

    public const float FallbackSpeed = 3f;

    // Movement state
    private static Vector2 _prevMove = Vector2.zero;

    // Cached HP/MP for GUI + engage distance
    private static float _lastHp = 1f, _lastMaxHp = 1f;
    private static float _lastMp = 0f, _lastMaxMp = 1f;

    private static Vector2 _lastPlayerPos = Vector2.zero;

    // Cached IL2CPP singleton wrappers — each GM.ins/VM.ins call allocates a new
    // wrapper object with a finalizer; caching avoids ~350 allocations/sec.
    private static GM  _gm;
    private static VM  _vm;
    private static SVM _svm;

    // GUI styles
    private static GUIStyle _guiBoxStyle;
    private static GUIStyle _guiLabelStyle;
    private static Texture2D _guiBg;
    private static string _guiText = "";
    private static float  _guiTextBuiltAt = -1f;

    // Skill-select state (level-up window)
    private float _skillSelectTimer = 0f;
    private bool _levelUpWasOpen = false;
    private const float SkillSelectDelay = 0.8f;

    private static float _lastLootSync = -10f;
    private static MonoBehaviour _sweepTarget = null;

    // Boss detection
    private static bool  _bossActive    = false;
    private static float _lastBossCheck = -10f;

    // Stuck detection
    private static Vector2 _stuckCheckPos  = Vector2.zero;
    private static float   _stuckCheckTime = -10f;
    private static float   _stuckDuration  = 0f;
    private static Vector2 _stuckEscapeDir = Vector2.zero;

    // RMB skill
    [DllImport("user32.dll")] static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP   = 0x0010;
    private static float _rmbCooldown = 0f;
    private const float RmbRepeatInterval = 0.15f; // how often we re-check while holding
    private static bool _rmbHeld = false;

    public BotController(System.IntPtr ptr) : base(ptr) { }

    static BotController() => ClassInjector.RegisterTypeInIl2Cpp<BotController>();

    private static Animator _cachedAnim;
    private float _lateUpdateLog = 0f;

    private void LateUpdate()
    {
        if (!BotEnabled) return;
        _lateUpdateLog += Time.deltaTime;
        bool doLog = _lateUpdateLog >= 3f;
        if (doLog) _lateUpdateLog = 0f;
        TryDriveAnimation(doLog);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1))
        {
            BotEnabled = !BotEnabled;
            Plugin.Log.LogInfo($"Bot {(BotEnabled ? "ON" : "OFF")}");
            if (BotEnabled) ResetState();
            else ReleaseRmb();
        }

        if (_gm == null) try { _gm = GM.ins; } catch { }
        if (_vm == null) try { _vm = VM.ins; } catch { }

        TryGetHpMp();

        if (!BotEnabled) return;

        HandleSkillSelect();
        HandleRmbSkill();

        if (_gm != null) try { _gm.autoAttack = true; _gm.autoAim = true; } catch { _gm = null; }
    }

    // ── RMB skill ─────────────────────────────────────────────────────────────
    // Hold RMB only when mana is sufficient AND at least one enemy is within range.
    // Uses Win32 mouse_event — works even when the window captures the cursor.
    private const float RmbMpThreshold  = 0.35f;
    private const float RmbEngageRadius = BotConfig.DEFAULT_ENGAGE_DISTANCE * 2.5f;

    private void HandleRmbSkill()
    {
        if (_rmbCooldown > 0f) { _rmbCooldown -= Time.deltaTime; return; }

        float mpRatio = _lastMaxMp > 0f ? _lastMp / _lastMaxMp : 0f;
        bool enoughMp = mpRatio >= RmbMpThreshold;
        bool enemyNear = enoughMp && EnemyWithinRadius(_lastPlayerPos, RmbEngageRadius);

        bool wantHold = enoughMp && enemyNear;
        if (wantHold && !_rmbHeld)
        {
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
            _rmbHeld = true;
            _rmbCooldown = RmbRepeatInterval;
        }
        else if (!wantHold && _rmbHeld)
        {
            ReleaseRmb();
        }
    }

    private static bool EnemyWithinRadius(Vector2 pos, float radius)
    {
        float r2 = radius * radius;
        var monsters = ThreatCache.Monsters;
        for (int i = 0; i < monsters.Count; i++)
            if ((monsters[i].pos - pos).sqrMagnitude <= r2) return true;
        return false;
    }

    private static void ReleaseRmb()
    {
        if (!_rmbHeld) return;
        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
        _rmbHeld = false;
    }

    // ── Boss detection ─────────────────────────────────────────────────────────
    // Scans for active boss objects every BOSS_CHECK_INTERVAL. Short-circuits on
    // first match so FindObjectOfType cost is paid at most once per check.

    private static void RefreshBossState()
    {
        float now = Time.time;
        if (now - _lastBossCheck < BotConfig.BOSS_CHECK_INTERVAL) return;
        _lastBossCheck = now;
        _bossActive =
            Object.FindObjectOfType<Boss_DoomThunder>()    != null ||
            Object.FindObjectOfType<Boss_FinalBoss>()      != null ||
            Object.FindObjectOfType<Boss_FrostJudge>()     != null ||
            Object.FindObjectOfType<Boss_Hellblossom>()    != null ||
            Object.FindObjectOfType<Boss_LordNightmares>() != null ||
            Object.FindObjectOfType<Boss_SkullKing>()      != null ||
            Object.FindObjectOfType<Boss_SwampPrincess>()  != null;
    }

    // ── Stuck detection ────────────────────────────────────────────────────────
    // Returns a random escape direction if the bot hasn't moved enough for
    // STUCK_ESCAPE_AFTER seconds. Resets automatically once movement resumes.

    private static Vector2 GetStuckEscape(Vector2 pos)
    {
        float now = Time.time;
        if (now - _stuckCheckTime < BotConfig.STUCK_CHECK_INTERVAL)
            return _stuckDuration >= BotConfig.STUCK_ESCAPE_AFTER ? _stuckEscapeDir : Vector2.zero;

        float moved = (pos - _stuckCheckPos).magnitude;
        _stuckCheckPos  = pos;
        _stuckCheckTime = now;

        if (moved < BotConfig.STUCK_THRESHOLD)
        {
            _stuckDuration += BotConfig.STUCK_CHECK_INTERVAL;
            if (_stuckEscapeDir == Vector2.zero)
            {
                float angle = UnityEngine.Random.value * 2f * Mathf.PI;
                _stuckEscapeDir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
        }
        else
        {
            _stuckDuration  = 0f;
            _stuckEscapeDir = Vector2.zero;
        }

        return _stuckDuration >= BotConfig.STUCK_ESCAPE_AFTER ? _stuckEscapeDir : Vector2.zero;
    }

    // ── Animation ─────────────────────────────────────────────────────────────

    private static void TryDriveAnimation(bool verbose = false)
    {
        if (_cachedAnim == null)
        {
            try { var p = Object.FindObjectOfType<Player>(); if (p != null) _cachedAnim = p.anim; }
            catch { }
            if (_cachedAnim == null) return;
        }

        bool isMoving = _prevMove.sqrMagnitude > 0.01f;
        try { _cachedAnim.SetBool("Walk", isMoving); } catch { }
        try { _cachedAnim.SetBool("Run",  isMoving); } catch { }
        try { _cachedAnim.SetFloat("Speed_Move", isMoving ? 1f : 0f); } catch { }

        if (verbose)
        {
            try
            {
                var si = _cachedAnim.GetCurrentAnimatorStateInfo(0);
                Plugin.Log.LogInfo($"[Anim] isMoving={isMoving} isRun={si.IsName("Run")} isIdle={si.IsName("Idle")}");
            }
            catch { }
        }
    }

    private static void ResetState()
    {
        _prevMove = Vector2.zero;
        ThreatCache.Reset();
        LootRegistry.Clear();
        _lastLootSync = -10f;
        _lastLootCompact = -10f;
        NativeHook.InvalidatePlayerCache();
        _cachedAnim = null;
        _rmbHeld = false;
        _sweepTarget = null;
        _bossActive = false;
        _lastBossCheck = -10f;
        _stuckDuration = 0f;
        _stuckEscapeDir = Vector2.zero;
        _stuckCheckTime = -10f;
        _gm = null; _vm = null; _svm = null;
    }

    private void OnGUI()
    {
        if (_guiBoxStyle == null)
        {
            _guiBg = new Texture2D(1, 1);
            _guiBg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.65f));
            _guiBg.Apply();
            _guiBoxStyle = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.UpperLeft };
            _guiBoxStyle.normal.background = _guiBg;
            _guiBoxStyle.normal.textColor = Color.white;
            _guiBoxStyle.padding = new RectOffset(8, 8, 6, 6);
            _guiLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
            _guiLabelStyle.normal.textColor = Color.white;
        }

        float now = Time.unscaledTime;
        if (now - _guiTextBuiltAt >= 0.2f)
        {
            _guiTextBuiltAt = now;
            float hpRatio = _lastMaxHp > 0f ? _lastHp / _lastMaxHp : 1f;
            float mpRatio = _lastMaxMp > 0f ? _lastMp / _lastMaxMp : 0f;
            string stateColor = BotEnabled ? "#7fdc7f" : "#888";
            string modeStr = !BotEnabled ? "" :
                _hpPhase == HpPhase.Flee    ? "<color=#ff4444><b>FLEE</b></color>"       :
                _hpPhase == HpPhase.Caution ? "<color=#ffaa00><b>CAUTION</b></color>"    :
                _bossActive && InSweepMode  ? "<color=#ff88ff>BOSS·SWEEP</color>"        :
                _bossActive                 ? "<color=#ff66cc><b>BOSS FIGHT</b></color>" :
                InSweepMode                 ? "<color=#7fd7ff>SWEEP</color>"              :
                                              "<color=#ffcc55>COMBAT</color>";
            string rmbStr = _rmbHeld ? "<color=#aaffaa>HELD</color>" : "off";
            string obsStr = _observedGameSpeed > 0.1f ? $"{_observedGameSpeed:F2}" : "n/a";
            int lootTotal = LootRegistry.Exp.Count + LootRegistry.Gold.Count + LootRegistry.Jewel.Count
                          + LootRegistry.Gem.Count + LootRegistry.Magnet.Count + LootRegistry.Rune.Count
                          + LootRegistry.Token.Count + LootRegistry.Greed.Count + LootRegistry.Arcane.Count
                          + LootRegistry.Food.Count + LootRegistry.Sealing.Count;
            _guiText =
                $"<b>TowerOfBabelBot</b>\n" +
                $"Bot: <color={stateColor}><b>{(BotEnabled ? "ON" : "OFF")}</b></color>  (F1)  {modeStr}\n" +
                $"HP: {_lastHp:F0}/{_lastMaxHp:F0} ({hpRatio:P0})  MP: {_lastMp:F0}/{_lastMaxMp:F0} ({mpRatio:P0})\n" +
                $"Speed: {_lastEffectiveSpeed:F2} ({_lastSpeedSource})  RMB: {rmbStr}\n" +
                $"Threats: {ThreatCache.Monsters.Count}  Exp: {LootRegistry.Exp.Count}  Loot: {lootTotal}\n" +
                $"Observed: {obsStr}";
        }

        var rect = new Rect(8, 8, 280, 130);
        GUI.Box(rect, "", _guiBoxStyle);
        GUI.Label(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12), _guiText, _guiLabelStyle);
    }

    // ── Speed observation ──────────────────────────────────────────────────────

    internal static float _observedGameSpeed = 0f;
    internal static string _lastSpeedSource = "?";
    internal static float _lastEffectiveSpeed = 0f;

    public static void UpdateObservedSpeed(float speed)
    {
        _observedGameSpeed = _observedGameSpeed > 0f
            ? Mathf.Lerp(_observedGameSpeed, speed, 0.15f)
            : speed;
    }

    public static float GetEffectiveSpeed(Player player)
    {
        if (_observedGameSpeed > 0.1f) { _lastSpeedSource = "observed"; return _observedGameSpeed; }

        if (_svm == null) try { _svm = SVM.ins; } catch { }
        if (_vm  == null) try { _vm  = VM.ins;  } catch { }

        float speed = 0f;
        try { if (_svm != null && _svm.characterMoveSpeed > 0) speed = _svm.characterMoveSpeed; } catch { _svm = null; }
        if (speed <= 0f)
            try { if (_vm != null && _vm.characterMoveSpeed > 0) speed = _vm.characterMoveSpeed; } catch { _vm = null; }
        if (speed > 0f)
        {
            if (speed > 20f) speed *= BotConfig.WORLD_SCALE;
            _lastSpeedSource = "SVM/VM";
            return speed;
        }

        _lastSpeedSource = "fallback";
        return FallbackSpeed;
    }

    // ── Level-up skill selection ───────────────────────────────────────────────

    private void HandleSkillSelect()
    {
        if (_gm == null) return;
        var rm = _gm.runManager;  // cached once — 4 accesses → 1
        if (rm == null) return;
        var win = rm.windowLevelUp;
        bool isOpen = win != null && win.activeInHierarchy;
        if (isOpen && !_levelUpWasOpen) Plugin.Log.LogInfo("Bot: level-up window opened");
        if (isOpen)
        {
            _skillSelectTimer += Time.unscaledDeltaTime;
            if (_skillSelectTimer >= SkillSelectDelay)
            {
                int pick = ChooseBestSkill(rm.skillSelector);
                rm.ConfirmSelectSkill(pick);
                Plugin.Log.LogInfo($"Bot: picked skill index {pick}");
                _skillSelectTimer = 0f;
            }
        }
        else _skillSelectTimer = 0f;
        _levelUpWasOpen = isOpen;
    }

    // Skill priority tiers: 3 = high, 2 = medium, 1 = low.
    // IDs are 4-digit: 11XX = warrior, 12XX = fire mage, 13XX = ice mage,
    // 14XX = thunder mage, 15XX = light/priest, 16XX = dark/necro.
    // Variants XX01–XX04: 01/02 = active attack skills (high value for bot),
    // 03 = passive/buff (medium), 04 = support/regen (low-medium).
    // Fill in exact values after checking BepInEx log lines:
    //   "Bot: skill option [N] id=XXXX level=Y"
    private static readonly Dictionary<int, int> _skillPriority = new Dictionary<int, int>
    {
        // ── Warrior (11XX) ──
        { 1101, 3 }, { 1102, 3 },   // active attack skills — high priority
        { 1103, 2 }, { 1104, 2 },   // passive/support

        // ── Fire Mage (12XX) ──
        { 1201, 3 }, { 1202, 3 },
        { 1203, 2 }, { 1204, 1 },

        // ── Ice Mage (13XX) ──
        { 1301, 3 }, { 1302, 3 },
        { 1303, 2 }, { 1304, 1 },

        // ── Thunder Mage (14XX) ──
        { 1401, 3 }, { 1402, 3 },
        { 1403, 2 }, { 1404, 1 },

        // ── Light / Priest (15XX) ──
        { 1501, 2 }, { 1502, 2 },
        { 1503, 3 }, { 1504, 2 },   // 1503 likely healing — high for survival

        // ── Dark / Necro (16XX) ──
        { 1601, 3 }, { 1602, 3 },
        { 1603, 2 }, { 1604, 1 },

        // ── Special ──
        { 1251, 3 },
    };

    private static int ChooseBestSkill(SkillSelector ss)
    {
        if (ss == null || ss.currentPickSkillId == null) return 0;

        int bestIdx = 0, bestScore = int.MinValue;
        int optionCount = ss.currentPickSkillId.Count;

        // Count how many options are upgrades vs new — prefer upgrades when
        // multiple are available, but don't skip a tier-3 new skill.
        int upgradeCount = 0;
        for (int i = 0; i < optionCount; i++)
        {
            int id = ss.currentPickSkillId[i];
            if (id <= 0) continue;
            int lv = 0;
            try { lv = ss.GetSkillLevel(id); } catch { }
            if (lv > 0) upgradeCount++;
        }

        for (int i = 0; i < optionCount; i++)
        {
            int skillId = ss.currentPickSkillId[i];
            if (skillId <= 0) continue;
            int curLevel = 0;
            try { curLevel = ss.GetSkillLevel(skillId); } catch { }

            int tier = _skillPriority.TryGetValue(skillId, out var t) ? t : 1;

            // Upgrading an existing skill is strongly preferred: each existing
            // level adds 40 points on top of tier weight.
            int upgradeBonus = curLevel > 0 ? 60 + curLevel * 40 : 0;

            // New skill penalty when upgrades are available — only relevant when
            // there IS an upgrade option so we don't skip it for a new skill.
            int newPenalty = (curLevel == 0 && upgradeCount > 0) ? 20 : 0;

            int score = tier * 100 + upgradeBonus - newPenalty;

            Plugin.Log.LogInfo($"Bot: skill [{i}] id={skillId} lv={curLevel} tier={tier} score={score}");

            if (score > bestScore) { bestScore = score; bestIdx = i; }
        }

        Plugin.Log.LogInfo($"Bot: picked skill index {bestIdx}");
        return bestIdx;
    }

    // ── Loot cache ────────────────────────────────────────────────────────────
    // Full FindObjectsOfType scan every 8 s — expensive, avoid doing it often.
    // Between scans, a compact pass removes destroyed (null) entries so the
    // movement code always sees a clean list without allocating new arrays.
    private static float _lastLootCompact = -10f;
    private const float LootSyncInterval    = 1f;
    private const float LootCompactInterval = 0.5f;

    private static void RefreshLootCache()
    {
        float now = Time.time;

        if (now - _lastLootSync >= LootSyncInterval)
        {
            _lastLootSync = now;
            _lastLootCompact = now;
            LootRegistry.Exp.Clear();     foreach (var x in Object.FindObjectsOfType<DropItem_Exp>())          if (x != null) LootRegistry.Exp.Add(x);
            LootRegistry.Gold.Clear();    foreach (var x in Object.FindObjectsOfType<DropItem_Gold>())         if (x != null) LootRegistry.Gold.Add(x);
            LootRegistry.Jewel.Clear();   foreach (var x in Object.FindObjectsOfType<DropItem_Jewel>())        if (x != null) LootRegistry.Jewel.Add(x);
            LootRegistry.Gem.Clear();     foreach (var x in Object.FindObjectsOfType<DropItem_GemPowder>())    if (x != null) LootRegistry.Gem.Add(x);
            LootRegistry.Magnet.Clear();  foreach (var x in Object.FindObjectsOfType<DropItem_Magnet>())       if (x != null) LootRegistry.Magnet.Add(x);
            LootRegistry.Rune.Clear();    foreach (var x in Object.FindObjectsOfType<DropItem_Rune>())         if (x != null) LootRegistry.Rune.Add(x);
            LootRegistry.Token.Clear();   foreach (var x in Object.FindObjectsOfType<DropItem_InsightToken>()) if (x != null) LootRegistry.Token.Add(x);
            LootRegistry.Greed.Clear();   foreach (var x in Object.FindObjectsOfType<DropItem_GreedEssense>()) if (x != null) LootRegistry.Greed.Add(x);
            LootRegistry.Arcane.Clear();  foreach (var x in Object.FindObjectsOfType<DropItem_ArcaneBall>())   if (x != null) LootRegistry.Arcane.Add(x);
            LootRegistry.Food.Clear();    foreach (var x in Object.FindObjectsOfType<DropItem_Food>())         if (x != null) LootRegistry.Food.Add(x);
            LootRegistry.Sealing.Clear(); foreach (var x in Object.FindObjectsOfType<DropItem_SealingStone>()) if (x != null) LootRegistry.Sealing.Add(x);
            return;
        }

        if (now - _lastLootCompact >= LootCompactInterval)
        {
            _lastLootCompact = now;
            LootRegistry.Compact();
        }
    }

    // ── HP / MP reading ────────────────────────────────────────────────────────

    private static void TryGetHpMp()
    {
        try { if (_gm != null) { _lastHp = (float)(double)_gm.amountHp; _lastMp = (float)(double)_gm.amountMp; } } catch { _gm = null; }
        try { if (_vm != null) { _lastMaxHp = Mathf.Max(1f, (float)_vm.characterMaxHp); _lastMaxMp = Mathf.Max(1f, (float)_vm.characterMaxMp); } } catch { _vm = null; }
    }

    // ── Movement entry point ───────────────────────────────────────────────────

    private enum HpPhase { Normal, Caution, Flee }
    private static HpPhase _hpPhase = HpPhase.Normal;

    public static Vector2 CalcMoveVelocity(Player player)
    {
        if (!BotEnabled || !player.canMove || player.forceStop)
        {
            _prevMove = Vector2.zero;
            return Vector2.zero;
        }

        float speed = GetEffectiveSpeed(player);
        _lastEffectiveSpeed = speed;

        Vector2 pos = player.rb.position;
        _lastPlayerPos = pos;

        RefreshLootCache();
        ThreatCache.Refresh();
        RefreshBossState();

        var threats = ThreatCache.Monsters;
        var bullets = ThreatCache.Projectiles;

        float hpRatio = _lastMaxHp > 0f ? _lastHp / _lastMaxHp : 1f;
        _hpPhase = hpRatio < BotConfig.HP_FLEE    ? HpPhase.Flee
                 : hpRatio < BotConfig.HP_CAUTION ? HpPhase.Caution
                 : HpPhase.Normal;

        Vector2 dir;
        if (_hpPhase == HpPhase.Flee)
        {
            dir = CalcFleeDir(pos, threats, bullets, speed);
        }
        else
        {
            Vector2 desire = BuildDesire(pos, threats);
            if (bullets.Count > 0)
            {
                var esc = ProjectileEscape(pos, bullets, threats, desire, speed);
                if (esc.urgency > 0f && esc.dir != Vector2.zero)
                    desire = desire * (1f - esc.urgency) + esc.dir * esc.urgency;
            }
            dir = Normalize(desire);
        }

        // Stuck escape: if the bot hasn't moved for STUCK_ESCAPE_AFTER seconds,
        // blend in a random direction to break free from corners/walls.
        Vector2 stuckEsc = GetStuckEscape(pos);
        if (stuckEsc != Vector2.zero)
            dir = dir != Vector2.zero
                ? Normalize(dir * (1f - BotConfig.STUCK_ESCAPE_STRENGTH) + stuckEsc * BotConfig.STUCK_ESCAPE_STRENGTH)
                : stuckEsc;

        // When dir is zero (no target) we must reset _prevMove explicitly.
        // Normalize(Lerp(prev, zero, t)) = Normalize(prev*(1-t)) = prev — bot never stops otherwise.
        if (dir == Vector2.zero)
        {
            _prevMove = Vector2.zero;
            return Vector2.zero;
        }
        _prevMove = Normalize(Vector2.Lerp(_prevMove, dir, BotConfig.MOVE_SMOOTHING));
        return _prevMove * speed;
    }

    // ── Flee ───────────────────────────────────────────────────────────────────

    private static Vector2 CalcFleeDir(Vector2 pos, IReadOnlyList<ThreatSample> threats,
                                        IReadOnlyList<ThreatSample> bullets, float speed)
    {
        Vector2 away = Vector2.zero;
        for (int i = 0; i < threats.Count; i++)
        {
            Vector2 target = ClosestApproach(pos, threats[i]);
            Vector2 diff = pos - target;
            float d = Mathf.Max(diff.magnitude, 0.01f);
            away += diff / (d * d);
        }

        if (bullets.Count > 0 && away.sqrMagnitude > 1e-4f)
        {
            var esc = ProjectileEscape(pos, bullets, threats, Normalize(away), speed);
            if (esc.urgency > 0f)
                away = Normalize(away) * (1f - esc.urgency) + esc.dir * esc.urgency;
        }

        away += AttrForce(pos, LootRegistry.Food, BotConfig.CONSUMABLE_ATTRACTION * BotConfig.FOOD_FLEE_MULT, 1f);

        return Normalize(away);
    }

    // ── BuildDesire ────────────────────────────────────────────────────────────

    internal static bool InSweepMode = false;

    private static Vector2 BuildDesire(Vector2 pos, IReadOnlyList<ThreatSample> threats)
    {
        bool sweep = threats.Count == 0;
        if (sweep && !InSweepMode)
            _lastLootSync = -10f; // enemies just died — force immediate loot scan
        if (!sweep && InSweepMode)
            _sweepTarget = null;  // entering combat — reset sweep target
        InSweepMode = sweep;

        if (sweep)
        {
            return SweepDir(pos);
        }

        float engage = EngageDistance();
        Vector2 combatForce = EnemyEngagementForce(pos, threats, engage);
        if (combatForce.sqrMagnitude > 1e-4f)
        {
            Vector2 perp1 = new Vector2(-combatForce.y, combatForce.x);
            Vector2 perp2 = new Vector2( combatForce.y, -combatForce.x);
            Vector2 toExp = NearestClearExpDir(pos, threats);
            Vector2 perp  = toExp.sqrMagnitude > 0.01f &&
                            Vector2.Dot(perp2, toExp) > Vector2.Dot(perp1, toExp)
                            ? perp2 : perp1;
            combatForce += perp * BotConfig.CIRCLING_STRENGTH;
        }
        Vector2 lootForce = LootAttraction(pos, threats);
        float combatMag = combatForce.magnitude;
        if (combatMag > 1e-4f)
        {
            float maxLoot = combatMag * BotConfig.LOOT_COMBAT_CAP;
            float lootMag = lootForce.magnitude;
            if (lootMag > maxLoot) lootForce *= maxLoot / lootMag;
        }

        Vector2 desire = Normalize(combatForce + lootForce);
        Vector2 rearForce = RearThreatForce(pos, threats, desire);
        if (rearForce.sqrMagnitude > 1e-4f)
            desire = Normalize(desire + rearForce);
        return desire;
    }

    // Sweep direction: lock onto one item and follow it until collected, then pick next nearest.
    // Per-frame re-selection caused oscillation when equidistant items swapped each frame.
    private static Vector2 SweepDir(Vector2 pos)
    {
        if (_sweepTarget != null)
        {
            Vector2 diff = (Vector2)_sweepTarget.transform.position - pos;
            float d = diff.magnitude;
            return d > 0.01f ? diff / d : Vector2.zero;
        }

        float minDSq = float.MaxValue;
        MonoBehaviour found = null;
        TryFindSweepTarget(pos, LootRegistry.Exp,     ref minDSq, ref found);
        TryFindSweepTarget(pos, LootRegistry.Magnet,  ref minDSq, ref found);
        TryFindSweepTarget(pos, LootRegistry.Food,    ref minDSq, ref found);
        TryFindSweepTarget(pos, LootRegistry.Gold,    ref minDSq, ref found);
        TryFindSweepTarget(pos, LootRegistry.Jewel,   ref minDSq, ref found);
        TryFindSweepTarget(pos, LootRegistry.Gem,     ref minDSq, ref found);
        TryFindSweepTarget(pos, LootRegistry.Rune,    ref minDSq, ref found);
        TryFindSweepTarget(pos, LootRegistry.Token,   ref minDSq, ref found);
        TryFindSweepTarget(pos, LootRegistry.Greed,   ref minDSq, ref found);
        TryFindSweepTarget(pos, LootRegistry.Arcane,  ref minDSq, ref found);
        TryFindSweepTarget(pos, LootRegistry.Sealing, ref minDSq, ref found);
        _sweepTarget = found;
        if (found == null) return Vector2.zero;
        Vector2 toTarget = (Vector2)found.transform.position - pos;
        float dist = toTarget.magnitude;
        return dist > 0.01f ? toTarget / dist : Vector2.zero;
    }

    private static void TryFindSweepTarget<T>(Vector2 pos, List<T> items, ref float minDSq, ref MonoBehaviour best)
        where T : MonoBehaviour
    {
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item == null) continue;
            float dSq = ((Vector2)item.transform.position - pos).sqrMagnitude;
            if (dSq < minDSq) { minDSq = dSq; best = item; }
        }
    }

    // Updates (minD, best) with the nearest non-null item direction in list — used by LootAttraction.
    private static void UpdateNearest<T>(Vector2 pos, List<T> items, ref float minD, ref Vector2 best)
        where T : MonoBehaviour
    {
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item == null) continue;
            Vector2 diff = (Vector2)item.transform.position - pos;
            float d = diff.magnitude;
            if (d < minD) { minD = d; best = diff / d; }
        }
    }

    // ── EngageDistance ─────────────────────────────────────────────────────────

    private static float EngageDistance()
    {
        if (_hpPhase != HpPhase.Normal)
            return BotConfig.SAFE_FALLBACK_DISTANCE * BotConfig.FLEE_ENGAGE_MULT;

        float aggressive = Mathf.Max(BotConfig.DEFAULT_ENGAGE_DISTANCE * 0.85f, BotConfig.MIN_ENGAGE_DISTANCE);
        float safe = BotConfig.SAFE_FALLBACK_DISTANCE;
        float ratio = Mathf.Clamp01(_lastHp / Mathf.Max(1f, _lastMaxHp));
        float span = BotConfig.ENGAGE_HP_HIGH - BotConfig.ENGAGE_HP_LOW;
        float t = span > 0f ? (ratio - BotConfig.ENGAGE_HP_LOW) / span : 1f;
        t = Mathf.Clamp01(t);
        float dist = aggressive * t + safe * (1f - t);
        return _bossActive ? dist * BotConfig.BOSS_ENGAGE_MULT : dist;
    }

    // ── EnemyEngagementForce ───────────────────────────────────────────────────

    private static Vector2 EnemyEngagementForce(Vector2 pos, IReadOnlyList<ThreatSample> threats, float engage)
    {
        Vector2 force = Vector2.zero;
        float nearestD = float.MaxValue;
        Vector2 nearestTarget = Vector2.zero;
        for (int i = 0; i < threats.Count; i++)
        {
            Vector2 target = ClosestApproach(pos, threats[i]);
            Vector2 diff = pos - target;
            float d = Mathf.Max(diff.magnitude, 0.01f);
            Vector2 away = diff / d;
            if (d < engage)
                force += away * (engage - d) * BotConfig.ENGAGE_SPRING_K;
            if (d < BotConfig.CONTACT_DANGER)
                force += away * BotConfig.CONTACT_REPULSION / (d * d);
            if (d < nearestD) { nearestD = d; nearestTarget = target; }
        }
        if (nearestD < float.MaxValue && nearestD > engage * BotConfig.ENGAGE_PULL_THRESHOLD)
            force += (nearestTarget - pos) / nearestD * BotConfig.ENGAGE_PULL;
        return force;
    }

    private static Vector2 ClosestApproach(Vector2 pos, ThreatSample t)
    {
        if (t.vel.sqrMagnitude < 1e-4f) return t.pos;
        return t.pos + t.vel * BotConfig.ENEMY_LOOKAHEAD;
    }

    // ── LootAttraction (combat mode) ───────────────────────────────────────────
    // Loot safety floor 0.2 so bot never fully ignores nearby drops.

    private static Vector2 LootAttraction(Vector2 pos, IReadOnlyList<ThreatSample> threats)
    {
        if (_hpPhase == HpPhase.Flee)
            return AttrForce(pos, LootRegistry.Food, BotConfig.CONSUMABLE_ATTRACTION * BotConfig.FOOD_FLEE_MULT, 1f);

        float threatDist = NearestThreatDist(pos, threats);
        float safetyRaw  = Mathf.Min(1f, threatDist / BotConfig.SAFETY_DISTANCE);
        float safety     = Mathf.Max(0.2f, safetyRaw * safetyRaw); // floor 0.2 — always collect

        float expSafety = _hpPhase == HpPhase.Normal ? Mathf.Max(0.3f, safety) : safety;

        float hpRatio  = _lastMaxHp > 0f ? _lastHp / _lastMaxHp : 1f;
        float foodMult = hpRatio < 0.5f ? Mathf.Lerp(3f, 1f, hpRatio * 2f) : 1f;

        float E = BotConfig.EXP_ATTRACTION;
        float L = BotConfig.LOOT_ATTRACTION;
        float C = BotConfig.CONSUMABLE_ATTRACTION;

        Vector2 force = Vector2.zero;
        force += ExpAttractionSafe(pos, threats, E, expSafety);
        force += AttrForce(pos, LootRegistry.Gold,    L,                           safety);
        force += AttrForce(pos, LootRegistry.Jewel,   L,                           safety);
        force += AttrForce(pos, LootRegistry.Gem,     L,                           safety);
        force += AttrForce(pos, LootRegistry.Magnet,  L * BotConfig.MAGNET_WEIGHT, 1f);
        force += AttrForce(pos, LootRegistry.Rune,    L,                           safety);
        force += AttrForce(pos, LootRegistry.Token,   L,                           safety);
        force += AttrForce(pos, LootRegistry.Greed,   L,                           safety);
        float arcaneMult = _lastMaxMp > 0f && (_lastMp / _lastMaxMp) < 0.3f ? 3f : 1f;
        force += AttrForce(pos, LootRegistry.Arcane,  L * arcaneMult,              safety);
        force += AttrForce(pos, LootRegistry.Food,    C * foodMult,                safety);
        force += AttrForce(pos, LootRegistry.Sealing, L,                           safety);

        // Nearest-item bonus: constant-magnitude pull so symmetrical item layouts
        // don't produce a zero net force (oscillation fix for combat mode).
        float nearD = float.MaxValue;
        Vector2 nearDir = Vector2.zero;
        UpdateNearest(pos, LootRegistry.Exp,     ref nearD, ref nearDir);
        UpdateNearest(pos, LootRegistry.Magnet,  ref nearD, ref nearDir);
        UpdateNearest(pos, LootRegistry.Gold,    ref nearD, ref nearDir);
        UpdateNearest(pos, LootRegistry.Jewel,   ref nearD, ref nearDir);
        UpdateNearest(pos, LootRegistry.Gem,     ref nearD, ref nearDir);
        UpdateNearest(pos, LootRegistry.Rune,    ref nearD, ref nearDir);
        UpdateNearest(pos, LootRegistry.Token,   ref nearD, ref nearDir);
        UpdateNearest(pos, LootRegistry.Greed,   ref nearD, ref nearDir);
        UpdateNearest(pos, LootRegistry.Arcane,  ref nearD, ref nearDir);
        UpdateNearest(pos, LootRegistry.Food,    ref nearD, ref nearDir);
        UpdateNearest(pos, LootRegistry.Sealing, ref nearD, ref nearDir);
        if (nearDir != Vector2.zero)
            force += nearDir * (L * safety);

        return force;
    }

    private static Vector2 AttrForce<T>(Vector2 pos, List<T> items, float k, float safety)
        where T : MonoBehaviour
    {
        if (items == null || items.Count == 0) return Vector2.zero;
        Vector2 force = Vector2.zero;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item == null) continue;
            Vector2 diff = (Vector2)item.transform.position - pos;
            float d = Mathf.Max(diff.magnitude, 0.01f);
            force += (diff / d) * k * safety / d;
        }
        return force;
    }

    // ── Path-aware XP helpers ──────────────────────────────────────────────────

    private static float PathClearanceFactor(Vector2 from, Vector2 to, IReadOnlyList<ThreatSample> threats)
    {
        Vector2 seg = to - from;
        float len = seg.magnitude;
        if (len < 0.01f) return 1f;
        Vector2 dir = seg / len;
        float minClear = float.MaxValue;
        for (int i = 0; i < threats.Count; i++)
        {
            Vector2 rel  = threats[i].pos - from;
            float   proj = Mathf.Clamp(Vector2.Dot(rel, dir), 0f, len);
            float   dist = (from + dir * proj - threats[i].pos).magnitude;
            if (dist < minClear) minClear = dist;
        }
        if (minClear >= BotConfig.EXP_COLLECT_CLEARANCE) return 1f;
        float t = minClear / BotConfig.EXP_COLLECT_CLEARANCE;
        return t * t;
    }

    private static Vector2 NearestClearExpDir(Vector2 pos, IReadOnlyList<ThreatSample> threats)
    {
        var exp = LootRegistry.Exp;
        float   minD = float.MaxValue;
        Vector2 best = Vector2.zero;
        for (int i = 0; i < exp.Count; i++)
        {
            var item = exp[i];
            if (item == null) continue;
            Vector2 expPos = (Vector2)item.transform.position;
            if (PathClearanceFactor(pos, expPos, threats) < 0.5f) continue;
            float d = (expPos - pos).magnitude;
            if (d < minD) { minD = d; best = (expPos - pos) / d; }
        }
        return best;
    }

    private static Vector2 ExpAttractionSafe(Vector2 pos, IReadOnlyList<ThreatSample> threats, float k, float safety)
    {
        var exp = LootRegistry.Exp;
        if (exp.Count == 0) return Vector2.zero;
        Vector2 force = Vector2.zero;
        float   nearestClearD   = float.MaxValue;
        Vector2 nearestClearDir = Vector2.zero;

        for (int i = 0; i < exp.Count; i++)
        {
            var item = exp[i];
            if (item == null) continue;
            Vector2 expPos = (Vector2)item.transform.position;
            Vector2 diff   = expPos - pos;
            float   d      = Mathf.Max(diff.magnitude, 0.01f);
            if (threats.Count > 0 && d > BotConfig.EXP_MAX_COMBAT_RANGE) continue;
            float   factor = PathClearanceFactor(pos, expPos, threats) * safety;
            force += (diff / d) * k * factor / d;
            if (factor >= 0.5f && d < nearestClearD)
            {
                nearestClearD   = d;
                nearestClearDir = diff / d;
            }
        }

        if (nearestClearD < float.MaxValue)
            force += nearestClearDir * k * (0.6f / Mathf.Max(nearestClearD, 0.1f));

        return force;
    }

    // ── Rear-threat ────────────────────────────────────────────────────────────

    private static Vector2 RearThreatForce(Vector2 pos, IReadOnlyList<ThreatSample> threats, Vector2 moveDir)
    {
        if (threats.Count == 0 || moveDir.sqrMagnitude < 0.01f) return Vector2.zero;
        Vector2 force = Vector2.zero;
        for (int i = 0; i < threats.Count; i++)
        {
            Vector2 diff = threats[i].pos - pos;
            float d = diff.magnitude;
            if (d < 0.01f || d > BotConfig.REAR_THREAT_DIST) continue;
            Vector2 toEnemy = diff / d;
            float dot = Vector2.Dot(moveDir, toEnemy);
            if (dot < BotConfig.REAR_DOT_THRESHOLD) continue;
            Vector2 away = -toEnemy;
            Vector2 lateral = new Vector2(-moveDir.y, moveDir.x);
            if (Vector2.Dot(lateral, away) < 0f) lateral = -lateral;
            Vector2 push = Normalize(away * 0.6f + lateral * 0.4f);
            force += push * BotConfig.REAR_REPULSION_K * dot * (BotConfig.REAR_THREAT_DIST - d) / BotConfig.REAR_THREAT_DIST;
        }
        return force;
    }

    private static float NearestThreatDist(Vector2 pos, IReadOnlyList<ThreatSample> threats)
    {
        float min = float.MaxValue;
        for (int i = 0; i < threats.Count; i++)
        {
            float d = (threats[i].pos - pos).magnitude;
            if (d < min) min = d;
        }
        return min == float.MaxValue ? 1e6f : min;
    }

    // ── ProjectileEscape — zero-GC static buffers ─────────────────────────────

    private struct EscapeResult { public Vector2 dir; public float urgency; }

    private const int MaxThreatBullets = 48;  // was 32
    private const int EscapeT = 10;           // was 6 — BotConfig.ESCAPE_TIME_SAMPLES
    private const int EscapeN = 36;           // was 24 — BotConfig.ESCAPE_DIRECTIONS
    private static readonly ThreatSample[] _threatBuf  = new ThreatSample[MaxThreatBullets];
    private static readonly float[]        _timeBuf    = new float[EscapeT];
    private static readonly Vector2[]      _bulletFlat = new Vector2[EscapeT * MaxThreatBullets];
    private static int _threatBufCount = 0;

    private static EscapeResult ProjectileEscape(
        Vector2 pos,
        IReadOnlyList<ThreatSample> projectiles,
        IReadOnlyList<ThreatSample> threats,
        Vector2 desire,
        float playerSpeed)
    {
        float reach      = playerSpeed * BotConfig.ESCAPE_HORIZON;
        float projRadius = BotConfig.PROJ_THREAT_RADIUS * (_bossActive ? BotConfig.BOSS_PROJ_RADIUS_MULT : 1f);
        float margin     = projRadius + reach;
        float horizon = BotConfig.ESCAPE_HORIZON;

        _threatBufCount = 0;
        for (int i = 0; i < projectiles.Count && _threatBufCount < MaxThreatBullets; i++)
        {
            var p = projectiles[i];
            Vector2 rel = pos - p.pos;
            float speedSq = p.vel.sqrMagnitude;
            bool threat;
            if (speedSq < 1f)
            {
                threat = rel.magnitude < margin;
            }
            else
            {
                float t = Vector2.Dot(rel, p.vel) / speedSq;
                t = Mathf.Clamp(t, 0f, horizon);
                threat = (pos - (p.pos + p.vel * t)).magnitude < margin;
            }
            if (threat) _threatBuf[_threatBufCount++] = p;
        }
        if (_threatBufCount == 0) return new EscapeResult { dir = Vector2.zero, urgency = 0f };

        // Front-loaded time samples: denser near t=0, sparser near horizon.
        // Formula 1 - sqrt(1-frac) maps [0,1] → [0,1] with more points near 0.
        for (int i = 0; i < EscapeT; i++)
        {
            float frac = (float)i / Mathf.Max(EscapeT - 1, 1);
            _timeBuf[i] = horizon * (1f - Mathf.Sqrt(Mathf.Max(0f, 1f - frac)));
        }

        for (int ti = 0; ti < EscapeT; ti++)
            for (int bi = 0; bi < _threatBufCount; bi++)
                _bulletFlat[ti * MaxThreatBullets + bi] = _threatBuf[bi].pos + _threatBuf[bi].vel * _timeBuf[ti];

        // Score 36 directions: clearance with proportional alignment bonus.
        Vector2 bestDir = Vector2.zero;
        float bestScore = -1e18f;
        for (int k = 0; k < EscapeN; k++)
        {
            float ang = (2f * Mathf.PI * k) / EscapeN;
            Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            float clearance = DirClearance(pos, d, playerSpeed);
            float align     = desire.sqrMagnitude > 0f ? Mathf.Max(0f, Vector2.Dot(d, desire)) : 0f;
            float penalty   = EnemyPathPenalty(pos, d, playerSpeed, threats);
            // Proportional bonus: aligned dirs get up to ESCAPE_ALIGN_FACTOR% more score
            float score = clearance * (1f + BotConfig.ESCAPE_ALIGN_FACTOR * align) - penalty;
            if (score > bestScore) { bestScore = score; bestDir = d; }
        }

        // Urgency: time-to-impact based — how soon does the nearest bullet arrive?
        float tti = ComputeMinTTI(pos, projRadius, horizon);
        float urgency;
        if (tti >= horizon)
            urgency = 0f;
        else if (tti <= BotConfig.ESCAPE_PANIC_TTI)
            urgency = 1f;
        else
            urgency = 1f - (tti - BotConfig.ESCAPE_PANIC_TTI) /
                      Mathf.Max(horizon - BotConfig.ESCAPE_PANIC_TTI, 1e-4f);

        return new EscapeResult { dir = bestDir, urgency = urgency };
    }

    // Minimum time-to-impact: earliest moment any bullet reaches the player (stationary worst-case).
    private static float ComputeMinTTI(Vector2 pos, float threatRadius, float horizon)
    {
        float minTTI = float.MaxValue;
        for (int bi = 0; bi < _threatBufCount; bi++)
        {
            var p = _threatBuf[bi];
            float speedSq = p.vel.sqrMagnitude;
            if (speedSq < 1f)
            {
                if ((p.pos - pos).sqrMagnitude < threatRadius * threatRadius) return 0f;
                continue;
            }
            Vector2 relPos = pos - p.pos;
            float t = Vector2.Dot(relPos, p.vel) / speedSq;
            t = Mathf.Clamp(t, 0f, horizon);
            if ((pos - (p.pos + p.vel * t)).magnitude < threatRadius && t < minTTI)
                minTTI = t;
        }
        return minTTI == float.MaxValue ? horizon + 1f : minTTI;
    }

    private static float DirClearance(Vector2 pos, Vector2 d, float speed)
    {
        float minD = float.MaxValue;
        for (int ti = 0; ti < EscapeT; ti++)
        {
            Vector2 pt = pos + d * (speed * _timeBuf[ti]);
            int baseIdx = ti * MaxThreatBullets;
            for (int bi = 0; bi < _threatBufCount; bi++)
            {
                float dist = (pt - _bulletFlat[baseIdx + bi]).magnitude;
                if (dist < minD) minD = dist;
            }
        }
        return minD == float.MaxValue ? 1e6f : minD;
    }

    private static float EnemyPathPenalty(Vector2 pos, Vector2 d, float speed, IReadOnlyList<ThreatSample> enemies)
    {
        if (enemies.Count == 0) return 0f;
        float nearest = float.MaxValue;
        for (int ti = 0; ti < EscapeT; ti++)
        {
            Vector2 pt = pos + d * (speed * _timeBuf[ti]);
            for (int i = 0; i < enemies.Count; i++)
            {
                float dist = (pt - enemies[i].pos).magnitude;
                if (dist < nearest) nearest = dist;
            }
        }
        if (nearest >= BotConfig.ENEMY_AVOID_DIST) return 0f;
        return (BotConfig.ENEMY_AVOID_DIST - nearest) * BotConfig.ENEMY_AVOID_PENALTY;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static Vector2 Normalize(Vector2 v)
    {
        float m = v.magnitude;
        return m < 0.001f ? Vector2.zero : v / m;
    }
}
