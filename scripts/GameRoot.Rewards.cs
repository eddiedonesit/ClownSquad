namespace ClownSquad;

using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Combat;
using ClownSquad.Input;
using ClownSquad.Map;
using ClownSquad.Progression;
using ClownSquad.UI;
using ClownSquad.Visuals;
using ClownSquad.World;

/// <summary>
/// What a stage pays: XP, loot drops and bags, gold, banking on a win and losing on a loss.
/// </summary>
public partial class GameRoot
{
    // --------------------------------------------------------- progression

    /// <summary>
    /// Every player earns the full XP for every enemy, fallen players included:
    /// no kill stealing in co-op, and nobody is left behind for dying.
    /// </summary>
    private void OnEnemyDefeated(Fighter enemy)
    {
        _kos++;
        bool boss = enemy is IBoss { PrimaryBoss: true };
        var foe = enemy as PlayerFighter;
        int level = foe?.EnemyLevel ?? 1;
        EnemyRank rank = foe?.Rank ?? EnemyRank.Common;
        float worth = boss ? BossXp : GruntXp * (enemy is CastFighter cast ? cast.XpScale : 1f);
        int xp = (int)Mathf.Round(worth * Leveling.EnemyXpScale(level) * Leveling.RankXp(rank));
        CrowdCheer(boss ? 4f : rank != EnemyRank.Common ? 2f : 0.9f, boss ? 14 : 1 + (int)rank * 3);
        CameraShake.RequestPunch(this, boss ? 1.3f : 0.35f + 0.25f * (int)rank);
        PopText.Spawn(this, enemy.GlobalPosition + new Vector3(0f, enemy.BodyHeight + 0.2f, 0f),
                      $"+{xp} XP", new Color(1f, 0.85f, 0.3f), boss ? 1.3f : 0.8f);

        // Gear that heals on a knockout pays the one who landed it.
        if (enemy.LastAttacker is PlayerFighter killer && killer.Record is CharacterRecord kr && !killer.IsDead)
        {
            float heal = Leveling.LifeOnKill(kr);
            if (heal > 0f) killer.Heal(heal);
        }

        RollDrops(enemy, boss, rank, level);
        PayGold(enemy, boss, rank, level);

        foreach (PlayerFighter p in _players)
        {
            if (p.Record is not CharacterRecord r) continue;
            if (Leveling.Grant(r, (int)Mathf.Round(xp * Leveling.XpMultiplier(r))) == 0) continue;

            // The probe's AI spends its points the way a player would, or the
            // enemies, which scale with level, would outgrow it.
            if (_outcomeProbe == "win")
                while (r.StatPoints > 0) { r.StatPoints--; if (r.StatPoints % 2 == 0) r.Pow++; else r.Guts++; }

            // A level-up is a full heal: the classic reward for surviving.
            p.ApplyProgression(r);
            if (!p.IsDead) p.Heal(p.MaxHealth);
            PopText.Spawn(this, p.GlobalPosition + new Vector3(0f, p.BodyHeight + 0.35f, 0f),
                          "LEVEL UP!", new Color(0.5f, 0.9f, 1f), 1.2f, 1.3f);
            ClownSquad.Audio.Sfx.Play("level_up");
            GD.Print($"{r.Name} reached level {r.Level} ({r.StatPoints} stat point(s) to spend).");
        }
        SaveSystem.Save();
    }

    // ---------------------------------------------------------------- loot

    /// <summary>
    /// Drops on a knockout. Better enemies drop more often and better (GDD 6.3):
    /// a common grunt one time in five, ranked grunts nearly always, the boss
    /// twice, and the wild legendary twice with the rarity weights pushed hard.
    /// The host's LUCK and FORTUNE gear push them further.
    /// </summary>
    private void RollDrops(Fighter enemy, bool boss, EnemyRank rank, int level)
    {
        float boost = _players.Count > 0 && _players[0].Record is CharacterRecord host ? Leveling.LootBoost(host) : 0f;
        if (_director?.IsBonus == true)
        {
            // Pinatas: lots of gold, and now and then a prize inside.
            if (_lootRng.Randf() < 0.3f) SpawnDrop(Loot.Roll(_lootRng, level, 0.5f + boost), enemy.GlobalPosition);
            return;
        }
        (float chance, int count, float push) = boss ? (1f, 2, 1.5f) : rank switch
        {
            EnemyRank.Rare => (0.75f, 1, 0.6f),
            EnemyRank.Epic => (1f, 1, 1.2f),
            EnemyRank.Legendary => (1f, 2, 3f),
            _ => (0.2f, 1, 0f),
        };
        if (_lootRng.Randf() > chance) return;
        for (int i = 0; i < count; i++)
            SpawnDrop(Loot.Roll(_lootRng, level, push + boost), enemy.GlobalPosition);
    }

    private void SpawnDrop(ItemData item, Vector3 at)
    {
        var toss = new Vector3(_lootRng.RandfRange(-2.2f, 2.2f), _lootRng.RandfRange(5f, 6.5f), _lootRng.RandfRange(-1.2f, 1.2f));
        var drop = new LootDrop(item, toss);
        AddChild(drop);
        drop.GlobalPosition = at + new Vector3(0f, 0.6f, 0f);
        drop.PickedUp += OnPickedUp;
        _drops.Add(drop);
        _dropsSpawned++;
        if (item.Rarity >= ItemRarity.Rare) ClownSquad.Audio.Sfx.Play("ding", 1.3f + 0.1f * (int)item.Rarity);
        GD.Print($"Drop: {item.Rarity} {item.Name} (level {item.Level}).");
    }

