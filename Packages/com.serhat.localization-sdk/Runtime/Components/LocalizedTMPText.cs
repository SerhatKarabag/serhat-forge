using TMPro;
using UnityEngine;

namespace Serhat.Localization.Components
{
    /// <summary>
    /// Component that automatically updates a TextMeshPro text component with localized content.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    [AddComponentMenu("Serhat/Localization/Localized TMP Text")]
    public class LocalizedTMPText : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The localization key.")]
        private string _key;

        [SerializeField]
        [Tooltip("Optional: Count value for pluralization.")]
        private int _pluralCount = -1;

        [SerializeField]
        [Tooltip("Use plural count for formatting.")]
        private bool _usePluralCount;

        [SerializeField]
        [Tooltip("Additional format arguments (as strings).")]
        private string[] _formatArgs;

        [SerializeField]
        [Tooltip("Update text on Start.")]
        private bool _updateOnStart = true;

        private TMP_Text _textComponent;

        /// <summary>
        /// The localization key.
        /// </summary>
        public string Key
        {
            get => _key;
            set
            {
                _key = value;
                UpdateText();
            }
        }

        /// <summary>
        /// The plural count value.
        /// </summary>
        public int PluralCount
        {
            get => _pluralCount;
            set
            {
                _pluralCount = value;
                UpdateText();
            }
        }

        /// <summary>
        /// Format arguments.
        /// </summary>
        public string[] FormatArgs
        {
            get => _formatArgs;
            set
            {
                _formatArgs = value;
                UpdateText();
            }
        }

        private void Awake()
        {
            _textComponent = GetComponent<TMP_Text>();
        }

        private void OnEnable()
        {
            Loc.OnLocaleChanged += OnLocaleChanged;

            if (_updateOnStart && Loc.IsInitialized)
            {
                UpdateText();
            }
        }

        private void OnDisable()
        {
            Loc.OnLocaleChanged -= OnLocaleChanged;
        }

        private void Start()
        {
            if (_updateOnStart)
            {
                UpdateText();
            }
        }

        private void OnLocaleChanged(object sender, LocaleChangedEventArgs e)
        {
            UpdateText();
        }

        /// <summary>
        /// Updates the text with the current localization.
        /// </summary>
        public void UpdateText()
        {
            if (_textComponent == null || string.IsNullOrEmpty(_key))
                return;

            if (!Loc.IsInitialized)
                return;

            string localizedText;

            if (_usePluralCount && _pluralCount >= 0)
            {
                // Plural with count as format arg
                object[] args = BuildFormatArgs();
                localizedText = Loc.Plural(_key, _pluralCount, args);
            }
            else if (_formatArgs != null && _formatArgs.Length > 0)
            {
                // Simple format
                localizedText = Loc.Format(_key, _formatArgs);
            }
            else
            {
                // Simple get
                localizedText = Loc.Get(_key);
            }

            _textComponent.text = localizedText;
        }

        private object[] BuildFormatArgs()
        {
            if (_formatArgs == null || _formatArgs.Length == 0)
            {
                return new object[] { _pluralCount };
            }

            var args = new object[_formatArgs.Length + 1];
            args[0] = _pluralCount;
            for (int i = 0; i < _formatArgs.Length; i++)
            {
                args[i + 1] = _formatArgs[i];
            }
            return args;
        }

        /// <summary>
        /// Sets the key and updates immediately.
        /// </summary>
        public void SetKey(string key)
        {
            _key = key;
            UpdateText();
        }

        /// <summary>
        /// Sets the key with format arguments.
        /// </summary>
        public void SetKeyWithArgs(string key, params string[] args)
        {
            _key = key;
            _formatArgs = args;
            UpdateText();
        }

        /// <summary>
        /// Sets the key with plural count.
        /// </summary>
        public void SetKeyWithPlural(string key, int count)
        {
            _key = key;
            _pluralCount = count;
            _usePluralCount = true;
            UpdateText();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying && _textComponent != null)
            {
                UpdateText();
            }
        }
#endif
    }
}
