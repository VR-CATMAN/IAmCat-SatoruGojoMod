using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace IAmCatGojoMod
{
    /// <summary>
    /// 虚式「茈」の見た目担当。
    ///
    /// PurpleEffect_v2_ClearRedBlueMerge:
    /// - BlueEffect_v6/v7で成功した「Show時に初めてCreateObjects」方式
    /// - チャージ中: 青球 + 赤球 + 中央紫球 + 紫稲妻
    /// - 発射中: 大きい紫球 + 螺旋リング + 吸引ライン + 削除スパーク
    /// - 終端: 紫フラッシュ + 衝撃波リング + 放射稲妻
    /// </summary>
    public sealed class PurpleEffect
    {
        private const string VersionTag = "PurpleEffect_v2_ClearRedBlueMerge";

        private const int RingSegments = 96;
        private const int SpiralCount = 4;
        private const int LightningCount = 18;
        private const int PullLineCount = 36;
        private const int SparkMaxCount = 100;
        private const float SparkLifeSeconds = 0.55f;

        private GameObject _root;
        private GameObject _purpleCore;
        private GameObject _blueOrb;
        private GameObject _redOrb;
        private GameObject _flashSphere;

        private readonly List<LineRenderer> _rings = new List<LineRenderer>();
        private readonly List<LineRenderer> _spirals = new List<LineRenderer>();
        private readonly List<LineRenderer> _lightnings = new List<LineRenderer>();
        private readonly List<LineRenderer> _pullLines = new List<LineRenderer>();
        private readonly List<Spark> _sparks = new List<Spark>();

        private Material _purpleMaterial;
        private Material _purpleCoreMaterial;
        private Material _blueMaterial;
        private Material _redMaterial;
        private Material _ringMaterial;
        private Material _lightningMaterial;
        private Material _sparkMaterial;

        private bool _initialized;
        private bool _visible;
        private float _radius = 1.0f;
        private float _nextTickLogTime;

        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            MelonLogger.Msg("[" + VersionTag + "] Initialized. DeferredCreate=True");
        }

        public void ShowCharge(Vector3 center, Vector3 forward, float progress, float radius)
        {
            EnsureCreated(radius);
            _visible = true;
            _radius = radius;

            if (_root != null)
            {
                _root.SetActive(true);
                _root.transform.position = center;
                _root.transform.rotation = Quaternion.LookRotation(SafeNormalize(forward, Vector3.forward), Vector3.up);
            }

            SetModeCharge(true);
            UpdateCharge(center, forward, progress, radius);

            MelonLogger.Msg("[" + VersionTag + "] ChargeShow. Pos=" + FormatVector(center) +
                            ", Radius=" + radius.ToString("0.00") +
                            ", Renderers=" + CountRenderers() +
                            ", Lines=" + CountLines());
        }

        public void UpdateCharge(Vector3 center, Vector3 forward, float progress, float radius)
        {
            if (!_visible)
            {
                return;
            }

            EnsureCreated(radius);
            _radius = radius;

            if (_root != null)
            {
                _root.transform.position = center;
                _root.transform.rotation = Quaternion.LookRotation(SafeNormalize(forward, Vector3.forward), Vector3.up);
            }

            SetModeCharge(true);

            float t = Mathf.Clamp01(progress);

            // v2: 青球と赤球が「左右から混ざり合って紫になる」流れを見せるため、
            // ランダムな軌道ではなく、左右の大きな球が中央へ寄る動きに変更。
            float gather = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01((t - 0.10f) / 0.70f));
            float separation = Mathf.Lerp(radius * 1.65f, radius * 0.10f, gather);
            float swirl = Time.time * 8.0f;
            float wobble = Mathf.Sin(swirl) * Mathf.Lerp(0.18f, 0.02f, t);
            float depthWobble = Mathf.Cos(swirl * 0.83f) * Mathf.Lerp(0.18f, 0.01f, t);

            Vector3 bluePos = new Vector3(-separation, wobble, depthWobble);
            Vector3 redPos = new Vector3(separation, -wobble, -depthWobble);

            float orbScale = Mathf.Lerp(0.62f, 0.18f, Mathf.Clamp01((t - 0.55f) / 0.35f));
            float orbPulse = 1.0f + Mathf.Sin(Time.time * 18.0f) * 0.08f;

            if (_blueOrb != null)
            {
                _blueOrb.transform.localPosition = bluePos;
                _blueOrb.transform.localScale = Vector3.one * orbScale * orbPulse;
            }
            if (_redOrb != null)
            {
                _redOrb.transform.localPosition = redPos;
                _redOrb.transform.localScale = Vector3.one * orbScale * orbPulse;
            }
            if (_purpleCore != null)
            {
                float purpleGrow = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01((t - 0.35f) / 0.55f));
                float pulse = 1.0f + Mathf.Sin(Time.time * 16.0f) * 0.14f;
                _purpleCore.transform.localPosition = Vector3.zero;
                _purpleCore.transform.localScale = Vector3.one * Mathf.Lerp(0.10f, radius * 2.05f, purpleGrow) * pulse;
            }

            if (_flashSphere != null)
            {
                bool fusionFlash = t > 0.62f;
                _flashSphere.SetActive(fusionFlash);
                if (fusionFlash)
                {
                    float ft = Mathf.Clamp01((t - 0.62f) / 0.38f);
                    _flashSphere.transform.localPosition = Vector3.zero;
                    _flashSphere.transform.localScale = Vector3.one * Mathf.Lerp(radius * 0.55f, radius * 2.55f, ft);
                }
            }

            UpdateRings(radius * Mathf.Lerp(0.35f, 1.28f, t), 0.055f + t * 0.055f, Time.time * 3.0f);
            UpdateChargeLightning(radius, t, bluePos, redPos);
            UpdateSparks();
        }

        public void ShowProjectile(Vector3 position, Vector3 forward, float radius)
        {
            EnsureCreated(radius);
            _visible = true;
            _radius = radius;

            if (_root != null)
            {
                _root.SetActive(true);
                _root.transform.position = position;
                _root.transform.rotation = Quaternion.LookRotation(SafeNormalize(forward, Vector3.forward), Vector3.up);
            }

            SetModeProjectile();
            UpdateProjectile(position, forward, 0f, null, null);

            MelonLogger.Msg("[" + VersionTag + "] ProjectileShow. Pos=" + FormatVector(position) +
                            ", Radius=" + radius.ToString("0.00") +
                            ", Renderers=" + CountRenderers() +
                            ", Lines=" + CountLines());
        }

        public void UpdateProjectile(Vector3 position, Vector3 forward, float normalizedLife, List<Vector3> targetPositions, List<Vector3> erasedPositions)
        {
            if (!_visible)
            {
                return;
            }

            EnsureCreated(_radius);

            if (_root != null)
            {
                _root.transform.position = position;
                _root.transform.rotation = Quaternion.LookRotation(SafeNormalize(forward, Vector3.forward), Vector3.up);
            }

            SetModeProjectile();

            float pulse = 1.0f + Mathf.Sin(Time.time * 18.0f) * 0.08f;
            if (_purpleCore != null)
            {
                _purpleCore.transform.localPosition = Vector3.zero;
                _purpleCore.transform.localScale = Vector3.one * (_radius * 2.0f * pulse);
            }

            UpdateRings(_radius * (1.05f + Mathf.Sin(Time.time * 7.0f) * 0.08f), 0.07f, Time.time * 7.5f);
            UpdateSpirals(_radius, Time.time * 8.0f);
            UpdateProjectileLightning(_radius);
            UpdatePullLines(position, targetPositions);

            if (erasedPositions != null)
            {
                for (int i = 0; i < erasedPositions.Count; i++)
                {
                    SpawnEraseBurst(erasedPositions[i]);
                }
            }

            UpdateSparks();
            LogVisibleTick(position, targetPositions != null ? targetPositions.Count : 0);
        }

        public void ShowImpact(Vector3 center, float radius, List<Vector3> targetPositions)
        {
            EnsureCreated(radius);
            _visible = true;
            _radius = radius;

            if (_root != null)
            {
                _root.SetActive(true);
                _root.transform.position = center;
                _root.transform.rotation = Quaternion.identity;
            }

            SetModeImpact();
            UpdateImpact(center, radius, 0f, targetPositions);

            MelonLogger.Msg("[" + VersionTag + "] ImpactShow. Pos=" + FormatVector(center) +
                            ", Radius=" + radius.ToString("0.00") +
                            ", Renderers=" + CountRenderers() +
                            ", Lines=" + CountLines());
        }

        public void UpdateImpact(Vector3 center, float radius, float normalized, List<Vector3> targetPositions)
        {
            if (!_visible)
            {
                return;
            }

            EnsureCreated(radius);
            _radius = radius;

            if (_root != null)
            {
                _root.transform.position = center;
                _root.transform.rotation = Quaternion.identity;
            }

            SetModeImpact();

            float t = Mathf.Clamp01(normalized);
            float flashScale = Mathf.Lerp(1.2f, radius * 2.0f, t);
            if (_flashSphere != null)
            {
                _flashSphere.transform.localScale = Vector3.one * flashScale;
            }
            if (_purpleCore != null)
            {
                _purpleCore.transform.localScale = Vector3.one * Mathf.Lerp(radius * 1.5f, 0.1f, t);
            }

            UpdateRings(Mathf.Lerp(0.4f, radius, t), Mathf.Lerp(0.12f, 0.02f, t), Time.time * 5.0f);
            UpdateImpactLightning(radius, t);
            UpdatePullLines(center, targetPositions);
            UpdateSparks();
            LogVisibleTick(center, targetPositions != null ? targetPositions.Count : 0);
        }

        public void SpawnEraseBurst(Vector3 position)
        {
            if (!_visible)
            {
                return;
            }

            EnsureCreated(_radius);

            for (int i = 0; i < 4; i++)
            {
                if (_sparks.Count >= SparkMaxCount)
                {
                    RemoveOldestSpark();
                }

                GameObject spark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                if (spark == null)
                {
                    continue;
                }

                spark.name = "GojoPurple_EraseSpark";
                spark.transform.position = position + UnityEngine.Random.insideUnitSphere * 0.18f;
                spark.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.045f, 0.11f);

                Collider col = spark.GetComponent<Collider>();
                if (col != null)
                {
                    SafeDestroy(col);
                }

                Renderer renderer = spark.GetComponent<Renderer>();
                if (renderer != null)
                {
                    if (_sparkMaterial == null)
                    {
                        _sparkMaterial = CreateTransparentMaterial("GojoPurple_Spark_Mat", new Color(0.96f, 0.35f, 1.0f, 0.92f));
                    }
                    renderer.material = _sparkMaterial;
                }

                _sparks.Add(new Spark
                {
                    Object = spark,
                    SpawnTime = Time.time,
                    LifeSeconds = SparkLifeSeconds,
                    Velocity = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(0.25f, 0.75f),
                    InitialScale = spark.transform.localScale.x
                });
            }
        }

        public void Hide()
        {
            _visible = false;

            if (_root != null)
            {
                _root.SetActive(false);
            }

            ClearSparks();
            MelonLogger.Msg("[" + VersionTag + "] Hide.");
        }

        public void Destroy()
        {
            ClearSparks();
            SafeDestroy(_root);
            _root = null;
            _purpleCore = null;
            _blueOrb = null;
            _redOrb = null;
            _flashSphere = null;

            SafeDestroy(_purpleMaterial);
            SafeDestroy(_purpleCoreMaterial);
            SafeDestroy(_blueMaterial);
            SafeDestroy(_redMaterial);
            SafeDestroy(_ringMaterial);
            SafeDestroy(_lightningMaterial);
            SafeDestroy(_sparkMaterial);
        }

        private void EnsureCreated(float radius)
        {
            if (_root != null)
            {
                return;
            }

            CreateObjects(radius);
        }

        private void CreateObjects(float radius)
        {
            _radius = Mathf.Max(0.3f, radius);
            _root = new GameObject("GojoPurple_Effect_Root");
            _root.SetActive(false);

            _purpleMaterial = CreateTransparentMaterial("GojoPurple_SoftPurple_Mat", new Color(0.72f, 0.18f, 1.0f, 0.48f));
            _purpleCoreMaterial = CreateTransparentMaterial("GojoPurple_Core_Mat", new Color(0.95f, 0.34f, 1.0f, 0.98f));
            _blueMaterial = CreateTransparentMaterial("GojoPurple_Blue_Mat", new Color(0.10f, 0.62f, 1.0f, 0.96f));
            _redMaterial = CreateTransparentMaterial("GojoPurple_Red_Mat", new Color(1.0f, 0.08f, 0.05f, 0.96f));
            _ringMaterial = CreateTransparentMaterial("GojoPurple_Ring_Mat", new Color(0.92f, 0.55f, 1.0f, 0.82f));
            _lightningMaterial = CreateTransparentMaterial("GojoPurple_Lightning_Mat", new Color(1.0f, 0.78f, 1.0f, 0.95f));

            _purpleCore = CreateSphere("GojoPurple_CoreOrb", _purpleCoreMaterial, Vector3.one * (_radius * 2.0f));
            _blueOrb = CreateSphere("GojoPurple_BlueOrb", _blueMaterial, Vector3.one * 0.55f);
            _redOrb = CreateSphere("GojoPurple_RedOrb", _redMaterial, Vector3.one * 0.55f);
            _flashSphere = CreateSphere("GojoPurple_FlashSphere", _purpleMaterial, Vector3.one * (_radius * 2.0f));

            CreateRings();
            CreateSpirals();
            CreateLightnings();
            CreatePullLines();

            MelonLogger.Msg("[" + VersionTag + "] Created. ShaderPurple=" + GetShaderName(_purpleMaterial) +
                            ", ShaderRing=" + GetShaderName(_ringMaterial) +
                            ", Renderers=" + CountRenderers() +
                            ", Lines=" + CountLines());
        }

        private GameObject CreateSphere(string name, Material material, Vector3 scale)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            if (sphere == null)
            {
                return null;
            }

            sphere.name = name;
            sphere.transform.SetParent(_root.transform, false);
            sphere.transform.localPosition = Vector3.zero;
            sphere.transform.localRotation = Quaternion.identity;
            sphere.transform.localScale = scale;

            Collider col = sphere.GetComponent<Collider>();
            if (col != null)
            {
                SafeDestroy(col);
            }

            Renderer renderer = sphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = material;
            }

            return sphere;
        }

        private void CreateRings()
        {
            _rings.Clear();
            _rings.Add(CreateRing("GojoPurple_Ring_XY", RingPlane.XY));
            _rings.Add(CreateRing("GojoPurple_Ring_XZ", RingPlane.XZ));
            _rings.Add(CreateRing("GojoPurple_Ring_YZ", RingPlane.YZ));
        }

        private LineRenderer CreateRing(string name, RingPlane plane)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(_root.transform, false);
            LineRenderer lr = obj.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = RingSegments;
            lr.widthMultiplier = 0.045f;
            lr.material = _ringMaterial;
            TrySetLineCaps(lr);

            for (int i = 0; i < RingSegments; i++)
            {
                float a = Mathf.PI * 2.0f * i / RingSegments;
                float x = Mathf.Cos(a);
                float y = Mathf.Sin(a);
                Vector3 p;
                switch (plane)
                {
                    case RingPlane.XZ:
                        p = new Vector3(x, 0f, y);
                        break;
                    case RingPlane.YZ:
                        p = new Vector3(0f, x, y);
                        break;
                    default:
                        p = new Vector3(x, y, 0f);
                        break;
                }
                lr.SetPosition(i, p);
            }
            return lr;
        }

        private void CreateSpirals()
        {
            _spirals.Clear();
            for (int i = 0; i < SpiralCount; i++)
            {
                GameObject obj = new GameObject("GojoPurple_Spiral_" + i);
                obj.transform.SetParent(_root.transform, false);
                LineRenderer lr = obj.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.loop = false;
                lr.positionCount = 64;
                lr.widthMultiplier = 0.035f;
                lr.material = _ringMaterial;
                TrySetLineCaps(lr);
                _spirals.Add(lr);
            }
        }

        private void CreateLightnings()
        {
            _lightnings.Clear();
            for (int i = 0; i < LightningCount; i++)
            {
                GameObject obj = new GameObject("GojoPurple_Lightning_" + i);
                obj.transform.SetParent(_root.transform, false);
                LineRenderer lr = obj.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.loop = false;
                lr.positionCount = 7;
                lr.widthMultiplier = 0.035f;
                lr.material = _lightningMaterial;
                TrySetLineCaps(lr);
                _lightnings.Add(lr);
            }
        }

        private void CreatePullLines()
        {
            _pullLines.Clear();
            for (int i = 0; i < PullLineCount; i++)
            {
                GameObject obj = new GameObject("GojoPurple_PullLine_" + i);
                obj.transform.SetParent(_root.transform, false);
                LineRenderer lr = obj.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.loop = false;
                lr.positionCount = 2;
                lr.widthMultiplier = 0.022f;
                lr.material = _lightningMaterial;
                TrySetLineCaps(lr);
                _pullLines.Add(lr);
            }
        }

        private void SetModeCharge(bool visible)
        {
            SafeSetActive(_purpleCore, true);
            SafeSetActive(_blueOrb, visible);
            SafeSetActive(_redOrb, visible);
            SafeSetActive(_flashSphere, false);
            SetLinesActive(_rings, true);
            SetLinesActive(_spirals, false);
            SetLinesActive(_lightnings, true);
            SetLinesActive(_pullLines, false);
        }

        private void SetModeProjectile()
        {
            SafeSetActive(_purpleCore, true);
            SafeSetActive(_blueOrb, false);
            SafeSetActive(_redOrb, false);
            SafeSetActive(_flashSphere, false);
            SetLinesActive(_rings, true);
            SetLinesActive(_spirals, true);
            SetLinesActive(_lightnings, true);
            SetLinesActive(_pullLines, true);
        }

        private void SetModeImpact()
        {
            SafeSetActive(_purpleCore, true);
            SafeSetActive(_blueOrb, false);
            SafeSetActive(_redOrb, false);
            SafeSetActive(_flashSphere, true);
            SetLinesActive(_rings, true);
            SetLinesActive(_spirals, false);
            SetLinesActive(_lightnings, true);
            SetLinesActive(_pullLines, true);
        }

        private void UpdateRings(float radius, float width, float rotation)
        {
            for (int i = 0; i < _rings.Count; i++)
            {
                LineRenderer lr = _rings[i];
                if (lr == null)
                {
                    continue;
                }

                Transform tr = lr.transform;
                tr.localScale = Vector3.one * radius;
                tr.localRotation = Quaternion.Euler(rotation * (i + 1) * 12.0f, rotation * (i + 2) * 9.0f, rotation * (i + 3) * 7.0f);
                lr.widthMultiplier = width;
            }
        }

        private void UpdateSpirals(float radius, float spin)
        {
            for (int s = 0; s < _spirals.Count; s++)
            {
                LineRenderer lr = _spirals[s];
                if (lr == null)
                {
                    continue;
                }

                int count = lr.positionCount;
                float phase = spin + s * Mathf.PI * 0.5f;
                for (int i = 0; i < count; i++)
                {
                    float t = (float)i / Mathf.Max(1, count - 1);
                    float a = phase + t * Mathf.PI * 4.0f;
                    float r = Mathf.Lerp(radius * 1.7f, radius * 0.35f, t);
                    float z = Mathf.Lerp(-radius * 2.4f, radius * 0.5f, t);
                    Vector3 p = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z);
                    lr.SetPosition(i, p);
                }
            }
        }

        private void UpdateChargeLightning(float radius, float progress, Vector3 bluePos, Vector3 redPos)
        {
            for (int i = 0; i < _lightnings.Count; i++)
            {
                LineRenderer lr = _lightnings[i];
                if (lr == null)
                {
                    continue;
                }

                // 前半は青/赤から中央へ流れ込む線、後半は紫コアから外周へバチバチする線。
                if (i < _lightnings.Count / 2)
                {
                    Vector3 start = (i % 2 == 0) ? bluePos : redPos;
                    Vector3 end = Vector3.zero;
                    float phase = Time.time * 11.0f + i * 0.57f;
                    start += new Vector3(0f, Mathf.Sin(phase) * 0.08f, Mathf.Cos(phase) * 0.08f);
                    SetJaggedLine(lr, start, end, 0.08f + progress * 0.10f);
                    lr.widthMultiplier = 0.035f + progress * 0.055f;
                }
                else
                {
                    float a = Time.time * 13.0f + i * 0.73f;
                    Vector3 end = new Vector3(Mathf.Cos(a), Mathf.Sin(a * 1.2f), Mathf.Sin(a)) * radius * Mathf.Lerp(0.45f, 1.35f, progress);
                    SetJaggedLine(lr, Vector3.zero, end, 0.10f + progress * 0.14f);
                    lr.widthMultiplier = 0.025f + progress * 0.050f;
                }
            }
        }

        private void UpdateProjectileLightning(float radius)
        {
            for (int i = 0; i < _lightnings.Count; i++)
            {
                LineRenderer lr = _lightnings[i];
                if (lr == null)
                {
                    continue;
                }

                float a = Time.time * 16.0f + i * 0.61f;
                Vector3 start = new Vector3(Mathf.Cos(a), Mathf.Sin(a * 1.7f), Mathf.Sin(a)) * radius * UnityEngine.Random.Range(0.7f, 1.55f);
                Vector3 end = start * 0.15f + Vector3.back * radius * UnityEngine.Random.Range(0.2f, 1.2f);
                SetJaggedLine(lr, start, end, 0.18f);
                lr.widthMultiplier = UnityEngine.Random.Range(0.025f, 0.060f);
            }
        }

        private void UpdateImpactLightning(float radius, float normalized)
        {
            for (int i = 0; i < _lightnings.Count; i++)
            {
                LineRenderer lr = _lightnings[i];
                if (lr == null)
                {
                    continue;
                }

                float a = Mathf.PI * 2.0f * i / Mathf.Max(1, _lightnings.Count) + Time.time * 3.0f;
                Vector3 end = new Vector3(Mathf.Cos(a), Mathf.Sin(a * 1.3f) * 0.35f, Mathf.Sin(a)) * radius * Mathf.Lerp(0.2f, 1.0f, normalized);
                SetJaggedLine(lr, Vector3.zero, end, 0.28f * (1.0f - normalized));
                lr.widthMultiplier = Mathf.Lerp(0.085f, 0.015f, normalized);
            }
        }

        private void UpdatePullLines(Vector3 center, List<Vector3> targetPositions)
        {
            int targetCount = targetPositions != null ? targetPositions.Count : 0;
            for (int i = 0; i < _pullLines.Count; i++)
            {
                LineRenderer lr = _pullLines[i];
                if (lr == null)
                {
                    continue;
                }

                bool active = i < targetCount;
                lr.gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                Vector3 start = targetPositions[i];
                Vector3 wobble = UnityEngine.Random.insideUnitSphere * 0.07f;
                lr.SetPosition(0, start + wobble);
                lr.SetPosition(1, center + UnityEngine.Random.insideUnitSphere * 0.18f);
                lr.widthMultiplier = UnityEngine.Random.Range(0.018f, 0.040f);
            }
        }

        private void SetJaggedLine(LineRenderer lr, Vector3 start, Vector3 end, float jitter)
        {
            if (lr == null)
            {
                return;
            }

            int count = lr.positionCount;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / Mathf.Max(1, count - 1);
                Vector3 p = Vector3.Lerp(start, end, t);
                if (i != 0 && i != count - 1)
                {
                    p += UnityEngine.Random.insideUnitSphere * jitter;
                }
                lr.SetPosition(i, p);
            }
        }

        private void UpdateSparks()
        {
            if (_sparks.Count == 0)
            {
                return;
            }

            for (int i = _sparks.Count - 1; i >= 0; i--)
            {
                Spark s = _sparks[i];
                if (s.Object == null)
                {
                    _sparks.RemoveAt(i);
                    continue;
                }

                float age = Time.time - s.SpawnTime;
                float t = Mathf.Clamp01(age / s.LifeSeconds);
                if (t >= 1.0f)
                {
                    SafeDestroy(s.Object);
                    _sparks.RemoveAt(i);
                    continue;
                }

                s.Object.transform.position += s.Velocity * Time.deltaTime;
                s.Object.transform.localScale = Vector3.one * Mathf.Lerp(s.InitialScale, 0f, t);
            }
        }

        private void RemoveOldestSpark()
        {
            if (_sparks.Count == 0)
            {
                return;
            }

            Spark oldest = _sparks[0];
            SafeDestroy(oldest.Object);
            _sparks.RemoveAt(0);
        }

        private void ClearSparks()
        {
            for (int i = 0; i < _sparks.Count; i++)
            {
                SafeDestroy(_sparks[i].Object);
            }
            _sparks.Clear();
        }

        private Material CreateTransparentMaterial(string name, Color color)
        {
            Shader shader = null;

            try { shader = Shader.Find("Universal Render Pipeline/Unlit"); } catch { shader = null; }
            if (shader == null) { try { shader = Shader.Find("Sprites/Default"); } catch { shader = null; } }
            if (shader == null) { try { shader = Shader.Find("Standard"); } catch { shader = null; } }
            if (shader == null) { try { shader = Shader.Find("Diffuse"); } catch { shader = null; } }

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

        private void TrySetLineCaps(LineRenderer lr)
        {
            try
            {
                lr.numCornerVertices = 4;
                lr.numCapVertices = 4;
            }
            catch { }
        }

        private void SetLinesActive(List<LineRenderer> lines, bool active)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] != null)
                {
                    lines[i].gameObject.SetActive(active);
                }
            }
        }

        private void SafeSetActive(GameObject obj, bool active)
        {
            try
            {
                if (obj != null)
                {
                    obj.SetActive(active);
                }
            }
            catch { }
        }

        private Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
        {
            if (value.sqrMagnitude <= 0.0001f)
            {
                return fallback.sqrMagnitude <= 0.0001f ? Vector3.forward : fallback.normalized;
            }
            return value.normalized;
        }

        private int CountRenderers()
        {
            try
            {
                if (_root == null)
                {
                    return 0;
                }
                Renderer[] renderers = _root.GetComponentsInChildren<Renderer>(true);
                return renderers != null ? renderers.Length : 0;
            }
            catch
            {
                return -1;
            }
        }

        private int CountLines()
        {
            return _rings.Count + _spirals.Count + _lightnings.Count + _pullLines.Count;
        }

        private void LogVisibleTick(Vector3 position, int targetCount)
        {
            if (Time.time < _nextTickLogTime)
            {
                return;
            }

            _nextTickLogTime = Time.time + 1.0f;
            MelonLogger.Msg("[" + VersionTag + "] VisibleTick. Pos=" + FormatVector(position) +
                            ", Active=" + _visible +
                            ", Renderers=" + CountRenderers() +
                            ", Lines=" + CountLines() +
                            ", Targets=" + targetCount);
        }

        private string GetShaderName(Material mat)
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
            try { UnityEngine.Object.Destroy(obj); } catch { }
        }

        private enum RingPlane
        {
            XY,
            XZ,
            YZ
        }

        private struct Spark
        {
            public GameObject Object;
            public float SpawnTime;
            public float LifeSeconds;
            public Vector3 Velocity;
            public float InitialScale;
        }
    }
}
