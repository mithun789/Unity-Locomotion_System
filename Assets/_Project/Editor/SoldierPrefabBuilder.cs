using UnityEditor;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Rendering.HighDefinition;

namespace WarMission.EditorTools
{
    /// <summary>
    /// Adds the body parts to Soldier.prefab: ragdoll (kinematic hitboxes while alive), a placeholder
    /// rifle in the right hand (WeaponGrip aligns it to both hands at runtime), and the
    /// team/weapon/health/aim/grip/ragdoll components. Then sets up the Player, Ally AI and Enemy AI variants.
    /// Safe to re-run: generated parts are removed and rebuilt, prefabs keep their GUIDs.
    /// </summary>
    public static class SoldierPrefabBuilder
    {
        const string PrefabPath = "Assets/_Project/Prefabs/Soldier.prefab";
        const string PlayerPath = "Assets/_Project/Prefabs/Soldier_Player.prefab";
        const string EnemyPath = "Assets/_Project/Prefabs/Soldier_Enemy.prefab";
        const string AllyPath = "Assets/_Project/Prefabs/Soldier_Ally.prefab";
        /// <summary>Body mesh of the Mixamo mannequin; team colour goes here.</summary>
        const string BodyRendererName = "Beta_Surface";
        const string CharacterPath = "Assets/_Project/Characters/Soldier.fbx";
        const string AimClipPath = "Assets/_Project/Animations/Mixamo/Rifle_AimIdle.fbx";
        const string MaterialsFolder = "Assets/_Project/Materials";

        const float TotalMass = 70f;
        const float HeadDamageMultiplier = 3f;

        /// <summary>Rifle placement relative to the right hand, measured from the aim idle pose.</summary>
        struct RiflePose
        {
            public Vector3 RifleLocalPosition;
            public Quaternion RifleLocalRotation;
        }

        [MenuItem("War Mission/Build Soldier Prefab")]
        public static void Build()
        {
            RiflePose pose = MeasureRiflePose(CharacterPath);
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Animator animator = root.transform.Find(BaseModelName).GetComponent<Animator>();
                RemoveHandRig(animator);

                GetOrAdd<TeamMember>(root);
                var health = GetOrAdd<Health>(root);
                GetOrAdd<SoldierWeapon>(root);
                GetOrAdd<SoldierAim>(root);
                GetOrAdd<SoldierRagdoll>(root);
                GetOrAdd<WeaponGrip>(root);
                BuildBodyParts(root, animator, pose);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[SoldierPrefabBuilder] Built {PrefabPath} (health {health.MaxHealth})");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            Material allyBody = LoadOrCreateMaterial("Team_Ally", "HDRP/Lit", new Color(0.25f, 0.42f, 0.75f), 0f);
            Material enemyBody = LoadOrCreateMaterial("Team_Enemy", "HDRP/Lit", new Color(0.72f, 0.26f, 0.2f), 0f);
            BuildVariant(PlayerPath, Team.Ally, allyBody, typeof(PlayerCover), typeof(PlayerSoldierInput), typeof(PlayerHud), typeof(PlayerRestart));
            BuildVariant(AllyPath, Team.Ally, allyBody, typeof(AIPerception), typeof(AIPathFollower), typeof(AICover), typeof(AISoldierBrain));
            BuildVariant(EnemyPath, Team.Enemy, enemyBody, typeof(AIPerception), typeof(AIPathFollower), typeof(AICover), typeof(AISoldierBrain));

            // Imported bodies (see ImportedAssetBuilder): Ghost (the hero) for the player and allies,
            // Polish soldier for enemies.
            RiflePose ghostPose = MeasureRiflePose(ImportedAssetBuilder.GhostModel);
            ApplyBody(PlayerPath, ImportedAssetBuilder.GhostModel, ghostPose);
            ApplyBody(AllyPath, ImportedAssetBuilder.GhostModel, ghostPose);
            ApplyBody(EnemyPath, ImportedAssetBuilder.PolishModel, MeasureRiflePose(ImportedAssetBuilder.PolishModel));
        }

