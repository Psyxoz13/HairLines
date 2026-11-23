using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace HairLines.EditorTools
{
    [ScriptedImporter(1, new[] { GroomExtension, CyHairExtension })]
    public class HairGroomImporter : ScriptedImporter
    {
        public const string GroomExtension = "hairgroom";
        public const string CyHairExtension = "hair";

        [SerializeField]
        private HairImportSettings _import = new HairImportSettings();

        public override void OnImportAsset(AssetImportContext context)
        {
            var asset = ScriptableObject.CreateInstance<HairGroomAsset>();
            asset.name = Path.GetFileNameWithoutExtension(context.assetPath);

            LinePrimitiveModel model = LoadModel(context.assetPath, out byte[] blob);

            if (model == null)
            {
                context.AddObjectToAsset("groom", asset);
                context.SetMainObject(asset);
                return;
            }

            blob ??= HairGroomBlob.Write(model);
            asset.Fill(blob, model.StrandCount, model.VertexCount, model.LocalBounds);
            model.Dispose();

            context.AddObjectToAsset("groom", asset);
            context.SetMainObject(asset);
        }

        private LinePrimitiveModel LoadModel(string assetPath, out byte[] blob)
        {
            blob = null;

            try
            {
                if (assetPath.EndsWith("." + GroomExtension))
                {
                    blob = File.ReadAllBytes(assetPath);
                    return HairGroomBlob.Read(blob);
                }

                return LineModelIO.LoadCyHair(assetPath, _import);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[HairLines] Failed to import '{assetPath}': {e.Message}");
                blob = null;
                return null;
            }
        }
    }
}
