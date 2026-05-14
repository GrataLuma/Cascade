using System.Collections.Generic;

namespace Demo.Client.Services
{
    // Map slovo→hex pro vizualizaci handle color části. Klíče se musí shodovat
    // s Demo.Server/Data/HandleWords.cs Colors[]. UI použije pro swatch
    // (kruhová tečka vedle handle name) — uživatel vidí, že "blue-bear" je
    // skutečně modrý, "red-fox" červená, atd.
    //
    // Volba odstínů: středně sytá, dostatečný kontrast pro 12-px swatch
    // proti světlému background. White / ivory mají border (viz CSS).
    public static class HandleColors
    {
        private static readonly Dictionary<string, string> _map = new()
        {
            ["red"]      = "#d62828",
            ["blue"]     = "#1d3557",
            ["green"]    = "#2a9d8f",
            ["yellow"]   = "#e9c46a",
            ["purple"]   = "#7209b7",
            ["orange"]   = "#f77f00",
            ["pink"]     = "#ff6b9d",
            ["brown"]    = "#6f4518",
            ["black"]    = "#1a1a1a",
            ["white"]    = "#f8f9fa",   // potřebuje border
            ["cyan"]     = "#00b4d8",
            ["magenta"]  = "#d62598",
            ["lime"]     = "#aacc00",
            ["teal"]     = "#006d77",
            ["indigo"]   = "#3a0ca3",
            ["violet"]   = "#7b2cbf",
            ["gold"]     = "#c9a227",
            ["silver"]   = "#adb5bd",
            ["navy"]     = "#14213d",
            ["olive"]    = "#606c38",
            ["maroon"]   = "#800020",
            ["coral"]    = "#ff7f50",
            ["salmon"]   = "#fa8072",
            ["mint"]     = "#98d8c8",
            ["peach"]    = "#ffb997",
            ["ivory"]    = "#fffff0",   // potřebuje border
            ["ebony"]    = "#555d50",
            ["azure"]    = "#4cc9f0",
            ["beige"]    = "#d4a373",
            ["crimson"]  = "#d00000"
        };

        // "blue-bear" → "#1d3557". Pokud handle nezačíná známou barvou
        // (suffix `-N` retry, nebo neznámá), vrátí neutrální šedou.
        public static string ForHandle(string handle)
        {
            if (string.IsNullOrEmpty(handle)) return "#adb5bd";
            var dash = handle.IndexOf('-');
            var color = dash > 0 ? handle.Substring(0, dash) : handle;
            return _map.TryGetValue(color, out var hex) ? hex : "#adb5bd";
        }

        // True pokud barva je tak světlá, že potřebuje viditelný border
        // (white / ivory) na malé tečce vedle handle name.
        public static bool NeedsBorder(string handle) => IsLight(handle);

        // True pokud barva je světlá → na background z této barvy musí být
        // tmavý text (jinak by byl nečitelný). Používá se pro panel headers,
        // badges, progress bary, kde je handle barva pozadím.
        //
        // Empiricky vybráno z 30 colors: light = ty, kde white text má
        // contrast ratio < 4.5:1 (WCAG AA threshold).
        public static bool IsLight(string handle)
        {
            if (string.IsNullOrEmpty(handle)) return false;
            var dash = handle.IndexOf('-');
            var color = dash > 0 ? handle.Substring(0, dash) : handle;
            return color is
                "white" or "ivory" or "mint" or "yellow" or "peach" or
                "lime" or "salmon" or "beige" or "gold" or "silver" or
                "coral" or "pink" or "azure";
        }

        // Text color (white / dark ink) k použití na backgroundu této barvy,
        // aby byl čitelný.
        public static string OnColor(string handle) =>
            IsLight(handle) ? "#1a1a1a" : "#ffffff";
    }
}
