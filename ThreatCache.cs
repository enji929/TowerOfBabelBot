using System;
using System.Collections.Generic;
using UnityEngine;

namespace TowerOfBabelBot;

public struct ThreatSample
{
    public Vector2 pos;
    public Vector2 vel;
    public float lastSeen;
}

public static class ThreatCache
{
    private const float RefreshInterval  = 0.4f;
    private const float ProjSyncInterval = 3.0f; // full FindObjectsOfType scan for projectiles

    private static float _lastRefresh  = -10f;
    private static float _lastProjSync = -10f;
    private static readonly Dictionary<IntPtr, Vector2> _monsterPrev = new();
    private static readonly Dictionary<IntPtr, Vector2> _projPrev    = new();
    private static readonly List<ThreatSample> _monsters    = new();
    private static readonly List<ThreatSample> _projectiles = new();
    private static readonly HashSet<IntPtr>    _seenM  = new();
    private static readonly HashSet<IntPtr>    _seenP  = new();
    private static readonly List<IntPtr>       _stale  = new();
    private static GM _gm;

    public static IReadOnlyList<ThreatSample> Monsters    => _monsters;
    public static IReadOnlyList<ThreatSample> Projectiles => _projectiles;

    public static void Refresh()
    {
        float now = Time.time;
        if (now - _lastRefresh < RefreshInterval) return;
        float dt = Mathf.Max(now - _lastRefresh, 0.001f);
        _lastRefresh = now;

        // ── Monsters ──────────────────────────────────────────────────────────
        _monsters.Clear();
        if (_gm == null) try { _gm = GM.ins; } catch { }
        var db = _gm != null ? _gm.monsterDB : null;  // one property access, not two
        if (db != null)
        {
            _seenM.Clear();
            foreach (var kvp in db)
            {
                var m = kvp.Value;
                if (m == null || !m.isActiveAndEnabled) continue;
                IntPtr ptr = m.Pointer;
                _seenM.Add(ptr);
                Vector2 cur = m.transform.position;
                Vector2 vel = Vector2.zero;
                if (_monsterPrev.TryGetValue(ptr, out var prev))
                    vel = (cur - prev) / dt;
                _monsterPrev[ptr] = cur;
                _monsters.Add(new ThreatSample { pos = cur, vel = vel, lastSeen = now });
            }
            if (_monsterPrev.Count > _seenM.Count * 2)
            {
                _stale.Clear();
                foreach (var k in _monsterPrev.Keys)
                    if (!_seenM.Contains(k)) _stale.Add(k);
                foreach (var k in _stale) _monsterPrev.Remove(k);
            }
        }

        // ── Projectiles ────────────────────────────────────────────────────────
        // Full FindObjectsOfType scan only every ProjSyncInterval to avoid GC pressure.
        // Position/velocity still updated every RefreshInterval from the cached list.
        if (now - _lastProjSync >= ProjSyncInterval)
        {
            _lastProjSync = now;
            ProjectileRegistry.Sync();
        }
        _projectiles.Clear();
        var allProj = ProjectileRegistry.All;
        _seenP.Clear();
        for (int i = 0; i < allProj.Count; i++)
        {
            var p = allProj[i];
            if (p == null) continue;
            IntPtr ptr = p.Pointer;
            if (!_seenP.Add(ptr)) continue;
            Vector2 cur = p.transform.position;
            Vector2 vel = Vector2.zero;
            if (_projPrev.TryGetValue(ptr, out var prev))
                vel = (cur - prev) / dt;
            _projPrev[ptr] = cur;
            _projectiles.Add(new ThreatSample { pos = cur, vel = vel, lastSeen = now });
        }
        if (_projPrev.Count > _seenP.Count * 2)
        {
            _stale.Clear();
            foreach (var k in _projPrev.Keys)
                if (!_seenP.Contains(k)) _stale.Add(k);
            foreach (var k in _stale) _projPrev.Remove(k);
        }
    }

    public static void Reset()
    {
        _monsterPrev.Clear();
        _projPrev.Clear();
        _monsters.Clear();
        _projectiles.Clear();
        ProjectileRegistry.Clear();
        _lastRefresh  = -10f;
        _lastProjSync = -10f;
        _gm = null;
    }
}
