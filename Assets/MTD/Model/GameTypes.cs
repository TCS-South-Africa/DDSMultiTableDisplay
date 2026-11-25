namespace MTD
{
    // What MTD draws. Every roulette variant is Roulette; the variant only matters to the widget.
    public enum GameFamily
    {
        Unsupported,
        Roulette,
        SicBo,
        Blackjack, // Recognised so the door stays open; no snapshot or widget exists for it yet.
    }

    public enum RouletteVariant
    {
        Standard,
        Xtra,
        PowerReel,
    }

    public static class GameTypes
    {
        // Maps the wire game type (DDS's GameTypesEnum names) to what MTD shows.
        public static GameFamily GetFamily(string gameType, out RouletteVariant variant)
        {
            variant = RouletteVariant.Standard;

            switch ((gameType ?? "").Trim().ToUpperInvariant())
            {
                case "ROULETTE":
                case "ROULETTE_SPREAD":
                case "PARIMAX_ROULETTE":
                    return GameFamily.Roulette;
                case "ROULETTE_XTRA":
                    variant = RouletteVariant.Xtra;
                    return GameFamily.Roulette;
                case "ROULETTE_POWER":
                    variant = RouletteVariant.PowerReel;
                    return GameFamily.Roulette;
                case "SICBO":
                    return GameFamily.SicBo;
                case "BLACKJACK":
                    return GameFamily.Blackjack;
                default:
                    return GameFamily.Unsupported;
            }
        }

        public static GameFamily GetFamily(string gameType)
        {
            return GetFamily(gameType, out _);
        }
    }
}
