using System;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace HairLines.EditorTools
{
    public class HairOperatorStackGUI
    {
        public int RandomizeSeedForOperator { get; private set; } = -1;

        private readonly SerializedProperty _operators;
        private readonly SerializedProperty _guides;

        private static readonly GUIContent _maskLabel = new GUIContent("Mask");
        private static readonly GUIContent _seedLabel = new GUIContent("Seed");

        private int _removeIndex = -1;
        private int _moveUpIndex = -1;
        private int _moveDownIndex = -1;
        private Type _addType;

        private const float ButtonWidth = 22f;
        private const float RowGap = 4f;
        private const float SeedButtonWidth = 80f;
        private const float ChannelWidth = 64f;
        private const float HelpWidth = 24f;
        private const float ButtonGap = 1f;

        public HairOperatorStackGUI(SerializedProperty settings)
        {
            _operators = settings.FindPropertyRelative("_operators");
            SerializedProperty guideSet = settings.FindPropertyRelative("_guides");
            _guides = guideSet != null ? guideSet.FindPropertyRelative("_guides") : null;
        }

        public void Draw(bool allowEditing = false)
        {
            RandomizeSeedForOperator = -1;
            _removeIndex = -1;
            _moveUpIndex = -1;
            _moveDownIndex = -1;
            _addType = null;

            HairInspectorGUI.SubHeader("Operators");
            DrawClipboardRow(allowEditing);

            if (_operators.arraySize == 0)
            {
                HairInspectorGUI.Note("Empty stack - the strand keeps the shape the base growth gave it.");
            }

            for (int i = 0; i < _operators.arraySize; i++)
            {
                DrawOperator(i, allowEditing);
            }

            DrawAddRow();
            ApplyPendingEdits();
        }

        private void DrawClipboardRow(bool allowEditing)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_operators.arraySize == 0))
                {
                    if (GUILayout.Button(new GUIContent("Copy Stack",
                            "Put this whole operator stack on the clipboard."),
                            EditorStyles.miniButtonLeft))
                    {
                        HairOperatorClipboard.Copy(_operators);
                    }
                }

                using (new EditorGUI.DisabledScope(!allowEditing || !HairOperatorClipboard.HasStack))
                {
                    if (GUILayout.Button(new GUIContent(HairOperatorClipboard.PasteLabel,
                            "Replace this stack with the one on the clipboard."),
                            EditorStyles.miniButtonRight))
                    {
                        HairOperatorClipboard.Paste(_operators);
                    }
                }
            }

            GUILayout.Space(RowGap);
        }

        private void DrawOperator(int index, bool allowEditing)
        {
            SerializedProperty element = _operators.GetArrayElementAtIndex(index);
            var groomOperator = element.managedReferenceValue as HairGroomOperator;

            if (groomOperator == null)
            {
                EditorGUILayout.HelpBox("Empty stack slot.", MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawHeaderRow(index, groomOperator);

            EditorGUI.indentLevel++;
            DrawClumpRequirement(groomOperator);
            DrawParameters(element, index, allowEditing);
            DrawMask(element, allowEditing);
            EditorGUI.indentLevel--;

            EditorGUILayout.EndVertical();
        }

        private void DrawHeaderRow(int index, HairGroomOperator groomOperator)
        {
            Rect row = EditorGUILayout.GetControlRect(false, HelpWidth);
            float buttonHeight = EditorGUIUtility.singleLineHeight;
            float buttonY = row.y + (row.height - buttonHeight) * 0.5f;
            var remove = new Rect(row.xMax - ButtonWidth, buttonY, ButtonWidth, buttonHeight);
            var moveDown = new Rect(remove.x - ButtonWidth - ButtonGap, buttonY, ButtonWidth, buttonHeight);
            var moveUp = new Rect(moveDown.x - ButtonWidth - ButtonGap, buttonY, ButtonWidth, buttonHeight);
            var help = new Rect(Mathf.Round(moveUp.x - HelpWidth - ButtonGap),
                                Mathf.Round(row.y + (row.height - HelpWidth) * 0.5f),
                                HelpWidth, HelpWidth);
            var label = new Rect(row.x, row.y, help.x - row.x - RowGap, row.height);

            EditorGUI.LabelField(label, $"{index + 1}. {groomOperator.DisplayName}", EditorStyles.boldLabel);
            DrawHelp(help, groomOperator);

            bool canMoveUp = index > 0 && PriorityAt(index - 1) == PriorityAt(index);
            bool canMoveDown = index < _operators.arraySize - 1 &&
                               PriorityAt(index + 1) == PriorityAt(index);
            string upTip = canMoveUp
                ? "Move earlier in the stack"
                : index == 0
                    ? "Already first"
                    : $"{groomOperator.DisplayName} always runs after the operators above it";
            string downTip = canMoveDown
                ? "Move later in the stack"
                : index == _operators.arraySize - 1
                    ? "Already last"
                    : $"{groomOperator.DisplayName} always runs before the operators below it";

            using (new EditorGUI.DisabledScope(!canMoveUp))
            {
                if (GUI.Button(moveUp, new GUIContent("↑", upTip), EditorStyles.miniButton))
                {
                    _moveUpIndex = index;
                }
            }

            using (new EditorGUI.DisabledScope(!canMoveDown))
            {
                if (GUI.Button(moveDown, new GUIContent("↓", downTip), EditorStyles.miniButton))
                {
                    _moveDownIndex = index;
                }
            }

            if (GUI.Button(remove, new GUIContent("−", "Remove this operator"), EditorStyles.miniButton))
            {
                _removeIndex = index;
            }
        }

        private static void DrawHelp(Rect area, HairGroomOperator groomOperator)
        {
            if (string.IsNullOrEmpty(groomOperator.Description))
            {
                return;
            }

            GUI.Label(area, new GUIContent("?", groomOperator.Description), HelpStyle);
        }

        private static GUIStyle _helpStyle;

        private static GUIStyle HelpStyle
        {
            get
            {
                if (_helpStyle == null)
                {
                    _helpStyle = new GUIStyle(EditorStyles.label)
                    {
                        fontStyle = FontStyle.Bold,
                        fontSize = 15,
                        alignment = TextAnchor.MiddleCenter,
                        padding = new RectOffset(0, 0, 0, 0),
                    };
                    _helpStyle.normal.textColor = EditorStyles.centeredGreyMiniLabel.normal.textColor;
                }

                return _helpStyle;
            }
        }

        private void DrawClumpRequirement(HairGroomOperator groomOperator)
        {
            if (!(groomOperator is HairClumpOperator || groomOperator is HairBraidOperator) ||
                _guides == null || _guides.arraySize > 0)
            {
                return;
            }

            EditorGUILayout.HelpBox("Without guides the first clump spaces its own centres by the width " +
                                    "curve at x = 0. Generate guides to anchor one clump per guide.",
                                    MessageType.Info);
        }

        private void DrawParameters(SerializedProperty element, int index, bool allowEditing)
        {
            SerializedProperty iterator = element.Copy();
            SerializedProperty end = element.GetEndProperty();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;

                if (iterator.name == "_mask")
                {
                    continue;
                }

                if (iterator.name == "_seed")
                {
                    DrawSeed(iterator, index);
                    continue;
                }

                SerializedProperty channel = element.FindPropertyRelative(iterator.name + "Channel");

                if (channel == null)
                {
                    EditorGUILayout.PropertyField(iterator, true);
                    continue;
                }

                DrawWithChannel(iterator, channel);
            }
        }

        private static void DrawWithChannel(SerializedProperty property, SerializedProperty channel)
        {
            float height = EditorGUI.GetPropertyHeight(property, true);
            Rect row = EditorGUILayout.GetControlRect(true, height);
            var field = new Rect(row.x, row.y, Mathf.Max(0f, row.width - ChannelWidth - RowGap), height);
            var menu = new Rect(row.xMax - ChannelWidth, row.y, ChannelWidth, EditorGUIUtility.singleLineHeight);

            EditorGUI.PropertyField(field, property, true);

            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            EditorGUI.BeginProperty(menu, GUIContent.none, channel);
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUI.IntPopup(menu, channel.intValue, ChannelNames, ChannelValues);
            if (EditorGUI.EndChangeCheck())
            {
                channel.intValue = picked;
            }

            EditorGUI.EndProperty();
            EditorGUI.indentLevel = indent;
        }

        private static readonly GUIContent[] ChannelNames = BuildChannelNames();
        private static readonly int[] ChannelValues = BuildChannelValues();

        private static GUIContent[] BuildChannelNames()
        {
            var names = new GUIContent[HairStrandDataOperator.ChannelCount + 1];
            names[0] = new GUIContent("—", "This value is the same on every strand.");

            for (int i = 1; i < names.Length; i++)
            {
                names[i] = new GUIContent($"× Ch {i}",
                    $"Scaled per strand by the mask a Strand Data operator above wrote into channel {i}: " +
                    "white keeps this value, black takes it to zero.");
            }

            return names;
        }

        private static int[] BuildChannelValues()
        {
            var values = new int[HairStrandDataOperator.ChannelCount + 1];

            for (int i = 0; i < values.Length; i++)
            {
                values[i] = i;
            }

            return values;
        }

        private void DrawSeed(SerializedProperty seed, int index)
        {
            if (!HairInspectorGUI.PropertyWithButton(seed, "Randomize", SeedButtonWidth,
                                                     "Pick a new seed and rebuild the groom.", _seedLabel))
            {
                return;
            }

            Undo.RecordObject(seed.serializedObject.targetObject, "Randomize Operator Seed");
            seed.intValue = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            RandomizeSeedForOperator = index;
        }

        private void DrawMask(SerializedProperty element, bool allowEditing)
        {
            SerializedProperty property = element.FindPropertyRelative("_mask").FindPropertyRelative("_texture");
            Texture2D texture = property.objectReferenceValue as Texture2D;
            HairGroomOperator groomOperator = element.managedReferenceValue as HairGroomOperator;

            bool editActive = texture != null &&
                              ToolManager.activeToolType == typeof(HairMaskPaintTool) &&
                              HairMaskPaintTool.ConfiguredTexture == texture;
            HairInspectorGUI.PropertyWithMaskButtons(
                property, _maskLabel,
                allowEditing && texture != null && texture.isReadable,
                editActive,
                out bool createClicked, out bool editClicked);

            if (createClicked)
            {
                string ownerName = property.serializedObject.targetObject != null
                    ? property.serializedObject.targetObject.name
                    : "Hair";
                string operatorName = groomOperator != null ? groomOperator.DisplayName : "Operator";
                HairMaskCreateWindow.Open($"{ownerName} {operatorName}",
                                          HairMaskCreateWindow.MaskKind.Operator, property);
            }

            if (editClicked)
            {
                if (editActive)
                {
                    ToolManager.RestorePreviousPersistentTool();
                }
                else
                {
                    if (HairInspectorGUI.TryResolvePaintTarget(property, out LinePrimitiveRenderer hair,
                                                              out HairGroup group))
                    {
                        HairMaskPaintTool.Configure(hair, group, texture,
                            groomOperator != null ? groomOperator.InvalidateMask : null,
                            "Paint Operator Mask");
                        ToolManager.SetActiveTool<HairMaskPaintTool>();
                    }
                }
            }

            if (texture != null && !texture.isReadable)
            {
                EditorGUILayout.HelpBox($"'{texture.name}' is not readable, so it is ignored. " +
                                        "Enable Read/Write in its import settings.", MessageType.Error);
            }
        }

        private void DrawAddRow()
        {
            DrawAddButtons(typeof(HairStrandDataOperator), typeof(HairRotateOperator), typeof(HairClumpOperator),
                           typeof(HairBraidOperator));
            DrawAddButtons(typeof(HairCurlOperator), typeof(HairFrizzOperator), typeof(HairDetailOperator),
                           typeof(HairNoiseOperator));
            DrawAddButtons(typeof(HairGravityOperator), typeof(HairCutOperator), typeof(HairPropagationOperator),
                           typeof(HairGroundOperator));
            DrawAddButtons(typeof(HairDeleteOperator), typeof(HairMultiplierOperator));
        }

        private void DrawAddButtons(params Type[] operatorTypes)
        {
            Rect row = EditorGUILayout.GetControlRect(false, 20f);
            float width = (row.width - RowGap * 3f) / 4f;

            for (int i = 0; i < operatorTypes.Length; i++)
            {
                var area = new Rect(row.x + i * (width + RowGap), row.y, width, row.height);
                var groomOperator = (HairGroomOperator)Activator.CreateInstance(operatorTypes[i]);

                if (groomOperator.Priority != 0 && StackContains(operatorTypes[i]))
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        GUI.Button(area, $"+ {groomOperator.DisplayName}", EditorStyles.miniButton);
                    }

                    continue;
                }

                if (GUI.Button(area, $"+ {groomOperator.DisplayName}", EditorStyles.miniButton))
                {
                    _addType = operatorTypes[i];
                }
            }
        }

        private int PriorityAt(int index)
        {
            if (_operators == null || index < 0 || index >= _operators.arraySize)
            {
                return 0;
            }

            var groomOperator =
                _operators.GetArrayElementAtIndex(index).managedReferenceValue as HairGroomOperator;
            return groomOperator?.Priority ?? 0;
        }

        private bool StackContains(Type operatorType)
        {
            for (int i = 0; _operators != null && i < _operators.arraySize; i++)
            {
                if (_operators.GetArrayElementAtIndex(i).managedReferenceValue?.GetType() == operatorType)
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyPendingEdits()
        {
            if (_addType != null)
            {
                var added = (HairGroomOperator)Activator.CreateInstance(_addType);
                int insertAt = _operators.arraySize;
                while (insertAt > 0 && PriorityAt(insertAt - 1) > added.Priority)
                {
                    insertAt--;
                }

                _operators.InsertArrayElementAtIndex(insertAt);
                _operators.GetArrayElementAtIndex(insertAt).managedReferenceValue = added;
            }

            if (_moveUpIndex > 0)
            {
                _operators.MoveArrayElement(_moveUpIndex, _moveUpIndex - 1);
            }

            if (_moveDownIndex >= 0 && _moveDownIndex < _operators.arraySize - 1)
            {
                _operators.MoveArrayElement(_moveDownIndex, _moveDownIndex + 1);
            }

            if (_removeIndex >= 0)
            {
                _operators.DeleteArrayElementAtIndex(_removeIndex);
            }
        }
    }
}
