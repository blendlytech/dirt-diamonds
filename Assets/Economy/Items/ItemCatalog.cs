using System;
using System.Collections.Generic;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Economy.Items;

/// <summary>
/// One catalog entry (high_school_person_layer.md §5.1). Immutable content
/// loaded once per session from Assets/Data/Items/items.json — the modifiers
/// are the closed §5.2 status-stat vocabulary flattened to three ints so the
/// per-read aggregation below stays allocation-free.
/// </summary>
public sealed class ItemDefinition
{
    /// <summary>Stable key stored in Player_Items.item_id; never reused.</summary>
    public readonly string Id;

    /// <summary>Player-facing display name — catalog copy lives in content, not C#.</summary>
    public readonly string Name;

    public readonly ItemCategory Category;

    /// <summary>Purchase cost against Players.funds (spent via the atomic AdjustFunds, marketplace step).</summary>
    public readonly double Price;

    /// <summary>§5.2 passive status buffs — attractiveness / social_status / reputation ONLY (the closed vocabulary; anything else fails the parse).</summary>
    public readonly int ModAttractiveness;
    public readonly int ModSocialStatus;
    public readonly int ModReputation;

    /// <summary>§5.3: daily schedule hours refunded while owned — Transport only, highest owned wins. 0 elsewhere.</summary>
    public readonly double TransportHoursSaved;

    /// <summary>§3.2: wealth tier at/above which parents may auto-gift this on the weekly tick. -1 = never auto-bought.</summary>
    public readonly int AutobuyMinTier;

    public ItemDefinition(
        string id, string name, ItemCategory category, double price,
        int modAttractiveness, int modSocialStatus, int modReputation,
        double transportHoursSaved, int autobuyMinTier)
    {
        Id = id;
        Name = name;
        Category = category;
        Price = price;
        ModAttractiveness = modAttractiveness;
        ModSocialStatus = modSocialStatus;
        ModReputation = modReputation;
        TransportHoursSaved = transportHoursSaved;
        AutobuyMinTier = autobuyMinTier;
    }

    /// <summary>True when §3.2's parental auto-purchase may gift this at <paramref name="wealthTier"/>.</summary>
    public bool AutobuysAt(int wealthTier) => AutobuyMinTier >= 0 && wealthTier >= AutobuyMinTier;
}

/// <summary>
/// The loaded item catalog (§5) — content, not schema: Player_Items.item_id is
/// deliberately not an FK, so this class carries the validation the database
/// cannot. <see cref="ValidateOwnership"/> is the loud boot-time cross-check
/// GameManager runs over every Player_Items row (unknown id or category drift
/// fails the boot, never a render). Lookup is by id; <see cref="Entries"/>
/// preserves authoring order for the Marketplace listing.
/// </summary>
public sealed class ItemCatalog
{
    private readonly Dictionary<string, ItemDefinition> _byId;
    private readonly List<ItemDefinition> _entries;

    public ItemCatalog(List<ItemDefinition> entries)
    {
        _entries = entries;
        _byId = new Dictionary<string, ItemDefinition>(entries.Count, StringComparer.Ordinal);
        foreach (ItemDefinition entry in entries)
        {
            if (!_byId.TryAdd(entry.Id, entry))
            {
                throw new FormatException($"Item catalog: duplicate item id '{entry.Id}'.");
            }
        }
    }

    public int Count => _entries.Count;

    /// <summary>All entries in authoring order (the Marketplace's list source).</summary>
    public IReadOnlyList<ItemDefinition> Entries => _entries;

    public bool TryGet(string itemId, out ItemDefinition definition) =>
        _byId.TryGetValue(itemId, out definition!);

    /// <summary>Lookup that throws on a missing id — for callers that hold an already-validated ownership row.</summary>
    public ItemDefinition Require(string itemId) =>
        _byId.TryGetValue(itemId, out ItemDefinition? definition)
            ? definition
            : throw new InvalidOperationException($"Item catalog has no entry '{itemId}'.");

