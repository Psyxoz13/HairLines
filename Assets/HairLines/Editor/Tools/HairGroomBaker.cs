using System.IO;
using UnityEditor;

namespace HairLines.EditorTools
{
    public static class HairGroomBaker
    {
        public const string DefaultFolder = "Assets/HairLines/Grooms";

        public static HairGroomAsset Bake(LinePrimitiveModel model, string name)
        {
            if (model == null || model.StrandCount == 0)
            {
                return null;
            }

            Directory.CreateDirectory(DefaultFolder);

            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{DefaultFolder}/{name}.{HairGroomImporter.GroomExtension}");

            File.WriteAllBytes(path, HairGroomBlob.Write(model));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<HairGroomAsset>(path);
        }
    }
}
