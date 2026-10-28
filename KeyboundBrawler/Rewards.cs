namespace KeyboundBrawler;

// Tab is the Currency Exchange tab the item sits under: 0 Weapons, 1 Armor, 2 Accessories, 3 Others.
public sealed record Reward(string Name, int Cost, int Tab);

public static class Rewards
{
    // MGC costs from the Lodestone event page (2026-10-28).
    public static readonly Reward[] All =
    [
        new("Avalanche Brawler's Top", 430, 1),
        new("Avalanche Brawler's Halfgloves", 260, 1),
        new("Avalanche Brawler's Bottoms", 430, 1),
        new("Avalanche Brawler's Boots", 260, 1),
        new("Avalanche Brawler's Earring", 190, 2),
        new("Avalanche Leader's Sunglasses", 190, 1),
        new("Avalanche Leader's Vest", 430, 1),
        new("Avalanche Leader's Halfgloves", 260, 1),
        new("Avalanche Leader's Cargo Trousers", 430, 1),
        new("Avalanche Leader's Boots", 260, 1),
        new("Avalanche Leader's Earring", 190, 2),
        new("Avalanche Gatling Gun", 430, 3),
        new("Magitek Scorpion Identification Key", 430, 3),
        new("Wind-up Barret", 260, 3),
        new("Welcome to the Gold Saucer Orchestrion Roll", 190, 3),
        new("Let the Battles Begin! Orchestrion Roll", 190, 3),
        new("Fight On! Orchestrion Roll", 190, 3),
        new("Keybound Brawler Framer's Kit", 190, 3),
        new("Ballroom Etiquette - Intrepid Inputs", 260, 3),
        new("Keybound Coffer", 910, 3),
    ];
}