        /// <summary>Name of the mannequin model child in Soldier.prefab.</summary>
        const string BaseModelName = "Model";
        /// <summary>Name of an imported body added by a variant (the base model is switched off).</summary>
        const string BodyModelName = "Body";

        /// <summary>
        /// Gives a variant another body: adds the model as a child, switches the base mannequin off, and builds
        /// the ragdoll and rifle on the new skeleton. Adding (instead of replacing the base) keeps the variant
        /// object IDs, so scene instances and their references survive.
        /// </summary>
        static void ApplyBody(string variantPath, string modelPath, RiflePose pose)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(variantPath);
            try
            {
                Transform baseModel = root.transform.Find(BaseModelName);
                Transform body = root.transform.Find(BodyModelName);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (body != null && PrefabUtility.GetCorrespondingObjectFromSource(body.gameObject) != source)
                {
                    Object.DestroyImmediate(body.gameObject);
                    body = null;
                }
                if (body == null)
                {
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                    model.name = BodyModelName;
                    body = model.transform;
                }
                body.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                baseModel.gameObject.SetActive(false);

                Animator baseAnimator = baseModel.GetComponent<Animator>();
                Animator animator = GetOrAdd<Animator>(body.gameObject);
                animator.runtimeAnimatorController = baseAnimator.runtimeAnimatorController;
                animator.avatar = LoadAvatar(modelPath);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                BuildBodyParts(root, animator, pose);
                PrefabUtility.SaveAsPrefabAsset(root, variantPath);
                Debug.Log($"[SoldierPrefabBuilder] {variantPath}: body {modelPath}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Ragdoll and rifle on the given skeleton, and the root components pointed at them.</summary>
        static void BuildBodyParts(GameObject root, Animator animator, RiflePose pose)
        {
            BuildRagdoll(animator, root.transform);
            Transform rifle = BuildRifle(animator, pose, out Transform muzzle, out WeaponEffects effects);

            var weapon = root.GetComponent<SoldierWeapon>();
            var aim = root.GetComponent<SoldierAim>();
            var grip = root.GetComponent<WeaponGrip>();
            SetRefs(weapon, ("muzzle", muzzle), ("effects", effects));
            SetRefs(grip, ("animator", animator), ("rifle", rifle));
            SetRefs(aim, ("animator", animator), ("weapon", weapon), ("grip", grip));
            SetRefs(root.GetComponent<SoldierRagdoll>(), ("animator", animator), ("weapon", rifle));
            SetRefs(root.GetComponent<SoldierAnimator>(), ("animator", animator));
            SetRefs(root.GetComponent<SoldierTraversal>(), ("animator", animator));
        }

        static Avatar LoadAvatar(string modelPath)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(modelPath))
                if (o is Avatar avatar) return avatar;
            return null;
        }

        /// <summary>Creates (or updates) a variant of Soldier.prefab with a team, team colour and brain components.</summary>
        static void BuildVariant(string path, Team team, Material bodyMaterial, params System.Type[] components)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
                PrefabUtility.SaveAsPrefabAsset(instance, path);
                Object.DestroyImmediate(instance);
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (System.Type type in components)
                    if (root.GetComponent(type) == null) root.AddComponent(type);

                var so = new SerializedObject(root.GetComponent<TeamMember>());
                so.FindProperty("team").enumValueIndex = (int)team;
                so.ApplyModifiedPropertiesWithoutUndo();

                foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (renderer.name == BodyRendererName) renderer.sharedMaterial = bodyMaterial;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---------------------------------------------------------------- rifle pose

        static RiflePose MeasureRiflePose(string modelPath)
        {
            var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            model.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var animator = model.GetComponent<Animator>();
                AnimationClip aim = null;
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(AimClipPath))
                    if (o is AnimationClip c && !c.name.StartsWith("__preview__")) aim = c;
                aim.SampleAnimation(model, 0f);

                Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                Vector3 rightPalm = Palm(animator, HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal);
                Vector3 leftPalm = Palm(animator, HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal);

