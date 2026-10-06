using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WarMission.EditorTools
{
    /// <summary>
    /// Bakes TraversalAction assets from their clips (foot height and forward progress over time, for motion
    /// warping), rebuilds the Animator (one state per action) and assigns the actions to Soldier.prefab.
    /// Add or swap clips in <see cref="Definitions"/> and run War Mission > Build Traversal Actions.
    /// </summary>
    public static class TraversalActionBuilder
    {
        public const string Folder = "Assets/_Project/Animations/Traversal";
        const string MixamoFolder = "Assets/_Project/Animations/Mixamo/";
        const string CharacterPath = "Assets/_Project/Characters/Soldier.fbx";
        const string PrefabPath = "Assets/_Project/Prefabs/Soldier.prefab";
        const int Samples = 60;

        struct Definition
        {
            public string Name;
            public string ClipFile;
            public TraversalKind Kind;
            public TraversalWarp Warp;
            public float MinHeight, MaxHeight, Speed;
            public float PlayUntil;
            public float EndBlend; // 0 = default (0.15 s)
        }

        /// <summary>
        /// Order matters: the first action whose kind and height range match is used.
        /// Vault: Mixamo "Jumping - Male Jumping Over An Obstacle With 1 Hand Planted".
        /// Drop: under ~1 m a quick hop down (rifle forward jump); higher, Mixamo "Jumping Down - Jumping Down
        /// Over A Railing", cut at the landing squat (the clip then rolls forward and
        /// kneels, which read as a fall); the long end blend stands the soldier up from the squat.
        /// Mantle: stand-in hop (rifle forward jump) until a climb-onto clip is added (use ScaleToEnd).
        /// </summary>
        static readonly Definition[] Definitions =
        {
            new Definition { Name = "Traverse_Vault", ClipFile = "Traverse_Vault_OneHand", Kind = TraversalKind.Vault, Warp = TraversalWarp.ScaleToPeak, MinHeight = 0.4f, MaxHeight = 1.3f, Speed = 1f, PlayUntil = 1f },
            new Definition { Name = "Traverse_Mantle", ClipFile = "Rifle_Jump_Forward", Kind = TraversalKind.Mantle, Warp = TraversalWarp.AddRise, MinHeight = 0.3f, MaxHeight = 1.4f, Speed = 1f, PlayUntil = 1f },
            // Mixamo "box Climbing": hands on the ledge, climbs onto ~2.2 m; scaled to the real height.
            new Definition { Name = "Traverse_ClimbHigh", ClipFile = "Traverse_Climb_Box", Kind = TraversalKind.Mantle, Warp = TraversalWarp.ScaleToEnd, MinHeight = 1.4f, MaxHeight = 2.4f, Speed = 1.15f, PlayUntil = 1f },
            new Definition { Name = "Traverse_DropLow", ClipFile = "Rifle_Jump_Forward", Kind = TraversalKind.Drop, Warp = TraversalWarp.AddRise, MinHeight = 0.3f, MaxHeight = 1.05f, Speed = 1.2f, PlayUntil = 1f },
            new Definition { Name = "Traverse_Drop", ClipFile = "Traverse_Drop_Rail", Kind = TraversalKind.Drop, Warp = TraversalWarp.ScaleToEnd, MinHeight = 1.05f, MaxHeight = 2.6f, Speed = 1.4f, PlayUntil = 0.285f, EndBlend = 0.4f },
        };

        [MenuItem("War Mission/Build Traversal Actions")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project/Animations", "Traversal");

            var actions = new List<TraversalAction>();
            foreach (Definition d in Definitions) actions.Add(BakeAction(d));
            AssetDatabase.SaveAssets();

            SoldierAnimatorBuilder.Build();

            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                if (!root.TryGetComponent(out SoldierTraversal traversal)) traversal = root.AddComponent<SoldierTraversal>();
                var so = new SerializedObject(traversal);
                SerializedProperty list = so.FindProperty("actions");
                list.arraySize = actions.Count;
                for (int i = 0; i < actions.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = actions[i];
                so.FindProperty("animator").objectReferenceValue = root.GetComponentInChildren<Animator>();
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log($"[TraversalActionBuilder] Built {actions.Count} traversal actions");
        }

        /// <summary>All traversal assets, in definition order (the Animator builder adds a state for each).</summary>
        public static TraversalAction[] LoadAll() =>
            Definitions.Select(d => AssetDatabase.LoadAssetAtPath<TraversalAction>($"{Folder}/{d.Name}.asset")).Where(a => a != null).ToArray();

        static TraversalAction BakeAction(Definition d)
        {
            string path = $"{Folder}/{d.Name}.asset";
            var action = AssetDatabase.LoadAssetAtPath<TraversalAction>(path);
            if (action == null)
            {
                action = ScriptableObject.CreateInstance<TraversalAction>();
                AssetDatabase.CreateAsset(action, path);
            }

            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(MixamoFolder + d.ClipFile + ".fbx")
                .OfType<AnimationClip>().First(c => c.name == d.ClipFile);

            action.Kind = d.Kind;
            action.Warp = d.Warp;
            action.StateName = d.Name;
            action.MinHeight = d.MinHeight;
            action.MaxHeight = d.MaxHeight;
            action.Speed = d.Speed;
            action.PlayUntil = d.PlayUntil;
            action.EndBlend = d.EndBlend > 0f ? d.EndBlend : 0.15f;
            action.Clip = clip;
            action.ClipLength = clip.length;
            action.FootHeight = SampleFootHeight(clip, out action.FootBase);
            action.Forward = ForwardProgress(clip, action.FootHeight);
            action.KeyHeight = d.Warp == TraversalWarp.ScaleToPeak ? MaxValue(action.FootHeight, d.PlayUntil)
                             : d.Warp == TraversalWarp.ScaleToEnd ? action.FootHeight.Evaluate(d.PlayUntil) : 0f;
            EditorUtility.SetDirty(action);
            return action;
        }

        /// <summary>Lowest foot point above its start height, in metres, over normalized time.</summary>
        static AnimationCurve SampleFootHeight(AnimationClip clip, out float footBase)
        {
            var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath));
            model.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var animator = model.GetComponent<Animator>();
                Transform[] feet =
                {
                    animator.GetBoneTransform(HumanBodyBones.LeftToes), animator.GetBoneTransform(HumanBodyBones.RightToes),
                    animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.RightFoot),
                };
                float Lowest() => Mathf.Min(feet[0].position.y, feet[1].position.y, feet[2].position.y - 0.08f, feet[3].position.y - 0.08f);

                var curve = new AnimationCurve();
                float baseline = 0f;
                for (int i = 0; i <= Samples; i++)
                {
                    float u = (float)i / Samples;
                    clip.SampleAnimation(model, u * clip.length);
                    float h = Lowest();
                    if (i == 0) baseline = h;
                    curve.AddKey(u, h - baseline);
                }
                footBase = baseline - model.transform.position.y;
                return curve;
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
        }

        /// <summary>
        /// Forward progress 0..1. From the clip's root motion when it travels; for in-place clips, eased across
        /// the airborne part (feet off the ground).
        /// </summary>
        static AnimationCurve ForwardProgress(AnimationClip clip, AnimationCurve footHeight)
        {
            AnimationCurve x = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.x"));
            AnimationCurve z = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.z"));
            if (x != null && z != null)
            {
                var start = new Vector2(x.Evaluate(0f), z.Evaluate(0f));
                float total = (new Vector2(x.Evaluate(clip.length), z.Evaluate(clip.length)) - start).magnitude;
                if (total > 0.3f)
                {
                    var curve = new AnimationCurve();
                    for (int i = 0; i <= Samples; i++)
                    {
                        float t = (float)i / Samples * clip.length;
                        float d = (new Vector2(x.Evaluate(t), z.Evaluate(t)) - start).magnitude;
                        curve.AddKey((float)i / Samples, Mathf.Clamp01(d / total));
                    }
                    return curve;
                }
            }

            float takeoff = 0f, landing = 1f;
            bool airborne = false;
            for (int i = 0; i <= Samples; i++)
            {
                float u = (float)i / Samples;
                bool up = footHeight.Evaluate(u) > 0.05f;
                if (up && !airborne) { takeoff = u; airborne = true; }
                if (up) landing = u;
            }
            if (!airborne) return AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            // Start moving a little before takeoff and finish at landing.
            return AnimationCurve.EaseInOut(Mathf.Max(0f, takeoff - 0.05f), 0f, Mathf.Min(1f, landing + 0.02f), 1f);
        }

        static float MaxValue(AnimationCurve curve, float until) => curve.keys.Where(k => k.time <= until).Max(k => k.value);
    }
}
