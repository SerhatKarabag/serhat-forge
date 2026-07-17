#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Serhat.Analytics.Core;

namespace Serhat.Analytics.Editor
{
    /// <summary>
    /// Editor window for debugging analytics events.
    /// </summary>
    public class AnalyticsDebugWindow : EditorWindow
    {
        private static AnalyticsDebugWindow? _instance;

        private readonly List<EventLogEntry> _eventLog = new();
        private Vector2 _scrollPosition;
        private bool _autoScroll = true;
        private string _filterText = "";
        private EventCategory _filterCategory = EventCategory.All;
        private int _maxLogEntries = 100;

        private GUIStyle? _eventStyle;
        private GUIStyle? _headerStyle;
        private GUIStyle? _paramStyle;

        [MenuItem("Window/Analytics/Debug Window")]
        public static void ShowWindow()
        {
            _instance = GetWindow<AnalyticsDebugWindow>("Analytics Debug");
            _instance.minSize = new Vector2(400, 300);
        }

        /// <summary>
        /// Logs an event to the debug window.
        /// </summary>
        public static void LogEvent(AnalyticsEvent evt)
        {
            if (_instance == null) return;

            _instance._eventLog.Add(new EventLogEntry
            {
                Timestamp = DateTime.Now,
                Event = evt
            });

            // Trim old entries
            while (_instance._eventLog.Count > _instance._maxLogEntries)
            {
                _instance._eventLog.RemoveAt(0);
            }

            _instance.Repaint();
        }

        private void OnEnable()
        {
            _instance = this;
        }

        private void OnDisable()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void InitStyles()
        {
            if (_eventStyle != null) return;

            _eventStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(8, 8, 4, 4),
                margin = new RectOffset(4, 4, 2, 2)
            };

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11
            };

