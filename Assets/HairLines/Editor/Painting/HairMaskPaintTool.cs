using System;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace HairLines.EditorTools
{
    public class HairMaskPaintTool : EditorTool
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
            EditorGUIUtility.IconContent("Grid.PaintTool").image, "Paint hair density on the scalp");

        private GUIContent _icon;
        private HairMaskPainter _painter;
        private readonly HairScalpRaycaster _raycaster = new HairScalpRaycaster();
        private readonly HairMaskPreview _preview = new HairMaskPreview();
        private Texture2D _coverage;
        private bool _coverageDirty = true;

        private const float ScrollToRadius = 0.02f;

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

            if (_coverage != null)
            {
                UnityEngine.Object.DestroyImmediate(_coverage);
                _coverage = null;
            }

            _coverageDirty = true;
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
            HairMaskBrushOverlay.Hovering = hit;
            HairMaskBrushOverlay.Target = texture != null
                ? $"{texture.name} via {scalp.name} submesh {hair.Scalp.Submesh}"
                : "nothing";

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
                    if (!BeginStroke(group, scalp, hair.Scalp.Submesh))
                    {
                        break;
                    }

                    GUIUtility.hotControl = controlId;
                    Stamp(group, scalp, point, HairMaskBrush.Subtracts(current.shift));
                    current.Use();
                    break;

                case EventType.MouseDrag when GUIUtility.hotControl == controlId && hit:
                    Stamp(group, scalp, point, HairMaskBrush.Subtracts(current.shift));
                    current.Use();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == controlId:
                    GUIUtility.hotControl = 0;
                    EndStroke(group, hair);
                    current.Use();
                    break;

                case EventType.Repaint:
                    if (HairMaskPreview.ShowCoverage)
                    {
                        if (_coverageDirty || _coverage == null)
                        {
                            _coverage = HairCoverageMap.Build(hair, _coverage);
                            _coverageDirty = false;
                        }

                        _preview.DrawCoverage(scalp, _raycaster.Mesh, _coverage, hair.Scalp.Submesh);
                    }
                    else
                    {
                        _preview.Draw(scalp, _raycaster.Mesh, texture, false, hair.Scalp.Submesh);
                    }

                    if (hit)
                    {
                        DrawBrush(scalp, point, normal, HairMaskBrush.Subtracts(current.shift));
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
                return "Switch the group to Custom before painting its mask.";
            }

            if (hair.Source != LinePrimitiveRenderer.SourceMode.Procedural)
            {
                return "Mask painting needs a procedural source.";
            }

            if (hair.Scalp.Mode != HairScalpMode.Mesh)
            {
                return "Mask painting needs the Mesh scalp mode.";
            }

            if (hair.Scalp.Renderer == null)
            {
                return "Assign a scalp renderer first.";
            }

            Texture2D texture = ActiveTexture(group);
            if (texture == null)
            {
                return IsConfigured
                    ? "Create this mask in the inspector first."
                    : "Create a mask in the inspector first.";
            }

            if (!texture.isReadable)
            {
                return IsConfigured
                    ? "The mask texture is not readable."
                    : "The mask texture is not readable.";
            }

            return null;
        }

        private bool BeginStroke(HairGroup group, Renderer scalp, int submesh)
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

            _painter = new HairMaskPainter(mesh, texture, scalp.transform.localToWorldMatrix, submesh);
            bool valid = _painter.IsValid;

            if (scalp is SkinnedMeshRenderer)
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }

            if (!valid)
            {
                _painter = null;
            }

            return valid;
        }

        private void Stamp(HairGroup group, Renderer scalp, Vector3 point, bool subtract)
        {
            if (_painter == null)
            {
                return;
            }

            _painter.Stamp(point, HairMaskBrush.Radius, HairMaskBrush.Strength, HairMaskBrush.Hardness, subtract);

            if (HairMaskBrush.MirrorX)
            {
                _painter.Stamp(new HairScalpMirror(scalp, _raycaster.Mesh).Point(point), HairMaskBrush.Radius,
                               HairMaskBrush.Strength, HairMaskBrush.Hardness, subtract);
            }

            _painter.Apply();
        }

        private void EndStroke(HairGroup group, LinePrimitiveRenderer hair)
        {
            if (_painter == null)
            {
                return;
            }

            HairMaskBrushOverlay.Strokes++;
            Texture2D texture = ActiveTexture(group);

            if (_painter.TryTakeStroke(out RectInt region, out Color32[] before, out Color32[] after))
            {
                HairMaskUndo.Register(texture, region, before, after,
                                      string.IsNullOrWhiteSpace(_configuredUndoName)
                                          ? "Paint Hair Density"
                                          : _configuredUndoName);
            }

            _painter = null;
            HairMaskFactory.Save(texture);
            if (_configuredInvalidate != null)
            {
                _configuredInvalidate.Invoke();
            }
            else if (group != null)
            {
                group.Settings.DensityMask.Invalidate();
            }

            _coverageDirty = true;
            hair.Rebuild();
            SceneView.RepaintAll();
        }

        private static Texture2D ActiveTexture(HairGroup group)
        {
            return _configuredTexture != null
                ? _configuredTexture
                : group != null ? group.Settings.DensityMask.Texture : null;
        }

        private bool TryGetActiveContext(out HairGroup group, out LinePrimitiveRenderer hair)
        {
            group = _configuredGroup;
            hair = _configuredHair;

            if (ToolManager.activeToolType != typeof(HairMaskPaintTool) ||
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
            if (ToolManager.activeToolType == typeof(HairMaskPaintTool))
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
            if (ToolManager.activeToolType == typeof(HairMaskPaintTool))
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

        private static void DrawBrush(Renderer scalp, Vector3 point, Vector3 normal, bool subtract)
        {
            Handles.color = subtract ? new Color(1f, 0.4f, 0.35f, 0.9f) : new Color(0.4f, 0.9f, 1f, 0.9f);
            Handles.DrawWireDisc(point, normal, HairMaskBrush.Radius);
            Handles.DrawWireDisc(point, normal, HairMaskBrush.Radius * Mathf.Clamp01(HairMaskBrush.Hardness));
        }
    }
}
