using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.XR;

namespace IAmCatGojoMod
{
    /// <summary>
    /// B長押し用のVRホイールメニュー。
    ///
    /// GojoAbilityWheelMenu_v4_TextMaterialFix:
    /// - Initializeでは何も生成しない遅延生成方式
    /// - B長押し中だけ、HMD前方に3Dホイールを表示
    /// - 右コントローラーの向き/手の位置で5能力を選択
    /// - Release時にManager側がSelectedAbilityを採用
    /// - Unity UI/TextMeshProは使わず、Primitive + LineRenderer中心で安全寄り
    /// - TextMeshはReflectionで存在する場合だけ使う。なくても色付きノードだけで表示可能
    /// </summary>
    public sealed class GojoAbilityWheelMenu
    {
        private const string VersionTag = "GojoAbilityWheelMenu_v4_TextMaterialFix";

        private const int SlotCount = 5;
        private const int RingSegments = 96;
        private const int SlotRingSegments = 48;

        private const float MenuDistance = 1.18f;
        private const float MenuVerticalOffset = -0.04f;
        private const float WheelRadius = 0.285f;
        private const float CenterRadius = 0.060f;
        private const float SlotSphereScale = 0.045f;
        private const float SelectedSlotSphereScale = 0.070f;

        private const float AimSelectionThreshold = 0.22f;
        private const float HandSelectionThreshold = 0.12f;

        private bool _initialized;
        private bool _created;
        private bool _visible;

        private GameObject _root;
        private GameObject _centerOrb;
        private GameObject _selectorLineObject;
        private GameObject _titleLabelObject;
        private GameObject _selectedLabelObject;

        private LineRenderer _outerRing;
        private LineRenderer _innerRing;
        private LineRenderer _selectorLine;

        private Material _ringMaterial;
        private Material _dimMaterial;
        private Material _selectorMaterial;
        private Material _centerMaterial;
        private Material _textMaterial;

        private readonly SlotVisual[] _slots = new SlotVisual[SlotCount];

        private GojoAbilityType _currentAbility = GojoAbilityType.Infinity;
        private GojoAbilityType _selectedAbility = GojoAbilityType.Infinity;
        private bool _hasDirectionalSelection;

        private float _lastSelectionLogTime;

        public GojoAbilityType SelectedAbility => _selectedAbility;
        public bool HasDirectionalSelection => _hasDirectionalSelection;
        public bool IsVisible => _visible;

        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            // 重要: Initializeでは何も生成しない。
            // I Am CatのVR描画ではShow時の遅延生成が安定。
            MelonLogger.Msg("[" + VersionTag + "] Initialized. DeferredCreate=True");
        }

        public void Show(GojoAbilityType currentAbility)
        {
            if (!_initialized)
            {
                Initialize();
            }

            if (!_created || _root == null)
            {
                CreateObjects();
            }

            _currentAbility = currentAbility;
            _selectedAbility = currentAbility;
            _hasDirectionalSelection = false;
            _visible = true;

            if (_root != null)
            {
                _root.SetActive(true);
            }

            UpdateMenu(currentAbility);

            MelonLogger.Msg("[" + VersionTag + "] Show. Current=" + currentAbility);
        }

        public void Hide()
        {
            if (!_visible && (_root == null || !_root.activeSelf))
            {
                return;
            }

            _visible = false;

            if (_root != null)
            {
                _root.SetActive(false);
            }

            MelonLogger.Msg("[" + VersionTag + "] Hide. Selected=" + _selectedAbility + ", Directional=" + _hasDirectionalSelection);
        }

        public void UpdateMenu(GojoAbilityType currentAbility)
        {
            if (!_created || _root == null || !_visible)
            {
                return;
            }

            _currentAbility = currentAbility;

            Transform cam = GetCameraTransform();
            if (cam != null)
            {
                // HMD前方に固定。VR UIなのでWorld空間に置く。
                _root.transform.position = cam.position + cam.forward * MenuDistance + cam.up * MenuVerticalOffset;
                _root.transform.rotation = Quaternion.LookRotation(cam.forward, cam.up);
            }

            Vector2 selectionDir;
            GojoAbilityType handSelected;
            bool hasSelection = TryGetDirectionalSelection(out handSelected, out selectionDir);

            _hasDirectionalSelection = hasSelection;
            _selectedAbility = hasSelection ? handSelected : currentAbility;

            UpdateVisuals(selectionDir);
        }

