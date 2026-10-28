namespace KeyboundBrawler;

public sealed record Reward(string Name, int Cost);

public static class Rewards
{
    // MGC costs from the Lodestone event page (2026-10-28).
    public static readonly Reward[] All =
    [
        new("Avalanche Brawler's Top", 430),
        new("Avalanche Brawler's Halfgloves", 260),
        new("Avalanche Brawler's Bottoms", 430),
        new("Avalanche Brawler's Boots", 260),
        new("Avalanche Brawler's Earring", 190),
        new("Avalanche Leader's Sunglasses", 190),
        new("Avalanche Leader's Vest", 430),
        new("Avalanche Leader's Halfgloves", 260),
        new("Avalanche Leader's Cargo Trousers", 430),
        new("Avalanche Leader's Boots", 260),
        new("Avalanche Leader's Earring", 190),
        new("Avalanche Gatling Gun", 430),
        new("Magitek Scorpion Identification Key", 430),
        new("Wind-up Barret", 260),
        new("Welcome to the Gold Saucer Orchestrion Roll", 190),
        new("Let the Battles Begin! Orchestrion Roll", 190),
        new("Fight On! Orchestrion Roll", 190),
        new("Keybound Brawler Framer's Kit", 190),
        new("Ballroom Etiquette - Intrepid Inputs", 260),
        new("Keybound Coffer", 910),
    ];
}
