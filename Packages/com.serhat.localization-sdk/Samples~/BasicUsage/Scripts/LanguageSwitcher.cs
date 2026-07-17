using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Serhat.Localization.Samples
{
    /// <summary>
    /// Sample script that demonstrates language switching functionality.
    /// </summary>
    public class LanguageSwitcher : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Dropdown _languageDropdown;
        [SerializeField] private TMP_Text _welcomeText;
        [SerializeField] private TMP_Text _itemCountText;
        [SerializeField] private Slider _itemCountSlider;

        [Header("Settings")]
        [SerializeField] private string _playerName = "Player";

        private readonly Dictionary<string, string> _localeNames = new Dictionary<string, string>
        {
            { "en", "English" },
            { "tr", "Turkce" },
            { "ru", "Russkiy" }
        };

        private async void Start()
        {
            // Initialize localization if not already done
            if (!Loc.IsInitialized)
            {
                await Loc.InitializeAsync();
            }

            // Setup dropdown
            SetupDropdown();

            // Setup slider
            if (_itemCountSlider != null)
            {
                _itemCountSlider.onValueChanged.AddListener(OnItemCountChanged);
                _itemCountSlider.value = 1;
            }

            // Subscribe to locale changes
            Loc.OnLocaleChanged += OnLocaleChanged;

            // Initial update
            UpdateTexts();
        }

        private void OnDestroy()
        {
            Loc.OnLocaleChanged -= OnLocaleChanged;
        }

        private void SetupDropdown()
        {
            if (_languageDropdown == null)
                return;

            _languageDropdown.ClearOptions();

            var options = new List<TMP_Dropdown.OptionData>();
            var locales = Loc.GetSupportedLocales();
            int currentIndex = 0;

            for (int i = 0; i < locales.Count; i++)
            {
                var locale = locales[i];
                var displayName = _localeNames.TryGetValue(locale.Code, out var name)
                    ? name
                    : locale.Code.ToUpperInvariant();

                options.Add(new TMP_Dropdown.OptionData(displayName));

                if (locale == Loc.CurrentLocale)
                {
                    currentIndex = i;
                }
            }

            _languageDropdown.AddOptions(options);
            _languageDropdown.value = currentIndex;
            _languageDropdown.onValueChanged.AddListener(OnLanguageSelected);
        }

        private async void OnLanguageSelected(int index)
        {
            var locales = Loc.GetSupportedLocales();
            if (index >= 0 && index < locales.Count)
            {
                await Loc.SetLocaleAsync(locales[index].Code);
            }
        }

        private void OnLocaleChanged(object sender, LocaleChangedEventArgs e)
        {
            Debug.Log($"[Sample] Locale changed from {e.PreviousLocale} to {e.NewLocale}");
            UpdateTexts();
        }

        private void UpdateTexts()
        {
            // Update welcome text with formatting
            if (_welcomeText != null)
            {
                _welcomeText.text = Loc.Format("welcome.message", _playerName);
            }

            // Update item count with current slider value
            if (_itemCountSlider != null)
            {
                OnItemCountChanged(_itemCountSlider.value);
            }
        }

        private void OnItemCountChanged(float value)
        {
            int count = Mathf.RoundToInt(value);

            // Update pluralized text
            if (_itemCountText != null)
            {
                _itemCountText.text = Loc.Plural("items.count", count, count);
            }
        }
    }
}
