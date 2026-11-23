using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HairLines.EditorTools
{
    public static class HairOperatorClipboard
    {
        public static bool HasStack => _stack.Count > 0;

        public static string PasteLabel => _stack.Count == 0
            ? "Paste Stack"
            : $"Paste Stack ({_stack.Count})";

        private static readonly List<HairGroomOperator> _stack = new List<HairGroomOperator>();

        public static void Copy(SerializedProperty operators)
        {
            _stack.Clear();

            for (int i = 0; i < operators.arraySize; i++)
            {
                if (operators.GetArrayElementAtIndex(i).managedReferenceValue is HairGroomOperator source)
                {
                    _stack.Add(source.Clone());
                }
            }
        }

        public static void Paste(SerializedProperty operators)
        {
            operators.ClearArray();

            for (int i = 0; i < _stack.Count; i++)
            {
                operators.InsertArrayElementAtIndex(i);
                operators.GetArrayElementAtIndex(i).managedReferenceValue = _stack[i].Clone();
            }

            operators.serializedObject.ApplyModifiedProperties();
        }
    }
}
