using System.Collections.Generic;
using UnityEngine;

namespace TowerOfBabelBot;

// Simple lists populated by BotController.RefreshLootCache every 2 s.
// Replaces the per-frame FindObjectsOfType arrays that were causing GC spikes.

public static class LootRegistry
{
    public static readonly List<DropItem_Exp>          Exp     = new();
    public static readonly List<DropItem_Gold>         Gold    = new();
    public static readonly List<DropItem_Jewel>        Jewel   = new();
    public static readonly List<DropItem_GemPowder>    Gem     = new();
    public static readonly List<DropItem_Magnet>       Magnet  = new();
    public static readonly List<DropItem_Rune>         Rune    = new();
    public static readonly List<DropItem_InsightToken> Token   = new();
    public static readonly List<DropItem_GreedEssense> Greed   = new();
    public static readonly List<DropItem_ArcaneBall>   Arcane  = new();
    public static readonly List<DropItem_Food>         Food    = new();
    public static readonly List<DropItem_SealingStone> Sealing = new();

    public static void Clear()
    {
        Exp.Clear(); Gold.Clear(); Jewel.Clear(); Gem.Clear();
        Magnet.Clear(); Rune.Clear(); Token.Clear(); Greed.Clear();
        Arcane.Clear(); Food.Clear(); Sealing.Clear();
    }

    // Remove destroyed items without a full FindObjectsOfType scan.
    public static void Compact()
    {
        RemoveNulls(Exp);  RemoveNulls(Gold);   RemoveNulls(Jewel); RemoveNulls(Gem);
        RemoveNulls(Magnet); RemoveNulls(Rune); RemoveNulls(Token); RemoveNulls(Greed);
        RemoveNulls(Arcane); RemoveNulls(Food); RemoveNulls(Sealing);
    }

    private static void RemoveNulls<T>(List<T> list) where T : Object
    {
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i] == null) list.RemoveAt(i);
    }
}
