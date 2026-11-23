using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HairLines.EditorTools
{
    public static class HairInspectorGUI
    {
        private const float XpbdComplianceMax = 0.0001f;

        private const float SectionTopGap = 10f;
        private const float HeaderHeight = 18f;
        private const float PadX = 8f;
        private const float PadTop = 6f;

        private const float HeaderIndent = 10f;

        private const float InlineButtonWidth = 64f;
        private const float InlineButtonGap = 4f;

        public static bool BeginSection(string key, string title, string summary = null,
                                        bool defaultExpanded = true)
        {
            EditorGUILayout.BeginVertical(Styles.Section);

            bool expanded = EditorPrefs.GetBool(key, defaultExpanded);

            Rect row = GUILayoutUtility.GetRect(GUIContent.none, Styles.Header,
                                                GUILayout.Height(HeaderHeight));
            EditorGUI.DrawRect(new Rect(row.x - PadX, row.y - PadTop,
                                        row.width + 2f * PadX, row.height + PadTop + 4f),
                               HeaderBackground);

            Rect foldout = row;
            foldout.xMin += HeaderIndent;
            bool now = EditorGUI.Foldout(foldout, expanded, title, true, Styles.Header);
            if (!string.IsNullOrEmpty(summary))
            {
                GUI.Label(row, summary, Styles.Summary);
            }

            if (now != expanded)
            {
                EditorPrefs.SetBool(key, now);
            }

            if (now)
            {
                GUILayout.Space(SectionTopGap);
            }

            return now;
        }

        public static void EndSection()
        {
            EditorGUILayout.EndVertical();
        }

        public static void SubHeader(string title)
        {
            GUILayout.Space(6f);
            Rect r = GUILayoutUtility.GetRect(new GUIContent(title), Styles.SubHeader);
            Rect label = r;
            label.xMin += HeaderIndent;
            GUI.Label(label, title, Styles.SubHeader);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax, r.width, 1f), Separator);
            GUILayout.Space(3f);
        }

        public static void DrawSeparator()
        {
            GUILayout.Space(6f);
            Rect r = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(r, Separator);
            GUILayout.Space(3f);
        }

        public static void Note(string text)
        {
            var content = new GUIContent(text);
            GUIStyle style = NoteStyle(Mathf.RoundToInt(EditorGUI.IndentedRect(Rect.zero).x));
            Rect rect = GUILayoutUtility.GetRect(content, style);
            GUI.Label(rect, content, style);
        }

        private static readonly Dictionary<int, GUIStyle> NoteStyles = new Dictionary<int, GUIStyle>();

        private static GUIStyle NoteStyle(int indent)
        {
            if (!NoteStyles.TryGetValue(indent, out GUIStyle style))
            {
                RectOffset margin = Styles.Note.margin;
                style = new GUIStyle(Styles.Note)
                {
                    margin = new RectOffset(margin.left + indent, margin.right, margin.top, margin.bottom),
                };
                NoteStyles.Add(indent, style);
            }

            return style;
        }

        public static void ComplianceSlider(SerializedProperty property, string label,
                                            string tooltip = null)
        {
            if (property == null)
            {
                return;
            }

            EditorGUI.BeginChangeCheck();
            float value = EditorGUILayout.Slider(new GUIContent(label, tooltip ?? property.tooltip),
                                                  property.floatValue, 0f, XpbdComplianceMax);
            if (EditorGUI.EndChangeCheck())
            {
                property.floatValue = value;
            }
        }

        public static void Stat(string label, string value)
        {
            EditorGUILayout.LabelField(label, value, Styles.Stat);
        }

        public static bool Button(string label, string tooltip = null, bool accent = false,
                                  float height = 22f, float width = 0f)
        {
            Color old = GUI.backgroundColor;
            if (accent)
            {
                GUI.backgroundColor = Accent;
            }

            var content = new GUIContent(label, tooltip);
            bool clicked = width > 0f
                ? GUILayout.Button(content, GUILayout.Height(height), GUILayout.Width(width))
                : GUILayout.Button(content, GUILayout.Height(height));

            GUI.backgroundColor = old;
            return clicked;
        }

        public static bool Button(Rect rect, string label, string tooltip = null, bool accent = false)
        {
            Color old = GUI.backgroundColor;
            if (accent)
            {
                GUI.backgroundColor = Accent;
            }

            bool clicked = GUI.Button(rect, new GUIContent(label, tooltip));

            GUI.backgroundColor = old;
            return clicked;
        }

        public static bool PropertyWithButton(SerializedProperty property, string button,
                                              float width = InlineButtonWidth, string tooltip = null,
                                              GUIContent label = null)
        {
            Rect row = EditorGUILayout.GetControlRect();
            var field = new Rect(row.x, row.y, Mathf.Max(0f, row.width - width - InlineButtonGap),
                                 row.height);
            var action = new Rect(row.xMax - width, row.y, width, row.height);

            if (label == null)
            {
                EditorGUI.PropertyField(field, property);
            }
            else
            {
                EditorGUI.PropertyField(field, property, label);
            }

            return GUI.Button(action, new GUIContent(button, tooltip), EditorStyles.miniButton);
        }

        public static void PropertyWithMaskButtons(SerializedProperty property, GUIContent label,
                                                    bool editEnabled, bool editActive,
                                                    out bool createClicked, out bool editClicked)
        {
            const float buttonWidth = 54f;
            const float gap = InlineButtonGap;
            Rect row = EditorGUILayout.GetControlRect();
            float fieldWidth = Mathf.Max(0f, row.width - buttonWidth * 2f - gap * 2f);
            Rect field = new Rect(row.x, row.y, fieldWidth, row.height);
            Rect create = new Rect(field.xMax + gap, row.y, buttonWidth, row.height);
            Rect edit = new Rect(create.xMax + gap, row.y, buttonWidth, row.height);

            EditorGUI.PropertyField(field, property, label);
            createClicked = GUI.Button(create, new GUIContent("Create", "Create a new mask texture."),
                                       EditorStyles.miniButtonLeft);
            using (new EditorGUI.DisabledScope(!editEnabled))
            {
                Color old = GUI.backgroundColor;
                if (editActive)
                {
                    GUI.backgroundColor = Accent;
                }
                editClicked = GUI.Button(edit, new GUIContent("Edit", "Edit this mask on the scalp mesh."),
                                         EditorStyles.miniButtonRight);
                GUI.backgroundColor = old;
            }
        }

        public static bool TryResolvePaintTarget(SerializedProperty property,
                                                 out LinePrimitiveRenderer hair, out HairGroup group)
        {
            hair = null;
            group = null;

            Object owner = property != null ? property.serializedObject.targetObject : null;
            if (owner is HairGroup ownerGroup)
            {
                group = ownerGroup;
                hair = ownerGroup.GetComponentInParent<LinePrimitiveRenderer>();
            }
            else if (owner is LinePrimitiveRenderer ownerRenderer)
            {
                hair = ownerRenderer;
            }

            return hair != null;
        }

        public static void EnumToolbar(SerializedProperty property, string label = null)
        {
            Rect rect = EditorGUILayout.GetControlRect();
            var content = new GUIContent(label ?? property.displayName, property.tooltip);

            EditorGUI.BeginProperty(rect, content, property);
            rect = EditorGUI.PrefixLabel(rect, content);

            int index = property.hasMultipleDifferentValues ? -1 : property.enumValueIndex;
            EditorGUI.BeginChangeCheck();
            int picked = GUI.Toolbar(rect, index, property.enumDisplayNames);
            if (EditorGUI.EndChangeCheck() && picked != index)
            {
                property.enumValueIndex = picked;
            }
            EditorGUI.EndProperty();
        }

        public static bool HelpBoxWithButton(string message, MessageType type, string button)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox(message, type);
            bool clicked = GUILayout.Button(button, GUILayout.Width(84f), GUILayout.ExpandHeight(true));
            EditorGUILayout.EndHorizontal();
            return clicked;
        }

        public static int HashProperties(params SerializedProperty[] properties)
        {
            unchecked
            {
                int hash = 17;
                foreach (SerializedProperty property in properties)
                {
                    if (property == null)
                    {
                        continue;
                    }
                    hash = hash * 31 + property.propertyPath.GetHashCode();
                    hash = hash * 31 + ValueHash(property);

                    SerializedProperty iterator = property.Copy();
                    SerializedProperty end = property.GetEndProperty();
                    bool enterChildren = true;
                    while (iterator.NextVisible(enterChildren) &&
                           !SerializedProperty.EqualContents(iterator, end))
                    {
                        enterChildren = true;
                        hash = hash * 31 + iterator.propertyPath.GetHashCode();
                        hash = hash * 31 + ValueHash(iterator);
                    }
                }
                return hash;
            }
        }

        public static string TreePrefix(Transform root, Transform node)
        {
            var chain = new List<Transform>();

            for (Transform current = node; current != null && current != root; current = current.parent)
            {
                chain.Add(current);
            }

            chain.Reverse();
            var prefix = new StringBuilder();

            for (int level = 1; level < chain.Count; level++)
            {
                bool last = IsLastGroupBranch(chain[level]);

                if (level == chain.Count - 1)
                {
                    prefix.Append(last ? "└ " : "├ ");
                }
                else
                {
                    prefix.Append(last ? "  " : "│ ");
                }
            }

            return prefix.ToString();
        }

        private static bool IsLastGroupBranch(Transform node)
        {
            Transform parent = node.parent;

            if (parent == null)
            {
                return true;
            }

            for (int child = parent.childCount - 1; child >= 0; child--)
            {
                Transform sibling = parent.GetChild(child);

                if (sibling.GetComponentInChildren<HairGroup>(false) == null)
                {
                    continue;
                }

                return sibling == node;
            }

            return true;
        }

        public static string Count(long value)
        {
            return value.ToString("N0");
        }

        public static string Memory(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }
            if (bytes < 1024)
            {
                return bytes + " B";
            }
            if (bytes < 1024 * 1024)
            {
                return (bytes / 1024.0).ToString("0.#") + " KB";
            }

            return (bytes / (1024.0 * 1024.0)).ToString("0.##") + " MB";
        }

        private static Color HeaderBackground => EditorGUIUtility.isProSkin
            ? new Color(1f, 1f, 1f, 0.055f)
            : new Color(0f, 0f, 0f, 0.055f);

        private static Color Separator => EditorGUIUtility.isProSkin
            ? new Color(1f, 1f, 1f, 0.12f)
            : new Color(0f, 0f, 0f, 0.12f);

        internal static Color Accent => EditorGUIUtility.isProSkin
            ? new Color(0.45f, 0.75f, 1f)
            : new Color(0.62f, 0.82f, 1f);

        private static int ValueHash(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return property.intValue;
                case SerializedPropertyType.Boolean:
                    return property.boolValue ? 1 : 0;
                case SerializedPropertyType.Float:
                    return property.floatValue.GetHashCode();
                case SerializedPropertyType.String:
                    return property.stringValue == null ? 0 : property.stringValue.GetHashCode();
                case SerializedPropertyType.Color:
                    return property.colorValue.GetHashCode();
                case SerializedPropertyType.Enum:
                    return property.enumValueIndex;
                case SerializedPropertyType.Vector2:
                    return property.vector2Value.GetHashCode();
                case SerializedPropertyType.Vector3:
                    return property.vector3Value.GetHashCode();
                case SerializedPropertyType.Vector4:
                    return property.vector4Value.GetHashCode();
                case SerializedPropertyType.Quaternion:
                    return property.quaternionValue.GetHashCode();
                case SerializedPropertyType.Bounds:
                    return property.boundsValue.GetHashCode();
                case SerializedPropertyType.ObjectReference:
                    return property.objectReferenceInstanceIDValue;
                case SerializedPropertyType.ArraySize:
                    return property.intValue;
                default:
                    return 0;
            }
        }

        private static class Styles
        {
            public static GUIStyle Section { get; } = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset((int)PadX, (int)PadX, (int)PadTop, 8),
                margin = new RectOffset(0, 0, 3, 3),
            };

            public static GUIStyle Header { get; } = new GUIStyle(EditorStyles.foldout)
            {
                fontStyle = FontStyle.Bold,
            };

            public static GUIStyle Summary { get; } = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
            };

            public static GUIStyle SubHeader { get; } = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                margin = new RectOffset(0, 0, 0, 0),
            };

            public static GUIStyle Note { get; } = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Italic,
                wordWrap = true,
            };

            public static GUIStyle Stat { get; } = new GUIStyle(EditorStyles.label);
        }
    }
}
