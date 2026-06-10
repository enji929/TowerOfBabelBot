using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TowerOfBabelBot;

// Simple list populated by ThreatCache.Refresh using FindObjectsOfType once per
// RefreshInterval. The list persists between refreshes — no per-frame allocation.

public static class ProjectileRegistry
{
    public static readonly List<MonoBehaviour> All = new();

    public static void Clear() => All.Clear();

    public static void Sync()
    {
        All.Clear();
        Add<MonsterProjectile>();
        Add<IceBullet_Wizard>();
        Add<IceBall_Wizard>();
        Add<EliteAtk_FireBall>();
        Add<EliteAtk_LightningBall>();
        Add<DoomThunder_ThunderBall>();
        Add<DoomThunder_LightningProjectile>();
        Add<DoomThunder_LightningWave>();
        Add<Hellblossom_Fireball>();
        Add<Hellblossom_ZigzagFireball>();
        Add<FrostJudge_IceBallProjectile>();
        Add<FrostJudge_IceBeam>();
        Add<LordNightmares_Projectile>();
        Add<SwampPrincess_Projectile>();
        Add<FinalBoss_PoisonProjectile>();
        Add<FinalBoss_SkullBeam>();
    }

    private static void Add<T>() where T : MonoBehaviour
    {
        var arr = Object.FindObjectsOfType<T>();
        if (arr == null) return;
        for (int i = 0; i < arr.Length; i++)
            if (arr[i] != null) All.Add(arr[i]);
    }
}
