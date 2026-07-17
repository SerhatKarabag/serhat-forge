using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Serhat.Forge.UI.Components
{
    /// <summary>
    /// A ScrollRect that only handles vertical drags.
    /// Horizontal drags are passed through to the parent (e.g., SwipePageController).
    /// Solves the nested scroll conflict between vertical content scrolling
    /// and horizontal page swiping.
    /// </summary>
    public class VerticalOnlyScrollRect : ScrollRect
    {
        private bool _isSwiping;
        private bool _isDecided;
        private Vector2 _startPos;

        [Tooltip("Minimum drag distance (pixels) before deciding direction")]
        [SerializeField] private float _decisionDistance = 20f;

        public override void OnBeginDrag(PointerEventData eventData)
        {
            _startPos = eventData.position;
            _isDecided = false;
            _isSwiping = false;
        }

        public override void OnDrag(PointerEventData eventData)
        {
            if (!_isDecided)
            {
                Vector2 delta = eventData.position - _startPos;
                float absX = Mathf.Abs(delta.x);
                float absY = Mathf.Abs(delta.y);

                // Wait until finger moves enough to decide
                if (absX < _decisionDistance && absY < _decisionDistance) return;

                _isDecided = true;

                // Horizontal dominant → page swipe
                // Vertical dominant → this scroll rect
                _isSwiping = absX > absY;

                if (_isSwiping)
                {
                    ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData,
                        ExecuteEvents.beginDragHandler);
                }
                else
                {
                    base.OnBeginDrag(eventData);
                }
            }

            if (_isSwiping)
            {
                ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData,
                    ExecuteEvents.dragHandler);
            }
            else
            {
                base.OnDrag(eventData);
            }
        }

        public override void OnEndDrag(PointerEventData eventData)
        {
            if (_isSwiping)
            {
                ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData,
                    ExecuteEvents.endDragHandler);
            }
            else
            {
                base.OnEndDrag(eventData);
            }

            _isSwiping = false;
            _isDecided = false;
        }
    }
}