        private void CreateObjects()
        {
            _created = true;

            _root = new GameObject("Gojo_AbilityWheelMenu_Root_v1");
            UnityEngine.Object.DontDestroyOnLoad(_root);

            _ringMaterial = CreateTransparentMaterial("Gojo_Wheel_Ring_Mat_v1", new Color(0.74f, 0.94f, 1.0f, 0.74f));
            _dimMaterial = CreateTransparentMaterial("Gojo_Wheel_Dim_Mat_v1", new Color(0.18f, 0.24f, 0.34f, 0.52f));
            _selectorMaterial = CreateTransparentMaterial("Gojo_Wheel_Selector_Mat_v1", new Color(1.0f, 1.0f, 1.0f, 0.96f));
            _centerMaterial = CreateTransparentMaterial("Gojo_Wheel_Center_Mat_v1", new Color(0.92f, 0.98f, 1.0f, 0.88f));
            // v4: TextMesh の Renderer.material は差し替えない。
            // フォントアトラス付きの標準マテリアルを壊すと、文字化け/白四角化するため。
            _textMaterial = null;

            _outerRing = CreateLine("Gojo_Wheel_OuterRing_v1", _root.transform, _ringMaterial, 0.0075f, RingSegments + 1, true);
            _innerRing = CreateLine("Gojo_Wheel_InnerRing_v1", _root.transform, _dimMaterial, 0.0045f, RingSegments + 1, true);

            SetCircleLine(_outerRing, WheelRadius, RingSegments);
            SetCircleLine(_innerRing, CenterRadius * 1.55f, RingSegments);

            _centerOrb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _centerOrb.name = "Gojo_Wheel_CenterOrb_v1";
            _centerOrb.transform.SetParent(_root.transform, false);
            _centerOrb.transform.localPosition = Vector3.zero;
            _centerOrb.transform.localScale = Vector3.one * CenterRadius;
            RemoveCollider(_centerOrb);
            SetRendererMaterial(_centerOrb, _centerMaterial);

            _selectorLineObject = new GameObject("Gojo_Wheel_SelectorLine_v1");
            _selectorLineObject.transform.SetParent(_root.transform, false);
            _selectorLine = _selectorLineObject.AddComponent<LineRenderer>();
            SetupLine(_selectorLine, _selectorMaterial, 0.010f, 2, false);

            CreateSlots();
            CreateOptionalLabels();

            _root.SetActive(false);

            MelonLogger.Msg("[" + VersionTag + "] CreateObjects completed. Slots=" + SlotCount);
        }

        private void CreateSlots()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                GojoAbilityType ability = IndexToAbility(i);
                float angle = GetSlotAngleDegrees(ability);
                Vector3 pos = AngleToLocalPosition(angle, WheelRadius);

                GameObject slotRoot = new GameObject("Gojo_Wheel_Slot_" + ability + "_v1");
                slotRoot.transform.SetParent(_root.transform, false);
                slotRoot.transform.localPosition = pos;

                GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                orb.name = "Gojo_Wheel_SlotOrb_" + ability + "_v1";
                orb.transform.SetParent(slotRoot.transform, false);
                orb.transform.localPosition = Vector3.zero;
                orb.transform.localScale = Vector3.one * SlotSphereScale;
                RemoveCollider(orb);

                Material slotMaterial = CreateTransparentMaterial(
                    "Gojo_Wheel_SlotMat_" + ability + "_v1",
                    GetAbilityColor(ability, 0.72f));
                SetRendererMaterial(orb, slotMaterial);

                LineRenderer ring = CreateLine("Gojo_Wheel_SlotRing_" + ability + "_v1", slotRoot.transform, _ringMaterial, 0.0045f, SlotRingSegments + 1, true);
                SetCircleLine(ring, 0.060f, SlotRingSegments);

                GameObject label = CreateWheelText(
                    "Gojo_Wheel_Label_" + ability + "_v1",
                    slotRoot.transform,
                    GetAbilityLabel(ability),
                    new Vector3(0f, -0.085f, 0.002f),
                    0.0065f,
                    GetAbilityColor(ability, 0.96f));

