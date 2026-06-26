using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace IAmCatGojoMod
{
    /// <summary>
    /// 蒼 / Blue の見た目担当。
    ///
    /// BlueEffect_v7_LightningVortex:
    /// - v6で見えることが確認できた「Show時に遅延生成 + Infinity式透明Material」を維持
    /// - 青いコア球
    /// - コア周囲の高速リング
    /// - 外周から中心へ吸い込まれる渦ライン
    /// - 対象物/外周から中心へ走る稲妻ライン
    /// - 小粒子が中心へ吸い込まれるように移動
    ///
    /// 注意:
    /// - Initialize時には描画Objectを作らない。
    /// - GameObject/MaterialはShow時に作る。ここがv6で見えた重要ポイント。
    /// </summary>
    public sealed class BlueEffect
    {
        private const string VersionTag = "BlueEffect_v7_LightningVortex";

        private const int RingSegments = 128;
        private const int CoreRingCount = 4;
        private const int OuterRingCount = 5;
        private const int VortexLineCount = 18;
        private const int VortexLineSegments = 20;
        private const int LightningLineCount = 14;
        private const int LightningSegments = 7;
        private const int ParticleCount = 48;
        private const int MaxTargetLineCount = 36;

        private GameObject _root;
        private GameObject _coreSphere;

        private readonly List<LineRenderer> _coreRings = new List<LineRenderer>();
        private readonly List<LineRenderer> _outerRings = new List<LineRenderer>();
        private readonly List<LineRenderer> _vortexLines = new List<LineRenderer>();
        private readonly List<LineRenderer> _lightningLines = new List<LineRenderer>();
        private readonly List<LineRenderer> _targetLines = new List<LineRenderer>();
        private readonly List<GameObject> _particles = new List<GameObject>();
        private readonly List<Vector3> _particleSeeds = new List<Vector3>();

        private Material _coreMaterial;
        private Material _ringMaterial;
        private Material _lineMaterial;
        private Material _lightningMaterial;
        private Material _particleMaterial;

        private bool _initialized;
        private bool _visible;
        private float _radius = 5.5f;
        private float _coreRadius = 0.65f;
        private float _nextDebugLogTime;
        private float _shownTime;

        public bool IsVisible => _visible;

        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            MelonLogger.Msg("[" + VersionTag + "] Initialized. DeferredCreate=True");
        }

        public void Show(Vector3 corePosition, float radius, float coreRadius)
        {
            if (!_initialized)
            {
                Initialize();
            }

            _radius = Mathf.Max(0.5f, radius);
            _coreRadius = Mathf.Max(0.25f, coreRadius);
            _visible = true;
            _shownTime = Time.time;
            _nextDebugLogTime = 0f;

            if (_root == null)
            {
                CreateObjects(_radius, _coreRadius);
            }

            if (_root != null)
            {
                _root.SetActive(true);
                _root.transform.position = corePosition;
                _root.transform.localScale = Vector3.one;
            }

            UpdateEffect(corePosition, _radius, _coreRadius, 0f, null);

            MelonLogger.Msg("[" + VersionTag + "] Show. Pos=" + FormatVector(corePosition) +
                            ", Radius=" + _radius.ToString("0.00") +
                            ", CoreRadius=" + _coreRadius.ToString("0.00") +
                            ", Renderers=" + CountEnabledRenderers(_root) +
                            ", Lines=" + CountEnabledLines() +
                            ", Cam=" + CameraInfo());
        }

        public void Hide()
        {
            _visible = false;

            if (_root != null)
            {
                _root.SetActive(false);
            }

            HideTargetLines();
            HideLightningLines();
            MelonLogger.Msg("[" + VersionTag + "] Hide.");
        }

        public void Destroy()
        {
            SafeDestroy(_root);
            _root = null;
            _coreSphere = null;

            _coreRings.Clear();
            _outerRings.Clear();
            _vortexLines.Clear();
            _lightningLines.Clear();
            _targetLines.Clear();
            _particles.Clear();
            _particleSeeds.Clear();

            SafeDestroy(_coreMaterial);
            SafeDestroy(_ringMaterial);
            SafeDestroy(_lineMaterial);
            SafeDestroy(_lightningMaterial);
            SafeDestroy(_particleMaterial);

            _coreMaterial = null;
            _ringMaterial = null;
            _lineMaterial = null;
            _lightningMaterial = null;
            _particleMaterial = null;
        }

        public void UpdateEffect(Vector3 corePosition, float radius, float coreRadius, float normalizedTime, IList<Vector3> targetPositions)
        {
            if (!_initialized || !_visible)
            {
                return;
            }

            _radius = Mathf.Max(0.5f, radius);
            _coreRadius = Mathf.Max(0.25f, coreRadius);

            if (_root == null)
            {
                CreateObjects(_radius, _coreRadius);
            }

            if (_root == null)
            {
                return;
            }

            _root.SetActive(true);
            _root.transform.position = corePosition;

            int targetCount = targetPositions != null ? targetPositions.Count : 0;
            float activeAge = Mathf.Max(0f, Time.time - _shownTime);
            float pulse = 1.0f + Mathf.Sin(Time.time * 11.0f) * 0.08f + Mathf.Sin(Time.time * 23.0f) * 0.025f;
            _root.transform.localScale = Vector3.one * pulse;

            UpdateCore(coreRadius, normalizedTime, targetCount);
            UpdateCoreRings(coreRadius, normalizedTime);
            UpdateOuterRings(normalizedTime);
            UpdateVortexLines(normalizedTime, activeAge);
            UpdateParticles(normalizedTime, activeAge);
            UpdateTargetLines(corePosition, targetPositions);
            UpdateLightningLines(corePosition, targetPositions, activeAge);
            UpdateMaterials(normalizedTime, targetCount);

            if (Time.time >= _nextDebugLogTime)
            {
                _nextDebugLogTime = Time.time + 1.0f;
                MelonLogger.Msg("[" + VersionTag + "] VisibleTick. Pos=" + FormatVector(corePosition) +
                                ", Active=" + (_root != null && _root.activeInHierarchy) +
                                ", Renderers=" + CountEnabledRenderers(_root) +
                                ", Lines=" + CountEnabledLines() +
                                ", Targets=" + targetCount +
                                ", Cam=" + CameraInfo());
            }
        }

        private void CreateObjects(float radius, float coreRadius)
        {
            _radius = Mathf.Max(0.5f, radius);
            _coreRadius = Mathf.Max(0.25f, coreRadius);

            _root = new GameObject("GojoBlue_EffectRoot_v7_LightningVortex");
            _root.SetActive(false);

            // v6で見えた方式を維持。Material生成処理はInfinityBarrierEffect系。
            _coreMaterial = CreateTransparentMaterial("GojoBlue_v7_Core_Mat", new Color(0.05f, 0.50f, 1.0f, 0.50f));
            _ringMaterial = CreateTransparentMaterial("GojoBlue_v7_Ring_Mat", new Color(0.55f, 0.95f, 1.0f, 0.95f));
            _lineMaterial = CreateTransparentMaterial("GojoBlue_v7_SuctionLine_Mat", new Color(0.35f, 0.88f, 1.0f, 0.82f));
            _lightningMaterial = CreateTransparentMaterial("GojoBlue_v7_Lightning_Mat", new Color(0.82f, 0.98f, 1.0f, 1.0f));
            _particleMaterial = CreateTransparentMaterial("GojoBlue_v7_Particle_Mat", new Color(0.75f, 0.98f, 1.0f, 0.95f));

            CreateCoreSphere(_coreRadius);
            CreateCoreRings(_coreRadius);
            CreateOuterRings();
            CreateVortexLines();
            CreateLightningLines();
            CreateTargetLines();
            CreateParticles();

            MelonLogger.Msg("[" + VersionTag + "] Created. ShaderCore=" + ShaderName(_coreMaterial) +
                            ", ShaderRing=" + ShaderName(_ringMaterial) +
                            ", ShaderLightning=" + ShaderName(_lightningMaterial) +
                            ", Renderers=" + CountEnabledRenderers(_root) +
                            ", Lines=" + CountEnabledLines());
        }

        private void CreateCoreSphere(float coreRadius)
        {
            _coreSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            if (_coreSphere == null)
            {
                return;
            }

            _coreSphere.name = "GojoBlue_v7_BlackBlueCore";
            _coreSphere.transform.SetParent(_root.transform, false);
            _coreSphere.transform.localPosition = Vector3.zero;
            _coreSphere.transform.localRotation = Quaternion.identity;
            _coreSphere.transform.localScale = Vector3.one * (coreRadius * 2.0f);

            Collider col = _coreSphere.GetComponent<Collider>();
            if (col != null)
            {
                SafeDestroy(col);
            }

            Renderer renderer = _coreSphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = _coreMaterial;
            }
        }

        private void CreateCoreRings(float coreRadius)
        {
            for (int i = 0; i < CoreRingCount; i++)
            {
                GameObject obj = new GameObject("GojoBlue_v7_CoreLightningRing_" + i);
                obj.transform.SetParent(_root.transform, false);
                obj.transform.localPosition = Vector3.zero;
                obj.transform.localRotation = Quaternion.identity;

                LineRenderer lr = obj.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.loop = true;
                lr.positionCount = RingSegments;
                lr.widthMultiplier = 0.034f;
                lr.material = i == 0 ? _lightningMaterial : _ringMaterial;

                TrySetLineCaps(lr);
                _coreRings.Add(lr);
            }
        }

        private void CreateOuterRings()
        {
            for (int i = 0; i < OuterRingCount; i++)
            {
                GameObject obj = new GameObject("GojoBlue_v7_OuterSuctionRing_" + i);
                obj.transform.SetParent(_root.transform, false);
                obj.transform.localPosition = Vector3.zero;
                obj.transform.localRotation = Quaternion.identity;

                LineRenderer lr = obj.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.loop = true;
                lr.positionCount = RingSegments;
                lr.widthMultiplier = 0.020f;
                lr.material = _ringMaterial;

                TrySetLineCaps(lr);
                _outerRings.Add(lr);
            }
        }

        private void CreateVortexLines()
        {
            for (int i = 0; i < VortexLineCount; i++)
            {
                GameObject obj = new GameObject("GojoBlue_v7_InwardVortexLine_" + i);
                obj.transform.SetParent(_root.transform, false);
                obj.transform.localPosition = Vector3.zero;
                obj.transform.localRotation = Quaternion.identity;

                LineRenderer lr = obj.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.loop = false;
                lr.positionCount = VortexLineSegments;
                lr.widthMultiplier = 0.019f;
                lr.material = _lineMaterial;

                TrySetLineCaps(lr);
                _vortexLines.Add(lr);
            }
        }

        private void CreateLightningLines()
        {
            for (int i = 0; i < LightningLineCount; i++)
            {
                GameObject obj = new GameObject("GojoBlue_v7_JaggedLightning_" + i);
                obj.transform.SetParent(_root.transform, true);

                LineRenderer lr = obj.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.loop = false;
                lr.positionCount = LightningSegments;
                lr.widthMultiplier = 0.032f;
                lr.material = _lightningMaterial;
                obj.SetActive(false);

                TrySetLineCaps(lr);
                _lightningLines.Add(lr);
            }
        }

        private void CreateTargetLines()
        {
            for (int i = 0; i < MaxTargetLineCount; i++)
            {
                GameObject obj = new GameObject("GojoBlue_v7_TargetSuctionLine_" + i);
                obj.transform.SetParent(_root.transform, true);

                LineRenderer lr = obj.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.loop = false;
                lr.positionCount = 3;
                lr.widthMultiplier = 0.020f;
                lr.material = _lineMaterial;
                obj.SetActive(false);

                TrySetLineCaps(lr);
                _targetLines.Add(lr);
            }
        }

        private void CreateParticles()
        {
            for (int i = 0; i < ParticleCount; i++)
            {
                GameObject p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                if (p == null)
                {
                    continue;
                }

                p.name = "GojoBlue_v7_InwardParticle_" + i;
                p.transform.SetParent(_root.transform, false);
                p.transform.localScale = Vector3.one * 0.07f;

                Collider col = p.GetComponent<Collider>();
                if (col != null)
                {
                    SafeDestroy(col);
                }

                Renderer renderer = p.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material = _particleMaterial;
                }

                Vector3 seed = UnityEngine.Random.onUnitSphere;
                if (seed.sqrMagnitude < 0.001f)
                {
                    seed = Vector3.up;
                }

                _particles.Add(p);
                _particleSeeds.Add(seed.normalized);
            }
        }

        private void UpdateCore(float coreRadius, float normalizedTime, int targetCount)
        {
            if (_coreSphere == null)
            {
                return;
            }

            float hitBoost = targetCount > 0 ? 0.16f : 0.0f;
            float pulse = 1.0f + Mathf.Sin(Time.time * 13.0f) * (0.12f + hitBoost) + Mathf.Sin(Time.time * 31.0f) * 0.035f;
            _coreSphere.transform.localScale = Vector3.one * (coreRadius * 2.0f * pulse);
        }

        private void UpdateCoreRings(float coreRadius, float normalizedTime)
        {
            for (int i = 0; i < _coreRings.Count; i++)
            {
                LineRenderer lr = _coreRings[i];
                if (lr == null)
                {
                    continue;
                }

                float r = coreRadius * (1.10f + i * 0.30f) + Mathf.Sin(Time.time * 9.0f + i) * 0.05f;
                float twist = Time.time * (1.15f + i * 0.35f) * (i % 2 == 0 ? 1f : -1f);
                RingPlane plane = (RingPlane)(i % 3);
                SetNoisyRingPositions(lr, r, plane, twist, 0.018f + i * 0.006f, i * 19.17f);
                lr.widthMultiplier = i == 0 ? 0.045f : 0.030f;
            }
        }

        private void UpdateOuterRings(float normalizedTime)
        {
            // 外周リングは半径が内側へ収縮していくように見せる。
            float baseRadius = Mathf.Clamp(_radius * 0.38f, 1.25f, 2.65f);
            float inward = Mathf.Repeat(Time.time * 0.85f, 1.0f);

            for (int i = 0; i < _outerRings.Count; i++)
            {
                LineRenderer lr = _outerRings[i];
                if (lr == null)
                {
                    continue;
                }

                float lane = (i / (float)Mathf.Max(1, OuterRingCount - 1));
                float collapse = Mathf.Repeat(lane + inward, 1.0f);
                float r = Mathf.Lerp(baseRadius, _coreRadius * 1.7f, collapse);
                float phase = -Time.time * (0.55f + i * 0.12f);
                RingPlane plane = (RingPlane)(i % 3);
                SetNoisyRingPositions(lr, r, plane, phase, 0.030f, i * 12.7f);
                lr.widthMultiplier = Mathf.Lerp(0.030f, 0.012f, collapse);
            }
        }

        private void UpdateVortexLines(float normalizedTime, float activeAge)
        {
            float maxR = Mathf.Clamp(_radius * 0.42f, 1.45f, 2.95f);
            float minR = Mathf.Max(_coreRadius * 0.55f, 0.25f);

            for (int i = 0; i < _vortexLines.Count; i++)
            {
                LineRenderer lr = _vortexLines[i];
                if (lr == null)
                {
                    continue;
                }

                float baseAngle = (Mathf.PI * 2f * i) / Mathf.Max(1, _vortexLines.Count);
                float spin = -Time.time * (3.1f + (i % 4) * 0.28f);
                float verticalLane = ((i % 5) - 2) * 0.16f;

                for (int s = 0; s < VortexLineSegments; s++)
                {
                    float t = s / (float)(VortexLineSegments - 1);
                    float inward = Mathf.Pow(t, 1.25f);
                    float r = Mathf.Lerp(maxR, minR, inward);
                    float angle = baseAngle + spin + t * Mathf.PI * 2.6f;
                    float wobble = Mathf.Sin(Time.time * 6.0f + i * 1.7f + s * 0.71f) * 0.055f;
                    float y = Mathf.Lerp(verticalLane, 0f, inward) + Mathf.Sin(t * Mathf.PI * 2.0f + activeAge * 5.0f + i) * 0.08f;

                    Vector3 p = new Vector3(Mathf.Cos(angle) * (r + wobble), y, Mathf.Sin(angle) * (r + wobble));
                    lr.SetPosition(s, p);
                }

                lr.widthMultiplier = 0.016f + Mathf.Abs(Mathf.Sin(Time.time * 5.5f + i)) * 0.014f;
            }
        }

        private void UpdateParticles(float normalizedTime, float activeAge)
        {
            float maxR = Mathf.Clamp(_radius * 0.37f, 1.20f, 2.55f);
            float minR = Mathf.Max(_coreRadius * 0.22f, 0.12f);

            for (int i = 0; i < _particles.Count; i++)
            {
                GameObject p = _particles[i];
                if (p == null)
                {
                    continue;
                }

                Vector3 seed = i < _particleSeeds.Count ? _particleSeeds[i] : Vector3.up;
                float lane = Mathf.Repeat(activeAge * (0.82f + (i % 6) * 0.08f) + i * 0.137f, 1.0f);
                float r = Mathf.Lerp(maxR, minR, lane);
                float angle = Time.time * (2.6f + (i % 5) * 0.22f) + i * 0.61f;
                Quaternion rot = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(angle * 43.0f, Vector3.right);
                Vector3 spiral = rot * seed * r;
                spiral.y *= 0.55f;

                p.transform.localPosition = spiral;

                float s = Mathf.Lerp(0.075f, 0.025f, lane) + Mathf.Abs(Mathf.Sin(Time.time * 10.0f + i)) * 0.018f;
                p.transform.localScale = Vector3.one * s;
            }
        }

        private void UpdateTargetLines(Vector3 corePosition, IList<Vector3> targetPositions)
        {
            int used = 0;

            if (targetPositions != null)
            {
                int count = Mathf.Min(targetPositions.Count, _targetLines.Count);
                for (int i = 0; i < count; i++)
                {
                    LineRenderer lr = _targetLines[i];
                    if (lr == null)
                    {
                        continue;
                    }

                    Vector3 target = targetPositions[i];
                    Vector3 dir = corePosition - target;
                    Vector3 side = Vector3.Cross(dir.normalized, Vector3.up);
                    if (side.sqrMagnitude < 0.001f)
                    {
                        side = Vector3.Cross(dir.normalized, Vector3.right);
                    }
                    side = side.normalized;

                    float wiggle = Mathf.Sin(Time.time * 9.0f + i * 1.31f) * 0.10f;
                    Vector3 mid = Vector3.Lerp(target, corePosition, 0.58f) + side * wiggle;

                    lr.gameObject.SetActive(true);
                    lr.positionCount = 3;
                    lr.SetPosition(0, target);
                    lr.SetPosition(1, mid);
                    lr.SetPosition(2, corePosition);
                    lr.widthMultiplier = 0.014f + Mathf.Abs(Mathf.Sin(Time.time * 8.0f + i)) * 0.020f;
                    used++;
                }
            }

            for (int i = used; i < _targetLines.Count; i++)
            {
                if (_targetLines[i] != null)
                {
                    _targetLines[i].gameObject.SetActive(false);
                }
            }
        }

        private void UpdateLightningLines(Vector3 corePosition, IList<Vector3> targetPositions, float activeAge)
        {
            int used = 0;
            int targetCount = targetPositions != null ? targetPositions.Count : 0;

            if (targetPositions != null && targetCount > 0)
            {
                int count = Mathf.Min(targetCount, _lightningLines.Count);
                for (int i = 0; i < count; i++)
                {
                    Vector3 start = targetPositions[i];
                    SetJaggedLightning(_lightningLines[i], start, corePosition, i, true);
                    used++;
                }
            }

            // 対象が少ない時でもコア周囲に稲妻を出す。動画的に「蒼が発生している」ことが分かりやすい。
            while (used < _lightningLines.Count)
            {
                float angle = (Mathf.PI * 2.0f * used) / Mathf.Max(1, _lightningLines.Count) + Time.time * (1.6f + used * 0.03f);
                float r = Mathf.Clamp(_radius * 0.28f, 0.85f, 1.85f) + Mathf.Sin(Time.time * 4.0f + used) * 0.18f;
                float y = Mathf.Sin(Time.time * 3.0f + used * 0.77f) * 0.45f;
                Vector3 localStart = new Vector3(Mathf.Cos(angle) * r, y, Mathf.Sin(angle) * r);
                Vector3 start = corePosition + localStart;
                SetJaggedLightning(_lightningLines[used], start, corePosition, used, used < 7);
                used++;
            }
        }

        private void SetJaggedLightning(LineRenderer lr, Vector3 start, Vector3 end, int seed, bool active)
        {
            if (lr == null)
            {
                return;
            }

            lr.gameObject.SetActive(active);
            if (!active)
            {
                return;
            }

            lr.positionCount = LightningSegments;

            Vector3 dir = end - start;
            Vector3 sideA = Vector3.Cross(dir.normalized, Vector3.up);
            if (sideA.sqrMagnitude < 0.001f)
            {
                sideA = Vector3.Cross(dir.normalized, Vector3.right);
            }
            sideA = sideA.normalized;
            Vector3 sideB = Vector3.Cross(dir.normalized, sideA).normalized;

            float length = Mathf.Max(0.1f, dir.magnitude);
            float amp = Mathf.Clamp(length * 0.08f, 0.035f, 0.22f);
            float flicker = Mathf.Abs(Mathf.Sin(Time.time * 18.0f + seed * 2.17f));

            for (int s = 0; s < LightningSegments; s++)
            {
                float t = s / (float)(LightningSegments - 1);
                Vector3 p = Vector3.Lerp(start, end, t);
                if (s != 0 && s != LightningSegments - 1)
                {
                    float n1 = Mathf.Sin(Time.time * 24.0f + seed * 12.989f + s * 78.233f);
                    float n2 = Mathf.Cos(Time.time * 19.0f + seed * 4.113f + s * 37.719f);
                    p += sideA * n1 * amp + sideB * n2 * amp * 0.65f;
                }
                lr.SetPosition(s, p);
            }

            lr.widthMultiplier = Mathf.Lerp(0.018f, 0.060f, flicker);
        }

        private void HideTargetLines()
        {
            for (int i = 0; i < _targetLines.Count; i++)
            {
                if (_targetLines[i] != null)
                {
                    _targetLines[i].gameObject.SetActive(false);
                }
            }
        }

        private void HideLightningLines()
        {
            for (int i = 0; i < _lightningLines.Count; i++)
            {
                if (_lightningLines[i] != null)
                {
                    _lightningLines[i].gameObject.SetActive(false);
                }
            }
        }

        private void UpdateMaterials(float normalizedTime, int targetCount)
        {
            float pulse = Mathf.Abs(Mathf.Sin(Time.time * 8.0f));
            float fast = Mathf.Abs(Mathf.Sin(Time.time * 24.0f));
            float hitBoost = targetCount > 0 ? 0.20f : 0.0f;

            Color core = new Color(0.04f + hitBoost, 0.42f + hitBoost, 1.0f, Mathf.Lerp(0.38f, 0.68f, pulse));
            Color ring = new Color(0.50f, 0.94f, 1.0f, Mathf.Lerp(0.55f, 1.0f, pulse));
            Color line = new Color(0.32f, 0.88f, 1.0f, Mathf.Lerp(0.42f, 0.88f, pulse));
            Color lightning = new Color(0.78f, 0.98f, 1.0f, Mathf.Lerp(0.58f, 1.0f, fast));
            Color particle = new Color(0.70f, 0.98f, 1.0f, Mathf.Lerp(0.55f, 1.0f, pulse));

            SetMaterialColor(_coreMaterial, core);
            SetMaterialColor(_ringMaterial, ring);
            SetMaterialColor(_lineMaterial, line);
            SetMaterialColor(_lightningMaterial, lightning);
            SetMaterialColor(_particleMaterial, particle);
        }

        private void SetNoisyRingPositions(LineRenderer lr, float radius, RingPlane plane, float phase, float noise, float seed)
        {
            if (lr == null)
            {
                return;
            }

            for (int i = 0; i < RingSegments; i++)
            {
                float angle = (Mathf.PI * 2.0f * i) / RingSegments + phase;
                float jitter = Mathf.Sin(angle * 9.0f + Time.time * 5.0f + seed) * noise +
                               Mathf.Sin(angle * 17.0f - Time.time * 9.0f + seed * 0.37f) * noise * 0.45f;
                float r = radius + jitter;
                float x = Mathf.Cos(angle) * r;
                float y = Mathf.Sin(angle) * r;

                Vector3 p;
                switch (plane)
                {
                    case RingPlane.XZ:
                        p = new Vector3(x, Mathf.Sin(angle * 3.0f + seed) * noise * 2.0f, y);
                        break;

                    case RingPlane.YZ:
                        p = new Vector3(Mathf.Sin(angle * 3.0f + seed) * noise * 2.0f, x, y);
                        break;

                    default:
                        p = new Vector3(x, y, Mathf.Sin(angle * 3.0f + seed) * noise * 2.0f);
                        break;
                }

                lr.SetPosition(i, p);
            }
        }

        private void TrySetLineCaps(LineRenderer lr)
        {
            if (lr == null)
            {
                return;
            }

            try
            {
                lr.numCornerVertices = 4;
                lr.numCapVertices = 4;
            }
            catch { }
        }

        private Material CreateTransparentMaterial(string name, Color color)
        {
            Shader shader = null;

            try
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            catch
            {
                shader = null;
            }

            if (shader == null)
            {
                try
                {
                    shader = Shader.Find("Sprites/Default");
                }
                catch
                {
                    shader = null;
                }
            }

            if (shader == null)
            {
                try
                {
                    shader = Shader.Find("Standard");
                }
                catch
                {
                    shader = null;
                }
            }

            Material mat = shader != null ? new Material(shader) : new Material(Shader.Find("Diffuse"));
            mat.name = name;
            mat.color = color;
            mat.renderQueue = 3000;

            TrySetMaterialFloat(mat, "_Surface", 1.0f);
            TrySetMaterialFloat(mat, "_Blend", 0.0f);
            TrySetMaterialFloat(mat, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            TrySetMaterialFloat(mat, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            TrySetMaterialFloat(mat, "_ZWrite", 0.0f);
            TrySetMaterialFloat(mat, "_Mode", 3.0f);

            TryEnableKeyword(mat, "_SURFACE_TYPE_TRANSPARENT");
            TryEnableKeyword(mat, "_ALPHABLEND_ON");
            TryEnableKeyword(mat, "_ALPHAPREMULTIPLY_ON");

            TrySetMaterialColor(mat, "_BaseColor", color);
            TrySetMaterialColor(mat, "_Color", color);

            return mat;
        }

        private void SetMaterialColor(Material mat, Color color)
        {
            if (mat == null)
            {
                return;
            }

            try
            {
                mat.color = color;
            }
            catch { }

            TrySetMaterialColor(mat, "_BaseColor", color);
            TrySetMaterialColor(mat, "_Color", color);
        }

        private void TrySetMaterialFloat(Material mat, string propertyName, float value)
        {
            try
            {
                if (mat != null && mat.HasProperty(propertyName))
                {
                    mat.SetFloat(propertyName, value);
                }
            }
            catch { }
        }

        private void TrySetMaterialColor(Material mat, string propertyName, Color value)
        {
            try
            {
                if (mat != null && mat.HasProperty(propertyName))
                {
                    mat.SetColor(propertyName, value);
                }
            }
            catch { }
        }

        private void TryEnableKeyword(Material mat, string keyword)
        {
            try
            {
                if (mat != null)
                {
                    mat.EnableKeyword(keyword);
                }
            }
            catch { }
        }

        private string ShaderName(Material mat)
        {
            try
            {
                if (mat != null && mat.shader != null)
                {
                    return mat.shader.name;
                }
            }
            catch { }

            return "null";
        }

        private int CountEnabledRenderers(GameObject root)
        {
            if (root == null)
            {
                return 0;
            }

            try
            {
                Renderer[] rs = root.GetComponentsInChildren<Renderer>(true);
                int count = 0;
                for (int i = 0; i < rs.Length; i++)
                {
                    if (rs[i] != null && rs[i].enabled)
                    {
                        count++;
                    }
                }

                return count;
            }
            catch
            {
                return -1;
            }
        }

        private int CountEnabledLines()
        {
            int count = 0;
            for (int i = 0; i < _coreRings.Count; i++) if (_coreRings[i] != null && _coreRings[i].enabled) count++;
            for (int i = 0; i < _outerRings.Count; i++) if (_outerRings[i] != null && _outerRings[i].enabled) count++;
            for (int i = 0; i < _vortexLines.Count; i++) if (_vortexLines[i] != null && _vortexLines[i].enabled) count++;
            for (int i = 0; i < _lightningLines.Count; i++) if (_lightningLines[i] != null && _lightningLines[i].enabled && _lightningLines[i].gameObject.activeSelf) count++;
            for (int i = 0; i < _targetLines.Count; i++) if (_targetLines[i] != null && _targetLines[i].enabled && _targetLines[i].gameObject.activeSelf) count++;
            return count;
        }

        private string CameraInfo()
        {
            try
            {
                Camera cam = Camera.main;
                if (cam == null)
                {
                    return "null";
                }

                return cam.name + "/mask=" + cam.cullingMask + "/pos=" + FormatVector(cam.transform.position);
            }
            catch
            {
                return "error";
            }
        }

        private string FormatVector(Vector3 v)
        {
            return "(" + v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ", " + v.z.ToString("0.00") + ")";
        }

        private void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return;
            }

            try
            {
                UnityEngine.Object.Destroy(obj);
            }
            catch { }
        }

        private enum RingPlane
        {
            XY,
            XZ,
            YZ
        }
    }
}
