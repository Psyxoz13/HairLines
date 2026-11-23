using System;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace HairLines.EditorTools
{
    public class HairCombPaintTool : EditorTool
    {
        private static Texture2D _configuredTexture;
        private static LinePrimitiveRenderer _configuredHair;
        private static HairGroup _configuredGroup;
        private static Action _configuredInvalidate;
        private static string _configuredUndoName;

        internal static bool IsConfigured => _configuredTexture != null;
        internal static Texture2D ConfiguredTexture => _configuredTexture;

        internal static void Configure(LinePrimitiveRenderer hair, HairGroup group, Texture2D texture,
                                       Action invalidate, string undoName = null)
        {
            _configuredHair = hair;
            _configuredGroup = group;
            _configuredTexture = texture;
            _configuredInvalidate = invalidate;
            _configuredUndoName = undoName;
        }

        public override GUIContent toolbarIcon => _icon ??= new GUIContent(
            EditorGUIUtility.IconContent("Grid.PaintTool").image, "Comb the hair direction on the scalp");

        private GUIContent _icon;
        private HairCombPainter _painter;
        private readonly HairScalpRaycaster _raycaster = new HairScalpRaycaster();
        private readonly HairMaskPreview _preview = new HairMaskPreview();

        private Vector3 _previousPoint;
        private Vector3 _lastStroke;
        private bool _hasPreviousPoint;

        private const float StepFraction = 0.2f;
        private const float DirectionSmoothing = 0.45f;
        private const int MaxStampsPerEvent = 8;

        private const float ScrollToRadius = 0.02f;
        private const float ArrowLength = 1.6f;

        public override void OnActivated()
        {
            HairMaskBrushOverlay.SyncOwner();
        }

        public override void OnWillBeDeactivated()
        {
            HairMaskBrushOverlay.Blocker = null;
            HairMaskBrushOverlay.ClearOwner();
            _raycaster.Release();
            _preview.Release();
            ClearConfiguration();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView)
            {
                return;
            }

            if (!TryGetActiveContext(out HairGroup group, out LinePrimitiveRenderer hair))
            {
                HairMaskBrushOverlay.ClearOwner();
                return;
            }

            HairMaskBrushOverlay.SetOwner(hair);

            string blocker = Blocker(group, hair);
            HairMaskBrushOverlay.Blocker = blocker;

            if (blocker != null)
            {
                return;
            }

            Renderer scalp = hair.Scalp.Renderer;
            Texture2D texture = ActiveTexture(group);
            _raycaster.EnsureMesh(scalp, hair.Scalp.Submesh);

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            Event current = Event.current;
            bool hit = _raycaster.Raycast(scalp, HandleUtility.GUIPointToWorldRay(current.mousePosition),
                                          out Vector3 point, out Vector3 normal);

            switch (current.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(controlId);
                    break;

                case EventType.ScrollWheel when current.shift:
                    HairMaskBrush.Radius -= current.delta.y * ScrollToRadius * HairMaskBrush.Radius;
                    current.Use();
                    break;

                case EventType.MouseDown when current.button == 0 && !current.alt && hit:
                    if (!BeginStroke(group, scalp, point, hair.Scalp.Submesh))
                    {
                        break;
                    }

                    GUIUtility.hotControl = controlId;
                    if (HairMaskBrush.Subtracts(current.shift))
                    {
                        Comb(scalp, point, normal, true);
                    }
                    current.Use();
                    break;

                case EventType.MouseDrag when GUIUtility.hotControl == controlId && hit:
                    Comb(scalp, point, normal, HairMaskBrush.Subtracts(current.shift));
                    current.Use();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == controlId:
                    GUIUtility.hotControl = 0;
                    EndStroke(group, hair);
                    current.Use();
                    break;

                case EventType.Repaint:
                    _preview.Draw(scalp, _raycaster.Mesh, texture, true, hair.Scalp.Submesh);

                    if (hit)
                    {
                        DrawBrush(point, normal, HairMaskBrush.Subtracts(current.shift));
                    }
                    break;
            }

            if (hit)
            {
                SceneView.RepaintAll();
            }
        }

        private static string Blocker(HairGroup group, LinePrimitiveRenderer hair)
        {
            if (group != null && group.Mode != HairGroupMode.Custom)
            {
                return "Switch the group to Custom before painting its comb map.";
            }

            if (hair.Source != LinePrimitiveRenderer.SourceMode.Procedural)
            {
                return "Combing needs a procedural source.";
            }

            if (hair.Scalp.Mode != HairScalpMode.Mesh)
            {
                return "Combing needs the Mesh scalp mode.";
            }

            if (hair.Scalp.Renderer == null)
            {
                return "Assign a scalp renderer first.";
            }

            Texture2D texture = ActiveTexture(group);
            if (texture == null)
            {
                return IsConfigured
                    ? "Create this map in the inspector first."
                    : "Create a comb map in the inspector first.";
            }

            if (!texture.isReadable)
            {
                return IsConfigured
                    ? "The map texture is not readable."
                    : "The comb map texture is not readable.";
            }

            return null;
        }

        private bool BeginStroke(HairGroup group, Renderer scalp, Vector3 point, int submesh)
        {
            Texture2D texture = ActiveTexture(group);
            if (texture == null)
            {
                return false;
            }

            Mesh mesh = HairMaskFactory.ResolvePaintMesh(scalp);

            if (mesh == null)
            {
                return false;
            }

            _painter = new HairCombPainter(mesh, texture, scalp.transform.localToWorldMatrix, submesh);
            bool valid = _painter.IsValid;
            _previousPoint = point;
            _hasPreviousPoint = true;
            _lastStroke = Vector3.zero;

            if (scalp is SkinnedMeshRenderer)
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }

            if (!valid)
            {
                _painter = null;
                _hasPreviousPoint = false;
            }

            return valid;
        }

        private void Comb(Renderer scalp, Vector3 point, Vector3 normal, bool clear)
        {
            if (_painter == null)
            {
                return;
            }

            if (clear)
            {
                _painter.Clear(point, HairMaskBrush.Radius, HairMaskBrush.Strength, HairMaskBrush.Hardness);
                _painter.Apply();
                _previousPoint = point;
                return;
            }

            Vector3 travelled = point - _previousPoint;
            Vector3 stroke = travelled - normal * Vector3.Dot(travelled, normal);
            float distance = stroke.magnitude;

            float step = Mathf.Max(HairMaskBrush.Radius * StepFraction, 1e-4f);

            if (distance < step)
            {
                return;
            }

            Vector3 direction = stroke / distance;
            _lastStroke = _lastStroke.sqrMagnitude > 1e-10f
                ? Vector3.Slerp(_lastStroke, direction, DirectionSmoothing).normalized
                : direction;

            int stamps = Mathf.Clamp(Mathf.CeilToInt(distance / step), 1, MaxStampsPerEvent);

            for (int i = 1; i <= stamps; i++)
            {
                Vector3 along = Vector3.Lerp(_previousPoint, point, i / (float)stamps);
                StampAt(scalp, along, _lastStroke);
            }

            _painter.Apply();
            _previousPoint = point;
        }

        private void StampAt(Renderer scalp, Vector3 point, Vector3 stroke)
        {
            _painter.Stamp(point, stroke, HairMaskBrush.Radius, HairMaskBrush.Strength,
                           HairMaskBrush.Hardness);

            if (HairMaskBrush.MirrorX)
            {
                var mirror = new HairScalpMirror(scalp, _raycaster.Mesh);
                _painter.Stamp(mirror.Point(point), mirror.Direction(stroke),
                               HairMaskBrush.Radius, HairMaskBrush.Strength, HairMaskBrush.Hardness);
            }
        }

        private void EndStroke(HairGroup group, LinePrimitiveRenderer hair)
        {
            if (_painter == null)
            {
                return;
            }

            Texture2D texture = ActiveTexture(group);

            if (_painter.TryTakeStroke(out RectInt region, out Color32[] before, out Color32[] after))
            {
                HairMaskUndo.Register(texture, region, before, after,
                                      string.IsNullOrWhiteSpace(_configuredUndoName)
                                          ? "Comb Hair"
                                          : _configuredUndoName);
            }

            _painter = null;
            _hasPreviousPoint = false;
            HairMaskFactory.Save(texture);
            if (_configuredInvalidate != null)
            {
                _configuredInvalidate.Invoke();
            }
            else
            {
                group.Settings.CombMap.Invalidate();
            }

            hair.Rebuild();
            SceneView.RepaintAll();
        }

        private static Texture2D ActiveTexture(HairGroup group)
        {
            return _configuredTexture != null
                ? _configuredTexture
                : group != null ? group.Settings.CombMap.Texture : null;
        }

        private bool TryGetActiveContext(out HairGroup group, out LinePrimitiveRenderer hair)
        {
            group = _configuredGroup;
            hair = _configuredHair;

            if (ToolManager.activeToolType != typeof(HairCombPaintTool) ||
                _configuredTexture == null || hair == null)
            {
                CloseEditing();
                return false;
            }

            return true;
        }

        private static void CloseEditing()
        {
            HairMaskBrushOverlay.ClearOwner();
            if (ToolManager.activeToolType == typeof(HairCombPaintTool))
            {
                EditorApplication.delayCall += RestoreActiveTool;
            }
            else
            {
                ClearConfiguration();
            }
        }

        private static void RestoreActiveTool()
        {
            if (ToolManager.activeToolType == typeof(HairCombPaintTool))
            {
                ToolManager.RestorePreviousPersistentTool();
            }

            ClearConfiguration();
        }

        private static void ClearConfiguration()
        {
            _configuredTexture = null;
            _configuredHair = null;
            _configuredGroup = null;
            _configuredInvalidate = null;
            _configuredUndoName = null;
        }

        private void DrawBrush(Vector3 point, Vector3 normal, bool clear)
        {
            Handles.color = clear ? new Color(1f, 0.4f, 0.35f, 0.9f) : new Color(0.6f, 1f, 0.6f, 0.9f);
            Handles.DrawWireDisc(point, normal, HairMaskBrush.Radius);

            if (clear || !_hasPreviousPoint || _lastStroke.sqrMagnitude < 1e-8f)
            {
                return;
            }

            Handles.DrawLine(point, point + _lastStroke * (HairMaskBrush.Radius * ArrowLength));
        }
    }
}
