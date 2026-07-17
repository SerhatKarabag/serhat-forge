using UnityEngine;
using UnityEngine.EventSystems;

namespace Serhat.Forge.Core
{
    /// <summary>
    /// Enforces single-pointer behavior for UGUI with New Input System.
    /// Attach to the same GameObject as EventSystem.
    ///
    /// How it works:
    /// - Tracks which pointer ID is currently "active" (first to interact)
    /// - Blocks all other pointer IDs from triggering UI events
    /// - Releases lock when the active pointer ends interaction
    /// </summary>
    [RequireComponent(typeof(EventSystem))]
    public class SinglePointerGuard : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private static SinglePointerGuard _instance;

        // The pointer ID that currently "owns" input (-1 = none)
        private int _activePointerId = -1;

        // Track if pointer is currently down
        private bool _isPointerActive;

        #region Properties

        /// <summary>
        /// Returns true if a pointer is currently active and interacting.
        /// </summary>
        public static bool IsPointerActive => _instance != null && _instance._isPointerActive;

        /// <summary>
        /// Returns the currently active pointer ID, or -1 if none.
        /// </summary>
        public static int ActivePointerId => _instance != null ? _instance._activePointerId : -1;

        #endregion

        #region Unity Lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnSubsystemRegistration()
        {
            _instance = null;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        #endregion

        #region Pointer Handlers (for this GameObject - not used directly)

        // These are here to satisfy interface but actual logic is in static methods
        public void OnPointerDown(PointerEventData eventData) { }
        public void OnPointerUp(PointerEventData eventData) { }

        #endregion

        #region Static API

        /// <summary>
        /// Call this when a pointer attempts to begin interaction.
        /// Returns true if this pointer is allowed to proceed, false if blocked.
        /// </summary>
        public static bool TryClaimPointer(int pointerId)
        {
            if (_instance == null)
                return true; // No guard, allow all

            // If no active pointer, claim it
            if (!_instance._isPointerActive)
            {
                _instance._activePointerId = pointerId;
                _instance._isPointerActive = true;
                return true;
            }

            // If this is the active pointer, allow
            if (_instance._activePointerId == pointerId)
                return true;

            // Different pointer while one is active - block
            return false;
        }

        /// <summary>
        /// Call this when a pointer ends interaction.
        /// </summary>
        public static void ReleasePointer(int pointerId)
        {
            if (_instance == null)
                return;

            // Only release if this is the active pointer
            if (_instance._activePointerId == pointerId)
            {
                _instance._activePointerId = -1;
                _instance._isPointerActive = false;
            }
        }

        /// <summary>
        /// Force release any active pointer. Use sparingly.
        /// </summary>
        public static void ForceRelease()
        {
            if (_instance == null)
                return;

            _instance._activePointerId = -1;
            _instance._isPointerActive = false;
        }

        /// <summary>
        /// Check if a specific pointer ID is allowed to interact.
        /// Does NOT claim the pointer.
        /// </summary>
        public static bool IsPointerAllowed(int pointerId)
        {
            if (_instance == null)
                return true;

            if (!_instance._isPointerActive)
                return true;

            return _instance._activePointerId == pointerId;
        }

        #endregion
    }
}
