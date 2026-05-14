using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace Demo.Client.Services
{
    // Bilingvní switch (CZ / EN). Per-tab state (scoped). Persistence
    // přes localStorage skrze JS helper `window.langStore`. UI komponenty
    // injektují a čtou Lang / IsCs property; podsekce se renderují
    // ternárně podle aktuální hodnoty.
    public sealed class LanguageService
    {
        private const string DefaultLang = "en";
        private readonly IJSRuntime _js;
        private bool _initialized;

        public string Lang { get; private set; } = DefaultLang;
        public bool IsCs => Lang == "cs";
        public bool IsEn => Lang == "en";

        public event Action OnChanged;

        public LanguageService(IJSRuntime js)
        {
            _js = js;
        }

        public async Task InitializeAsync()
        {
            if (_initialized) return;
            try
            {
                var saved = await _js.InvokeAsync<string>("langStore.get");
                if (saved == "cs" || saved == "en")
                {
                    Lang = saved;
                }
            }
            catch
            {
                // localStorage nedostupný (privacy mode) — fallback default.
            }
            _initialized = true;
            OnChanged?.Invoke();
        }

        public async Task SetAsync(string lang)
        {
            if (lang != "cs" && lang != "en") return;
            if (Lang == lang) return;
            Lang = lang;
            try { await _js.InvokeVoidAsync("langStore.set", lang); }
            catch { /* ignore */ }
            OnChanged?.Invoke();
        }

        public Task ToggleAsync() => SetAsync(IsCs ? "en" : "cs");
    }
}