            _paramStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                richText = true
            };
        }

        private void OnGUI()
        {
            InitStyles();

            DrawToolbar();
            DrawEventList();
            DrawStatusBar();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Filter by text
            EditorGUILayout.LabelField("Filter:", GUILayout.Width(40));
            _filterText = EditorGUILayout.TextField(_filterText, EditorStyles.toolbarSearchField, GUILayout.Width(150));

            // Filter by category
            EditorGUILayout.LabelField("Category:", GUILayout.Width(55));
            _filterCategory = (EventCategory)EditorGUILayout.EnumPopup(_filterCategory, EditorStyles.toolbarPopup, GUILayout.Width(100));

            GUILayout.FlexibleSpace();

            // Auto-scroll toggle
            _autoScroll = GUILayout.Toggle(_autoScroll, "Auto-scroll", EditorStyles.toolbarButton, GUILayout.Width(80));

            // Clear button
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                _eventLog.Clear();
            }

            // Test event button
            if (GUILayout.Button("Test Event", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                SendTestEvent();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawEventList()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            foreach (var entry in _eventLog)
            {
                // Apply filters
                if (!PassesFilter(entry)) continue;

                DrawEventEntry(entry);
            }

            // Auto-scroll to bottom
            if (_autoScroll && _eventLog.Count > 0)
            {
                _scrollPosition.y = float.MaxValue;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawEventEntry(EventLogEntry entry)
        {
            var evt = entry.Event;

            EditorGUILayout.BeginVertical(_eventStyle);

            // Header: timestamp + category + event name
            EditorGUILayout.BeginHorizontal();

            var categoryColor = GetCategoryColor(evt.Category);
            var headerText = $"<color=#{ColorUtility.ToHtmlStringRGB(categoryColor)}>[{evt.Category}]</color> <b>{evt.EventName}</b>";

            var headerContent = new GUIContent(headerText);
            EditorGUILayout.LabelField(entry.Timestamp.ToString("HH:mm:ss.fff"), GUILayout.Width(85));

            var richStyle = new GUIStyle(EditorStyles.label) { richText = true };
            EditorGUILayout.LabelField(headerContent, richStyle);

            EditorGUILayout.EndHorizontal();

            // Parameters
            if (evt.Parameters.Count > 0)
            {
                EditorGUI.indentLevel++;

                foreach (var kvp in evt.Parameters)
                {
                    var paramText = $"<color=#888888>{kvp.Key}:</color> {FormatValue(kvp.Value)}";
                    EditorGUILayout.LabelField(new GUIContent(paramText), _paramStyle);
                }

                EditorGUI.indentLevel--;
            }

            // User/Session info
            if (!string.IsNullOrEmpty(evt.UserId) || !string.IsNullOrEmpty(evt.SessionId))
            {
                var infoText = "";
                if (!string.IsNullOrEmpty(evt.UserId))
                {
                    infoText += $"User: {evt.UserId}  ";
                }
                var sessionId = evt.SessionId ?? string.Empty;
                if (sessionId.Length > 0)
                {
                    infoText += $"Session: {sessionId.Substring(0, Math.Min(8, sessionId.Length))}...";
                }
                EditorGUILayout.LabelField(infoText, EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUILayout.LabelField($"Events: {_eventLog.Count} / {_maxLogEntries}", GUILayout.Width(120));

            GUILayout.FlexibleSpace();

            // Max entries setting
            EditorGUILayout.LabelField("Max entries:", GUILayout.Width(70));
            _maxLogEntries = EditorGUILayout.IntField(_maxLogEntries, GUILayout.Width(50));
            _maxLogEntries = Mathf.Clamp(_maxLogEntries, 10, 1000);

            EditorGUILayout.EndHorizontal();
        }

        private bool PassesFilter(EventLogEntry entry)
        {
            var evt = entry.Event;

            // Category filter
            if (_filterCategory != EventCategory.All)
            {
                var categoryName = _filterCategory.ToString().ToLower();
                if (!evt.Category.Equals(categoryName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // Text filter
            if (!string.IsNullOrEmpty(_filterText))
            {
                var searchText = _filterText.ToLower();
                if (!evt.EventName.ToLower().Contains(searchText) &&
                    !evt.Category.ToLower().Contains(searchText))
                {
                    // Check parameters
                    var foundInParams = false;
                    foreach (var kvp in evt.Parameters)
                    {
                        if (kvp.Key.ToLower().Contains(searchText) ||
                            kvp.Value?.ToString()?.ToLower().Contains(searchText) == true)
                        {
                            foundInParams = true;
                            break;
                        }
                    }
                    if (!foundInParams) return false;
                }
            }

            return true;
        }

        private Color GetCategoryColor(string category)
        {
            return category.ToLower() switch
            {
                "gameplay" => new Color(0.4f, 0.8f, 0.4f),      // Green
                "progression" => new Color(0.8f, 0.8f, 0.4f),  // Yellow
                "session" => new Color(0.4f, 0.6f, 0.8f),      // Blue
                "authentication" => new Color(0.8f, 0.4f, 0.8f), // Purple
                "purchase" => new Color(0.8f, 0.6f, 0.4f),     // Orange
                "technical" => new Color(0.6f, 0.6f, 0.6f),    // Gray
                _ => Color.white
            };
        }

        private string FormatValue(object? value)
        {
            if (value == null) return "<null>";

            return value switch
            {
                bool b => b ? "<color=#4CAF50>true</color>" : "<color=#F44336>false</color>",
                float f => f.ToString("F2"),
                double d => d.ToString("F2"),
                _ => value.ToString() ?? ""
            };
        }

        private void SendTestEvent()
        {
            var testEvent = new AnalyticsEvent("test_event")
                .WithCategory(Core.EventCategory.Custom)
                .WithParameter("test_param", "test_value")
                .WithParameter("number", 42)
                .WithParameter("float_value", 3.14f)
                .WithParameter("bool_value", true);

            testEvent.UserId = "test_user_123";
            testEvent.SessionId = Guid.NewGuid().ToString("N");

            LogEvent(testEvent);
        }

        private enum EventCategory
        {
            All,
            Gameplay,
            Progression,
            Session,
            Authentication,
            Purchase,
            Technical,
            Custom
        }

        private class EventLogEntry
        {
            public DateTime Timestamp { get; set; }
            public AnalyticsEvent Event { get; set; } = null!;
        }
    }
}
