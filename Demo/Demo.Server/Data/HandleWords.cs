namespace Demo.Server.Data
{
    // Word lists pro human-readable handle generaci. EN dvouslovné
    // (color-animal), 30+30 = 900 unikátních kombinací. Pro veřejný demo
    // server s desitkami současných uživatelů dost; přes 900 retry append
    // suffix `-N`.
    public static class HandleWords
    {
        public static readonly string[] Colors = new[]
        {
            "red", "blue", "green", "yellow", "purple", "orange", "pink", "brown",
            "black", "white", "cyan", "magenta", "lime", "teal", "indigo", "violet",
            "gold", "silver", "navy", "olive", "maroon", "coral", "salmon", "mint",
            "peach", "ivory", "ebony", "azure", "beige", "crimson"
        };

        public static readonly string[] Animals = new[]
        {
            "bear", "fox", "owl", "wolf", "tiger", "lion", "hawk", "eagle",
            "raven", "otter", "seal", "crab", "crow", "deer", "lynx", "hare",
            "dove", "mole", "swan", "koala", "panda", "viper", "shark", "whale",
            "gecko", "kiwi", "hippo", "llama", "rhino", "lemur"
        };
    }
}