                _slots[i] = new SlotVisual
                {
                    Ability = ability,
                    Root = slotRoot,
                    Orb = orb,
                    Ring = ring,
                    LabelObject = label,
                    Material = slotMaterial,
                    BasePosition = pos
                };
            }
        }

        private void CreateOptionalLabels()
        {
            _titleLabelObject = CreateWheelText(
                "Gojo_Wheel_TitleLabel_v1",
                _root.transform,
                "GOJO ABILITY",
                new Vector3(0f, 0.420f, 0.002f),
                0.0070f,
                new Color(0.88f, 0.98f, 1.0f, 0.92f));

            _selectedLabelObject = CreateWheelText(
                "Gojo_Wheel_SelectedLabel_v1",
                _root.transform,
                "SELECT",
                new Vector3(0f, -0.430f, 0.002f),
                0.0080f,
                new Color(1.0f, 1.0f, 1.0f, 0.96f));
        }

        private void UpdateVisuals(Vector2 selectionDir)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 7.0f);

            if (_outerRing != null)
            {
                _outerRing.widthMultiplier = Mathf.Lerp(0.0065f, 0.0105f, pulse);
            }

            for (int i = 0; i < _slots.Length; i++)
            {
                SlotVisual slot = _slots[i];
                if (slot.Root == null)
                {
                    continue;
                }

                bool selected = slot.Ability == _selectedAbility;
                bool current = slot.Ability == _currentAbility;

                float scale = selected ? SelectedSlotSphereScale : SlotSphereScale;
                if (current && !selected)
                {
                    scale *= 1.12f;
                }

                if (slot.Orb != null)
                {
                    slot.Orb.transform.localScale = Vector3.one * scale;
                }

                if (slot.Root != null)
                {
                    float pop = selected ? Mathf.Lerp(1.0f, 1.10f, pulse) : 1.0f;
                    slot.Root.transform.localPosition = slot.BasePosition * pop;
                }

                if (slot.Material != null)
                {
                    float alpha = selected ? 0.96f : (current ? 0.82f : 0.48f);
                    SetMaterialColor(slot.Material, GetAbilityColor(slot.Ability, alpha));
                }

                if (slot.Ring != null)
                {
                    slot.Ring.widthMultiplier = selected ? 0.0095f : (current ? 0.0065f : 0.0038f);
                }
            }

            if (_selectorLine != null)
            {
                Vector3 end = Vector3.zero;

                if (_hasDirectionalSelection && selectionDir.sqrMagnitude > 0.001f)
                {
                    Vector2 d = selectionDir.normalized;
                    end = new Vector3(d.x, d.y, 0.0f) * (WheelRadius * 0.82f);
                }
                else
                {
                    end = AngleToLocalPosition(GetSlotAngleDegrees(_selectedAbility), WheelRadius * 0.62f);
                }

                _selectorLine.SetPosition(0, Vector3.zero);
                _selectorLine.SetPosition(1, end);
                _selectorLine.widthMultiplier = _hasDirectionalSelection ? 0.012f : 0.006f;
            }

            SetWheelText(_selectedLabelObject, GetAbilityLabel(_selectedAbility));

            if (Time.time >= _lastSelectionLogTime && _hasDirectionalSelection)
            {
                _lastSelectionLogTime = Time.time + 0.45f;
                MelonLogger.Msg("[" + VersionTag + "] Selecting: " + _selectedAbility);
            }
        }

        private bool TryGetDirectionalSelection(out GojoAbilityType selected, out Vector2 selectionDir)
        {
            selected = _currentAbility;
            selectionDir = Vector2.zero;

            try
            {
                InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);

                if (!right.isValid || !head.isValid)
                {
                    return false;
                }

                bool hasRightPos = right.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 rightLocalPos);
                bool hasRightRot = right.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rightLocalRot);
                bool hasHeadPos = head.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 headLocalPos);
                bool hasHeadRot = head.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion headLocalRot);

                // まずはコントローラーの向きで選択。
                // 指し棒みたいに上下左右へ傾けると選べる想定。
                if (hasRightRot && hasHeadRot)
                {
                    Vector3 aimHeadLocal = Quaternion.Inverse(headLocalRot) * (rightLocalRot * Vector3.forward);
                    Vector2 aimDir = new Vector2(aimHeadLocal.x, aimHeadLocal.y);

                    if (aimDir.magnitude >= AimSelectionThreshold)
                    {
                        selectionDir = aimDir.normalized;
                        selected = DirectionToAbility(selectionDir);
                        return true;
                    }
                }

                // Controller forward軸がゲーム/機種で微妙な場合の保険。
                // HMDから見た右手の位置で選択する。
                if (hasRightPos && hasHeadPos && hasHeadRot)
                {
                    Vector3 handHeadLocal = Quaternion.Inverse(headLocalRot) * (rightLocalPos - headLocalPos);

                    // 右手の通常位置が右下にあるので、少しだけニュートラル補正する。
                    Vector2 handDir = new Vector2(handHeadLocal.x - 0.24f, handHeadLocal.y + 0.30f);

                    if (handDir.magnitude >= HandSelectionThreshold)
                    {
                        selectionDir = handDir.normalized;
                        selected = DirectionToAbility(selectionDir);
                        return true;
                    }
                }
            }
            catch
            {
                // 失敗時は現在能力を維持。
            }

            return false;
        }

        private GojoAbilityType DirectionToAbility(Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.0001f)
            {
                return _currentAbility;
            }

            dir.Normalize();

            GojoAbilityType best = GojoAbilityType.Infinity;
            float bestDot = -999f;

            for (int i = 0; i < SlotCount; i++)
            {
                GojoAbilityType ability = IndexToAbility(i);
                float angleRad = GetSlotAngleDegrees(ability) * Mathf.Deg2Rad;
                Vector2 slotDir = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
                float dot = Vector2.Dot(dir, slotDir);

                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = ability;
                }
            }

            return best;
        }

        private static GojoAbilityType IndexToAbility(int index)
        {
            switch (index)
            {
                case 0: return GojoAbilityType.Infinity;
                case 1: return GojoAbilityType.Blue;
                case 2: return GojoAbilityType.Red;
                case 3: return GojoAbilityType.Purple;
                case 4: return GojoAbilityType.DomainExpansion;
                default: return GojoAbilityType.Infinity;
            }
        }

        private static float GetSlotAngleDegrees(GojoAbilityType ability)
        {
            // 画面上の配置:
            // 上: Infinity
            // 右上: Blue
            // 右下: Red
            // 左下: Purple
            // 左上: DomainExpansion
            switch (ability)
            {
                case GojoAbilityType.Infinity: return 90f;
                case GojoAbilityType.Blue: return 18f;
                case GojoAbilityType.Red: return -54f;
                case GojoAbilityType.Purple: return -126f;
                case GojoAbilityType.DomainExpansion: return 162f;
                default: return 90f;
            }
        }

        private static string GetAbilityLabel(GojoAbilityType ability)
        {
            switch (ability)
            {
                case GojoAbilityType.Infinity: return "INFINITY";
                case GojoAbilityType.Blue: return "BLUE";
                case GojoAbilityType.Red: return "RED";
                case GojoAbilityType.Purple: return "PURPLE";
                case GojoAbilityType.DomainExpansion: return "DOMAIN";
                default: return ability.ToString().ToUpperInvariant();
            }
        }

        private static Color GetAbilityColor(GojoAbilityType ability, float alpha)
        {
            switch (ability)
            {
                case GojoAbilityType.Infinity: return new Color(0.62f, 0.95f, 1.0f, alpha);
                case GojoAbilityType.Blue: return new Color(0.12f, 0.48f, 1.0f, alpha);
                case GojoAbilityType.Red: return new Color(1.0f, 0.18f, 0.12f, alpha);
                case GojoAbilityType.Purple: return new Color(0.66f, 0.20f, 1.0f, alpha);
                case GojoAbilityType.DomainExpansion: return new Color(0.92f, 0.92f, 1.0f, alpha);
                default: return new Color(1.0f, 1.0f, 1.0f, alpha);
            }
        }

        private static Vector3 AngleToLocalPosition(float angleDegrees, float radius)
        {
            float a = angleDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
        }

        private static Transform GetCameraTransform()
        {
            try
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    return cam.transform;
                }
            }
            catch
            {
            }

            return null;
        }

        private static LineRenderer CreateLine(string name, Transform parent, Material material, float width, int positionCount, bool loop)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            LineRenderer lr = obj.AddComponent<LineRenderer>();
            SetupLine(lr, material, width, positionCount, loop);
            return lr;
        }

        private static void SetupLine(LineRenderer lr, Material material, float width, int positionCount, bool loop)
        {
            lr.useWorldSpace = false;
            lr.positionCount = positionCount;
            lr.loop = loop;
            lr.widthMultiplier = width;
            lr.material = material;
            lr.numCornerVertices = 4;
            lr.numCapVertices = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
        }

        private static void SetCircleLine(LineRenderer lr, float radius, int segments)
        {
            if (lr == null)
            {
                return;
            }

            for (int i = 0; i <= segments; i++)
            {
                float a = (Mathf.PI * 2.0f * i) / segments;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }
        }

        private static Material CreateTransparentMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material mat = new Material(shader);
            mat.name = name;
            SetMaterialColor(mat, color);

            try
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.SetInt("_Cull", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = 3300;
            }
            catch
            {
            }

            try
            {
                mat.SetFloat("_Mode", 3f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.SetInt("_Cull", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3300;
            }
            catch
            {
            }

            return mat;
        }

        private static void SetMaterialColor(Material mat, Color color)
        {
            if (mat == null)
            {
                return;
            }

            try { mat.color = color; } catch { }
            try { mat.SetColor("_BaseColor", color); } catch { }
            try { mat.SetColor("_Color", color); } catch { }
        }

        private static void SetRendererMaterial(GameObject obj, Material mat)
        {
            if (obj == null || mat == null)
            {
                return;
            }

            try
            {
                Renderer renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material = mat;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }
            catch
            {
            }
        }

        private static void RemoveCollider(GameObject obj)
        {
            if (obj == null)
            {
                return;
            }

            try
            {
                Collider col = obj.GetComponent<Collider>();
                if (col != null)
                {
                    UnityEngine.Object.Destroy(col);
                }
            }
            catch
            {
            }
        }

        private GameObject CreateWheelText(string name, Transform parent, string text, Vector3 localPosition, float scale, Color color)
        {
            try
            {
                GameObject obj = new GameObject(name);
                obj.transform.SetParent(parent, false);
                obj.transform.localPosition = localPosition;
                obj.transform.localRotation = Quaternion.identity;
                obj.transform.localScale = Vector3.one * scale;

                // v2:
                // Il2CPP環境では GameObject.AddComponent(System.Type) が
                // Il2CppSystem.Type を要求してCS1503になることがある。
                // TextMeshはUnityEngine組み込みComponentなので、Reflectionではなく
                // generic AddComponent<TextMesh>() で追加する。
                TextMesh textMesh = obj.AddComponent<TextMesh>();
                if (textMesh == null)
                {
                    UnityEngine.Object.Destroy(obj);
                    return null;
                }

                textMesh.text = text;
                textMesh.fontSize = 64;
                textMesh.anchor = TextAnchor.MiddleCenter;
                textMesh.alignment = TextAlignment.Center;
                textMesh.color = color;
                textMesh.richText = false;

                try
                {
                    // 重要:
                    // TextMesh の Renderer.material を独自Unlit Materialに差し替えると、
                    // フォント用テクスチャ/アトラスが外れて文字化け・白四角化しやすい。
                    // そのため material は触らず、色は textMesh.color だけで指定する。
                    Renderer renderer = obj.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                    }
                }
                catch
                {
                }

                return obj;
            }
            catch (Exception ex)
            {
                try
                {
                    MelonLogger.Warning("[" + VersionTag + "] TextMesh create failed. Continue without text. " + ex.GetType().Name + ": " + ex.Message);
                }
                catch
                {
                }

                return null;
            }
        }

        private static void SetWheelText(GameObject obj, string text)
        {
            if (obj == null)
            {
                return;
            }

            try
            {
                TextMesh textMesh = obj.GetComponent<TextMesh>();
                if (textMesh != null)
                {
                    textMesh.text = text;
                }
            }
            catch
            {
            }
        }

        private struct SlotVisual
        {
            public GojoAbilityType Ability;
            public GameObject Root;
            public GameObject Orb;
            public LineRenderer Ring;
            public GameObject LabelObject;
            public Material Material;
            public Vector3 BasePosition;
        }
    }
}
