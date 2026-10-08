using System;
using System.IO;
using UnityEditor;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine;

namespace WarMission.EditorTools
{
    /// <summary>
    /// Sets up models exported from Blender (see HANDOFF.md): HDRP Lit materials from the glTF textures
    /// listed in each folder's materials.json, remapped onto the FBX, and Humanoid rigs for characters.
    /// glTF metallic-roughness maps (G = roughness, B = metallic, R = occlusion when packed as ORM) are
    /// converted to HDRP mask maps (R = metallic, G = occlusion, A = smoothness).
    /// </summary>
    public static class ImportedAssetBuilder
    {
        public const string PolishFolder = "Assets/_Project/Characters/Polish";
        public const string PolishModel = PolishFolder + "/Polish_Soldier.fbx";
        public const string GhostFolder = "Assets/_Project/Characters/Ghost";
        public const string GhostModel = GhostFolder + "/Ghost_Soldier.fbx";
        public const string M4Folder = "Assets/_Project/Weapons/M4";
        public const string M4Model = M4Folder + "/M4.fbx";

        [Serializable]
        class MaterialList
        {
            public MaterialEntry[] materials;
        }

        [Serializable]
        class MaterialEntry
        {
            public string name;
            public float[] baseColor;
            public float metallicScale;
            public string baseMap;
            public string mrMap;
            public string normalMap;
            public bool aoInRed;
        }

        [MenuItem("War Mission/Build Imported Assets")]
        public static void Build()
        {
            BuildModel(PolishFolder, PolishModel, humanoid: true, fixPolishSpine: true);
            // Ghost: rigged with AccuRig (CC_Base bones); Unity's auto-map is complete.
            BuildModel(GhostFolder, GhostModel, humanoid: true);
            BuildModel(M4Folder, M4Model, humanoid: false);
            AssetDatabase.SaveAssets();
            Debug.Log("[ImportedAssetBuilder] Materials and rigs built.");
        }

        static void BuildModel(string folder, string modelPath, bool humanoid, bool fixPolishSpine = false)
        {
            string json = File.ReadAllText(Path.Combine(folder, "materials.json"));
            MaterialList list = JsonUtility.FromJson<MaterialList>(json);
            string texFolder = folder + "/Textures";
            string matFolder = folder + "/Materials";
            if (!AssetDatabase.IsValidFolder(matFolder)) AssetDatabase.CreateFolder(folder, "Materials");

            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            if (humanoid)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }
            else
            {
                importer.animationType = ModelImporterAnimationType.None;
            }

            foreach (MaterialEntry entry in list.materials)
            {
                Material material = BuildMaterial(entry, texFolder, matFolder);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), entry.name), material);
            }
            importer.SaveAndReimport();
            if (fixPolishSpine) FixHumanMapping(importer);
        }

        /// <summary>
        /// The auto-mapper skips the first spine bone (maps Spine2 as Spine, no Chest) and takes head gear
        /// parented to the head for eyes and jaw. Map the spine explicitly and drop eyes / jaw.
        /// </summary>
        static void FixHumanMapping(ModelImporter importer)
        {
            HumanDescription description = importer.humanDescription;
            var human = new System.Collections.Generic.List<HumanBone>();
            foreach (HumanBone bone in description.human)
            {
                if (bone.humanName == "LeftEye" || bone.humanName == "RightEye" || bone.humanName == "Jaw"
                    || bone.humanName == "Spine" || bone.humanName == "Chest" || bone.humanName == "UpperChest") continue;
                human.Add(bone);
            }
            human.Add(new HumanBone { humanName = "Spine", boneName = "mixamorig:Spine", limit = new HumanLimit { useDefaultValues = true } });
            human.Add(new HumanBone { humanName = "Chest", boneName = "mixamorig:Spine2", limit = new HumanLimit { useDefaultValues = true } });
            description.human = human.ToArray();
            importer.humanDescription = description;
            importer.SaveAndReimport();
        }

        static Material BuildMaterial(MaterialEntry entry, string texFolder, string matFolder)
        {
            string path = $"{matFolder}/{entry.name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("HDRP/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            Color baseColor = entry.baseColor != null && entry.baseColor.Length >= 3
                ? new Color(entry.baseColor[0], entry.baseColor[1], entry.baseColor[2], 1f)
                : Color.white;
            Texture2D baseMap = LoadTexture(texFolder, entry.baseMap, linear: false, normal: false);
            // glTF multiplies the texture by the factor; Blender's default factor 0.8 is not part of the look.
            material.SetColor("_BaseColor", baseMap != null ? Color.white : baseColor);
            material.SetTexture("_BaseColorMap", baseMap);
            material.SetTexture("_NormalMap", LoadTexture(texFolder, entry.normalMap, linear: true, normal: true));
            material.SetFloat("_NormalScale", 1f);

            Texture2D mask = BuildMaskMap(texFolder, entry);
            material.SetTexture("_MaskMap", mask);
            if (mask == null)
            {
                material.SetFloat("_Metallic", 0f);
                material.SetFloat("_Smoothness", entry.name == "glass" ? 0.9f : 0.3f);
            }
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D LoadTexture(string folder, string file, bool linear, bool normal)
        {
            if (string.IsNullOrEmpty(file)) return null;
            string path = $"{folder}/{file}";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return null;
            bool changed = false;
            if (normal && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                changed = true;
            }
            if (!normal && importer.sRGBTexture == linear)
            {
                importer.sRGBTexture = !linear;
                changed = true;
            }
            if (changed) importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>HDRP mask map from a glTF metallic-roughness (or ORM) map. Written once next to the source.</summary>
        static Texture2D BuildMaskMap(string folder, MaterialEntry entry)
        {
            if (string.IsNullOrEmpty(entry.mrMap)) return null;
            string source = $"{folder}/{entry.mrMap}";
            if (!File.Exists(source)) return null;
            string maskPath = $"{folder}/{Path.GetFileNameWithoutExtension(entry.mrMap).Replace("_mr", "")}_mask.png";

            if (!File.Exists(maskPath))
            {
                var mr = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                mr.LoadImage(File.ReadAllBytes(source));
                Color32[] px = mr.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    Color32 c = px[i];
                    byte metal = (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * entry.metallicScale), 0, 255);
                    byte ao = entry.aoInRed ? c.r : (byte)255;
                    px[i] = new Color32(metal, ao, 0, (byte)(255 - c.g));
                }
                var mask = new Texture2D(mr.width, mr.height, TextureFormat.RGBA32, false, true);
                mask.SetPixels32(px);
                File.WriteAllBytes(maskPath, mask.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(mr);
                UnityEngine.Object.DestroyImmediate(mask);
                AssetDatabase.ImportAsset(maskPath);
            }
            return LoadTexture(folder, Path.GetFileName(maskPath), linear: true, normal: false);
        }
    }
}