    private void OnPickedUp(LootDrop drop, PlayerFighter p)
    {
        _drops.Remove(drop);
        ItemData item = drop.Item;
        // Swept items were already counted into a bag at the stage clear.
        if (!_swept.Contains(item.Id)) { Bag(p).Add(item); _itemsFound++; }
        PopText.Spawn(this, p.GlobalPosition + new Vector3(0f, p.BodyHeight + 0.25f, 0f), item.Name,
                      Loot.RarityColor(item.Rarity), 0.7f, 0.9f);
        ClownSquad.Audio.Sfx.Play("coin", 0.9f + 0.08f * (int)item.Rarity);
    }

    /// <summary>Gold for a knockout, to whoever landed it, into the stage bag like loot.</summary>
    private void PayGold(Fighter enemy, bool boss, EnemyRank rank, int level)
    {
        PlayerFighter? to = enemy.LastAttacker as PlayerFighter ?? (_players.Count > 0 ? _players[0] : null);
        if (to is null || to.Hostile) return;
        float amount = (3f + level * 1.2f) * Leveling.RankXp(rank) * (boss ? 10f : 1f) * _lootRng.RandfRange(0.8f, 1.2f);
        if (_director?.IsBonus == true) amount *= 3f;
        int gold = Mathf.Max(1, (int)Mathf.Round(amount));
        _goldEarned += gold;
        _bagGold[to] = (_bagGold.TryGetValue(to, out int had) ? had : 0) + gold;
        PopText.Spawn(this, enemy.GlobalPosition + new Vector3(0.4f, enemy.BodyHeight, 0f), $"+{gold} G",
                      new Color(1f, 0.9f, 0.4f), 0.7f, 0.8f);
        ClownSquad.Audio.Sfx.Play("coin", 1.3f, -8f);
    }

    public string BagText(PlayerFighter p)
    {
        int items = BagCount(p);
        int gold = _bagGold.TryGetValue(p, out int g) ? g : 0;
        if (items == 0 && gold == 0) return "";
        return items > 0 ? $"LOOT {items}  {gold}G" : $"{gold}G";
    }

    private List<ItemData> Bag(PlayerFighter p)
    {
        if (!_bags.TryGetValue(p, out List<ItemData>? bag)) _bags[p] = bag = new List<ItemData>();
        return bag;
    }

    public int BagCount(PlayerFighter p) => _bags.TryGetValue(p, out List<ItemData>? bag) ? bag.Count : 0;

    /// <summary>
    /// Stage cleared: anything still on the floor flies to the nearest player,
    /// then every bag is banked to its character. Empty slots are filled, and an
    /// item that scores better than what is worn is put on.
    /// </summary>
    private void BankLoot()
    {
        foreach (LootDrop drop in _drops.ToArray())
        {
            if (!IsInstanceValid(drop)) continue;
            PlayerFighter? nearest = null;
            float best = float.MaxValue;
            foreach (PlayerFighter p in _players)
            {
                float d = p.GlobalPosition.DistanceTo(drop.GlobalPosition);
                if (d < best) { best = d; nearest = p; }
            }
            if (nearest is null) continue;
            Bag(nearest).Add(drop.Item);
            _itemsFound++;
            _swept.Add(drop.Item.Id);
            drop.SendTo(nearest);
        }

        foreach (PlayerFighter p in _players)
        {
            if (p.Record is not CharacterRecord r) continue;
            if (_bagGold.TryGetValue(p, out int gold) && gold > 0)
            {
                r.Gold += gold;
                GD.Print($"{r.Name} banked {gold} gold ({r.Gold} total).");
            }
            _bagGold[p] = 0;
            List<ItemData> bag = Bag(p);
            if (bag.Count == 0) continue;
            foreach (ItemData item in bag)
            {
                r.Stash.Add(item);
                ItemData? worn = r.EquippedIn(item.Slot);
                if (worn is null || Loot.Score(item) > Loot.Score(worn)) r.Equip(item.Slot, item);
            }
            _itemsBanked += bag.Count;
            p.ApplyProgression(r);
            PopText.Spawn(this, p.GlobalPosition + new Vector3(0f, p.BodyHeight + 0.6f, 0f),
                          $"LOOT BANKED x{bag.Count}", new Color(1f, 0.85f, 0.3f), 1.1f, 1.2f);
            GD.Print($"{r.Name} banked {bag.Count} item(s); wearing {r.Equipped.Count}, stash {r.Stash.Count}.");
            bag.Clear();
        }
        SaveSystem.Save();
    }

    /// <summary>Stage failed: XP stays, the bag is lost (ADR 0006).</summary>
    private void LoseLoot()
    {
        foreach (PlayerFighter p in _players)
        {
            int lost = BagCount(p);
            Bag(p).Clear();
            _bagGold[p] = 0;
            if (lost == 0) continue;
            PopText.Spawn(this, p.GlobalPosition + new Vector3(0f, p.BodyHeight + 0.6f, 0f),
                          $"LOOT LOST x{lost}", new Color(0.7f, 0.7f, 0.75f), 1.0f, 1.0f);
            GD.Print($"{p.DisplayName} lost {lost} item(s) with the stage.");
        }
        foreach (LootDrop drop in _drops)
            if (IsInstanceValid(drop)) drop.QueueFree();
        _drops.Clear();
        SaveSystem.Save();
    }
}