                // Rifle origin at the right palm (pistol grip), barrel running through the left palm.
                Quaternion rifleRotation = Quaternion.LookRotation(leftPalm - rightPalm, Vector3.up);

                return new RiflePose
                {
                    RifleLocalPosition = rightHand.InverseTransformPoint(rightPalm),
                    RifleLocalRotation = Quaternion.Inverse(rightHand.rotation) * rifleRotation,
                };
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
        }

        static Vector3 Palm(Animator animator, HumanBodyBones hand, HumanBodyBones middle) =>
            (animator.GetBoneTransform(hand).position + animator.GetBoneTransform(middle).position) * 0.5f;

        static Transform UpperChest(Animator animator) =>
            animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);

        // ---------------------------------------------------------------- rifle

        static Transform BuildRifle(Animator animator, RiflePose pose, out Transform muzzle, out WeaponEffects effects)
        {
            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            DestroyChild(hand, "Rifle");

            var rifle = new GameObject("Rifle").transform;
            rifle.SetParent(hand, false);
            rifle.localPosition = pose.RifleLocalPosition;
            rifle.localRotation = pose.RifleLocalRotation;
            Vector3 s = hand.lossyScale;
            rifle.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z); // rifle children are in metres

            // M4 from Blender (ImportedAssetBuilder): its RightPalm / LeftPalm markers go on the rifle origin
            // and +Z; the Muzzle marker is the shot origin (model +Z is the bore).
            // Work in the asset's own space (root at the origin with its import rotation): there the bore is
            // +Z and up is +Y. The root's own rotation is only the FBX axis conversion.
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ImportedAssetBuilder.M4Model);
            Vector3 rightPalm = FindDeep(asset.transform, "RightPalm").position;
            Vector3 leftPalm = FindDeep(asset.transform, "LeftPalm").position;
            Vector3 muzzlePoint = FindDeep(asset.transform, "Muzzle").position;
            Quaternion toRifle = Quaternion.Inverse(Quaternion.LookRotation(leftPalm - rightPalm, Vector3.up));

            var m4 = (GameObject)PrefabUtility.InstantiatePrefab(asset, rifle);
            m4.name = "M4";
            m4.transform.localRotation = toRifle * asset.transform.localRotation;
            m4.transform.localPosition = toRifle * (asset.transform.localPosition - rightPalm);

            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(rifle, false);
            muzzle.SetLocalPositionAndRotation(toRifle * (muzzlePoint - rightPalm), toRifle);

            effects = BuildEffects(rifle, muzzle);
            return rifle;
        }

        static WeaponEffects BuildEffects(Transform rifle, Transform muzzle)
        {
            var flashColor = new Color(1f, 0.75f, 0.35f);
            Material flashMat = LoadOrCreateMaterial("MuzzleFlash", "HDRP/Unlit", flashColor, 12f);
            Material tracerMat = LoadOrCreateMaterial("Tracer", "HDRP/Unlit", new Color(1f, 0.85f, 0.5f), 8f);

            var flash = new GameObject("MuzzleFlash").transform;
            flash.SetParent(muzzle, false);
            AddPart(flash, "Core", PrimitiveType.Sphere, new Vector3(0f, 0f, 0.07f), Vector3.zero, new Vector3(0.06f, 0.06f, 0.16f), flashMat);
            AddPart(flash, "Petals", PrimitiveType.Cube, new Vector3(0f, 0f, 0.03f), Vector3.zero, new Vector3(0.14f, 0.012f, 0.012f), flashMat);

            var lightGo = new GameObject("FlashLight");
            lightGo.transform.SetParent(muzzle, false);
            lightGo.transform.localPosition = new Vector3(0f, 0f, 0.1f);
            var light = lightGo.AddComponent<Light>();
            lightGo.AddComponent<HDAdditionalLightData>();
            light.type = LightType.Point;
            light.color = flashColor;
            light.range = 6f;
            light.lightUnit = UnityEngine.Rendering.LightUnit.Lumen;
            light.intensity = 4000f;
            light.shadows = LightShadows.None;

            var tracerGo = new GameObject("Tracer");
            tracerGo.transform.SetParent(rifle, false);
            var line = tracerGo.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = 0.015f;
            line.endWidth = 0.006f;
            line.sharedMaterial = tracerMat;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;

            var effects = rifle.gameObject.AddComponent<WeaponEffects>();
            SetRefs(effects, ("muzzleFlash", flash.gameObject), ("flashLight", light), ("tracer", line));
            flash.gameObject.SetActive(false);
            light.enabled = false;
            return effects;
        }

        static void AddPart(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 euler, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = Quaternion.Euler(euler);
            part.transform.localScale = scale;
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        static Material LoadOrCreateMaterial(string name, string shaderName, Color color, float emission)
        {
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
                AssetDatabase.CreateFolder("Assets/_Project", "Materials");
            string path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(shaderName));
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = Shader.Find(shaderName);

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_UnlitColor")) material.SetColor("_UnlitColor", color);
            if (emission > 0f && material.HasProperty("_EmissiveColor"))
            {
                // Bright regardless of scene exposure.
                material.SetColor("_EmissiveColor", color * emission);
                if (material.HasProperty("_EmissiveExposureWeight")) material.SetFloat("_EmissiveExposureWeight", 0f);
            }
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.35f);
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---------------------------------------------------------------- left hand

        /// <summary>
        /// Left hand IK is no longer used: WeaponGrip runs the barrel through the left palm, so the hand is
        /// on the rifle by construction. Removes the rig from prefabs built before that change.
        /// </summary>
        static void RemoveHandRig(Animator animator)
        {
            DestroyChild(animator.transform, "WeaponRig");
            DestroyChild(UpperChest(animator), "LeftElbowHint");
            if (animator.TryGetComponent(out RigBuilder builder)) Object.DestroyImmediate(builder);
        }

        // ---------------------------------------------------------------- ragdoll

        static void BuildRagdoll(Animator animator, Transform root)
        {
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            foreach (var joint in hips.GetComponentsInChildren<CharacterJoint>(true)) Object.DestroyImmediate(joint);
            foreach (var zone in hips.GetComponentsInChildren<HitZone>(true)) Object.DestroyImmediate(zone);
            foreach (var collider in hips.GetComponentsInChildren<Collider>(true))
                if (collider.GetComponentInParent<WeaponEffects>() == null) Object.DestroyImmediate(collider);
            foreach (var body in hips.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);

            Transform B(HumanBodyBones bone) => animator.GetBoneTransform(bone);
            Vector3 up = root.up, fwd = root.forward;

            Transform chest = B(HumanBodyBones.Chest);
            Transform head = B(HumanBodyBones.Head);

            Rigidbody hipsBody = AddBody(hips, 0.2f);
            AddBox(hips, B(HumanBodyBones.LeftUpperLeg).position, B(HumanBodyBones.RightUpperLeg).position, B(HumanBodyBones.Spine).position);

            Rigidbody chestBody = AddBody(chest, 0.2f);
            AddBox(chest, chest.position, B(HumanBodyBones.LeftUpperArm).position, B(HumanBodyBones.RightUpperArm).position, B(HumanBodyBones.Neck).position);
            AddJoint(chest, hipsBody, up, fwd, -25f, 35f, 10f, 10f);

            Rigidbody headBody = AddBody(head, 0.08f);
            var headSphere = head.gameObject.AddComponent<SphereCollider>();
            headSphere.radius = 0.11f / Scale(head);
            headSphere.center = head.InverseTransformPoint(head.position + up * 0.08f);
            var headZone = head.gameObject.AddComponent<HitZone>();
            SetFloat(headZone, "damageMultiplier", HeadDamageMultiplier);
            AddJoint(head, chestBody, up, fwd, -40f, 40f, 25f, 25f);

            foreach (bool left in new[] { true, false })
            {
                Transform upperLeg = B(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                Transform lowerLeg = B(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                Transform foot = B(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                Transform upperArm = B(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                Transform lowerArm = B(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                Transform hand = B(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);

                // Thigh swings forward; knee bends backward.
                Rigidbody thigh = AddBody(upperLeg, 0.1f);
                AddCapsule(upperLeg, lowerLeg.position, 0.08f);
                AddJoint(upperLeg, hipsBody, -up, fwd, -30f, 100f, 25f, 25f);

                Rigidbody shin = AddBody(lowerLeg, 0.05f);
                AddCapsule(lowerLeg, foot.position, 0.06f);
                AddJoint(lowerLeg, thigh, -up, -fwd, -5f, 130f, 5f, 5f);

                // Upper arm swings forward; elbow bends forward.
                Vector3 armDir = (lowerArm.position - upperArm.position).normalized;
                Rigidbody arm = AddBody(upperArm, 0.04f);
                AddCapsule(upperArm, lowerArm.position, 0.05f);
                AddJoint(upperArm, chestBody, armDir, fwd, -60f, 90f, 40f, 40f);

                Vector3 forearmDir = (hand.position - lowerArm.position).normalized;
                AddBody(lowerArm, 0.03f);
                AddCapsule(lowerArm, hand.position + forearmDir * 0.08f, 0.04f);
                AddJoint(lowerArm, arm, forearmDir, fwd, -5f, 130f, 5f, 5f);
            }
        }

        static Rigidbody AddBody(Transform bone, float massShare)
        {
            var body = bone.gameObject.AddComponent<Rigidbody>();
            body.mass = TotalMass * massShare / 0.92f; // shares above sum to 0.92
            body.isKinematic = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            return body;
        }

        /// <summary>
        /// Joint whose twist axis is the bend axis: positive twist moves the bone from boneDir toward bendDir.
        /// </summary>
        static void AddJoint(Transform bone, Rigidbody parent, Vector3 boneDir, Vector3 bendDir, float low, float high, float swing1, float swing2)
        {
            var joint = bone.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = parent;
            Vector3 axis = Vector3.Cross(boneDir, bendDir).normalized;
            joint.axis = bone.InverseTransformDirection(axis);
            joint.swingAxis = bone.InverseTransformDirection(boneDir.normalized);
            joint.lowTwistLimit = new SoftJointLimit { limit = low };
            joint.highTwistLimit = new SoftJointLimit { limit = high };
            joint.swing1Limit = new SoftJointLimit { limit = swing1 };
            joint.swing2Limit = new SoftJointLimit { limit = swing2 };
            joint.enablePreprocessing = false;
            joint.enableProjection = true;
        }

        static void AddCapsule(Transform bone, Vector3 end, float radius)
        {
            Vector3 local = bone.InverseTransformPoint(end);
            int axis = 0;
            if (Mathf.Abs(local.y) > Mathf.Abs(local[axis])) axis = 1;
            if (Mathf.Abs(local.z) > Mathf.Abs(local[axis])) axis = 2;

            var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = axis;
            capsule.center = local * 0.5f;
            capsule.radius = radius / Scale(bone);
            capsule.height = Mathf.Abs(local[axis]) + capsule.radius;
        }

        static void AddBox(Transform bone, params Vector3[] worldPoints)
        {
            var bounds = new Bounds(bone.InverseTransformPoint(worldPoints[0]), Vector3.zero);
            foreach (Vector3 p in worldPoints) bounds.Encapsulate(bone.InverseTransformPoint(p));
            bounds.Expand(0.16f / Scale(bone));

            var box = bone.gameObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
        }

        static float Scale(Transform t) => t.lossyScale.x;

        // ---------------------------------------------------------------- helpers

        static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            throw new System.ArgumentException($"{parent.name} has no child '{name}'");
        }

        static T GetOrAdd<T>(GameObject go) where T : Component =>
            go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();

        static void DestroyChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) Object.DestroyImmediate(child.gameObject);
        }

        static void SetRefs(Object target, params (string field, Object value)[] refs)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in refs)
            {
                SerializedProperty p = so.FindProperty(field);
                if (p == null) throw new System.ArgumentException($"{target.GetType().Name} has no field '{field}'");
                p.objectReferenceValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
