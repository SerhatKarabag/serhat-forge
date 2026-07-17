#if DOTWEEN
using Serhat.Forge.Audio;
using Zenject;
using UnityEngine;

namespace Serhat.Forge.UI.Navigation
{
    /// <summary>
    /// Button that switches a SwipePageController to a target page index when clicked.
    /// </summary>
    public class SwipePageShortcutButton : MonoBehaviour
    {
        [SerializeField] private SwipePageController _pageController;
        [Min(0)]
        [SerializeField] private int _pageIndex;

        [Inject] private ISfxService _sfxService;

        /// <summary>
        /// Optional analytics hook fired when this button opens its target page.
        /// Args: (pageIndex).
        /// </summary>
        public static System.Action<int> OnPageOpened;


        public void OpenPage()
        {
            _sfxService?.PlayButtonClick();
            OnPageOpened?.Invoke(_pageIndex);

            if (_pageController == null)
            {
                Debug.LogWarning("[SwipePageShortcutButton] SwipePageController is not assigned.", this);
                return;
            }

            _pageController.GoToPage(_pageIndex);
        }
    }
}
#endif
