using System;

namespace Serhat.Localization
{
    /// <summary>
    /// Event arguments for locale change events.
    /// </summary>
    public class LocaleChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The previous locale before the change.
        /// </summary>
        public Locale PreviousLocale { get; }

        /// <summary>
        /// The new current locale.
        /// </summary>
        public Locale NewLocale { get; }

        public LocaleChangedEventArgs(Locale previousLocale, Locale newLocale)
        {
            PreviousLocale = previousLocale;
            NewLocale = newLocale;
        }
    }
}