    /// <summary>
    /// The §5 load-time ownership audit: every Player_Items row must reference
    /// a live catalog id AND agree with it on category (the stored category
    /// column is a denormalized copy — drift means the catalog edit forgot the
    /// never-reuse-an-id rule). Throws with the offending row spelled out.
    /// </summary>
    public void ValidateOwnership(IReadOnlyList<PlayerItemRow> ownedRows)
    {
        for (int i = 0; i < ownedRows.Count; i++)
        {
            PlayerItemRow row = ownedRows[i];
            if (!_byId.TryGetValue(row.ItemId, out ItemDefinition? definition))
            {
                throw new InvalidOperationException(
                    $"Player_Items row (player '{row.PlayerId}', item '{row.ItemId}') references an id missing from items.json — catalog ids must never be removed or renamed once shipped.");
            }
            if (definition.Category != row.Category)
            {
                throw new InvalidOperationException(
                    $"Player_Items row (player '{row.PlayerId}', item '{row.ItemId}') stores category {row.Category} but the catalog says {definition.Category} — catalog categories must never change once shipped.");
            }
        }
    }
}

/// <summary>
/// The §5.2/§5.3 read-side math — computed at read, never persisted (the
/// EquipmentLedger "computed, not written" discipline: losing an item reverts
/// cleanly because nothing was ever written back). Pure statics over already
/// loaded rows; no database, no Godot.
/// </summary>
public static class ItemEffects
{
    /// <summary>§5.2 per-stat aggregate cap: Σ owned modifiers per status stat never exceeds this, however much is hoarded.</summary>
    public const int ItemBuffCap = 15;

    /// <summary>
    /// Sums the owned items' passive buffs per status stat, each sum capped at
    /// <see cref="ItemBuffCap"/> (the doc's one-sided cap — negatives pass
    /// through and the final [0,100] clamp in <see cref="EffectiveStat"/>
    /// absorbs them). Rows are assumed already ownership-validated, so a
    /// missing id here is loud.
    /// </summary>
    public static StatusBuffs ComputeBuffs(IReadOnlyList<PlayerItemRow> ownedRows, ItemCatalog catalog)
    {
        int attractiveness = 0, socialStatus = 0, reputation = 0;
        for (int i = 0; i < ownedRows.Count; i++)
        {
            ItemDefinition definition = catalog.Require(ownedRows[i].ItemId);
            attractiveness += definition.ModAttractiveness;
            socialStatus += definition.ModSocialStatus;
            reputation += definition.ModReputation;
        }
        return new StatusBuffs(
            Math.Min(attractiveness, ItemBuffCap),
            Math.Min(socialStatus, ItemBuffCap),
            Math.Min(reputation, ItemBuffCap));
    }

    /// <summary>§5.2: effective = clamp(stored base + capped buff, 0, 100). The stored column is never written back.</summary>
    public static int EffectiveStat(int baseStat, int cappedBuff) =>
        Math.Clamp(baseStat + cappedBuff, 0, 100);

    /// <summary>§5.3: the daily schedule refund — the single best owned Transport item wins (a car supersedes a bike), 0 when none owned.</summary>
    public static double BestTransportHoursSaved(IReadOnlyList<PlayerItemRow> ownedRows, ItemCatalog catalog)
    {
        double best = 0.0;
        for (int i = 0; i < ownedRows.Count; i++)
        {
            if (ownedRows[i].Category != ItemCategory.Transport)
            {
                continue;
            }
            double saved = catalog.Require(ownedRows[i].ItemId).TransportHoursSaved;
            if (saved > best)
            {
                best = saved;
            }
        }
        return best;
    }
}

/// <summary>The three §5.2 status-stat buff sums, already capped at <see cref="ItemEffects.ItemBuffCap"/>.</summary>
public readonly struct StatusBuffs
{
    public readonly int Attractiveness;
    public readonly int SocialStatus;
    public readonly int Reputation;

    public StatusBuffs(int attractiveness, int socialStatus, int reputation)
    {
        Attractiveness = attractiveness;
        SocialStatus = socialStatus;
        Reputation = reputation;
    }
}
