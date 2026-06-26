using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace IAmCatGojoMod
{
    /// <summary>
    /// 赫 / Red の見た目担当。
    ///
    /// RedEffect_v1_RedOrbShockwave:
    /// - BlueEffect v6/v7で見えた「Show時に遅延生成 + Infinity式透明Material」を踏襲
    /// - 手元から飛ぶ小さい赤い球
    /// - 飛翔中の赤いトレイルと短い稲妻
    /// - 着弾時に外へ広がる赤い衝撃波リング
    /// - 赤黒いギザギザ稲妻
    /// - 吹っ飛ぶ対象へ伸びる赤いライン
    ///
    /// 注意:
    /// - Initialize時には描画Objectを作らない。
    /// - Projectile/ImpactのShow時に初めてCreateObjectsする。
    /// </summary>
    public sealed class RedEffect
    {
        private const string VersionTag = "RedEffect_v1_RedOrbShockwave";

        private const int RingSegments = 96;
        private const int ProjectileRingCount = 3;
        private const int ProjectileTrailCount = 5;
        private const int ProjectileLightningCount = 5;
        private const int ImpactRingCount = 5;
        private const int ImpactLightningCount = 20;
        private const int TargetLineCount = 42;
        private const int SparkCount = 44;
        private const int LightningSegments = 7;

        private GameObject _root;
        private GameObject _projectileRoot;
        private GameObject _projectileSphere;
        private GameObject _impactRoot;
        private GameObject _impactFlashSphere;

        private readonly List<LineRenderer> _projectileRings = new List<LineRenderer>();
        private readonly List<LineRenderer> _projectileTrails = new List<LineRenderer>();
        private readonly List<LineRenderer> _projectileLightnings = new List<LineRenderer>();
        private readonly List<LineRenderer> _impactRings = new List<LineRenderer>();
        private readonly List<LineRenderer> _impactLightnings = new List<LineRenderer>();
        private readonly List<LineRenderer> _targetLines = new List<LineRenderer>();
        private readonly List<GameObject> _sparks = new List<GameObject>();
        private readonly List<Vector3> _sparkSeeds = new List<Vector3>();

        private Material _coreMaterial;
        private Material _ringMaterial;
        private Material _trailMaterial;
        private Material _lightningMaterial;
        private Material _sparkMaterial;

        private bool _initialized;
        private bool _projectileVisible;
        private bool _impactVisible;
        private float _nextDebugLogTime;
        private float _projectileShownTime;
        private float _impactShownTime;
        private float _projectileRadius = 0.16f;
        private float _impactRadius = 4.2f;

        public bool IsVisible => _projectileVisible || _impactVisible;

        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            MelonLogger.Msg("[" + VersionTag + "] Initialized. DeferredCreate=True");
        }

        public void ShowProjectile(Vector3 position, Vector3 direction, float projectileRadius)
        {
            if (!_initialized)
            {
                Initialize();
            }

            _projectileRadius = Mathf.Max(0.05f, projectileRadius);
            _projectileVisible = true;
            _impactVisible = false;
            _projectileShownTime = Time.time;
            _nextDebugLogTime = 0f;

            if (_root == null)
            {
                CreateObjects(_projectileRadius, _impactRadius);
            }

            if (_root != null)
            {
                _root.SetActive(true);
            }

            if (_projectileRoot != null)
            {
                _projectileRoot.SetActive(true);
                _projectileRoot.transform.position = position;
                _projectileRoot.transform.rotation = SafeLookRotation(direction);
            }

            if (_impactRoot != null)
            {
                _impactRoot.SetActive(false);
            }

            UpdateProjectile(position, direction, 0f);

            MelonLogger.Msg("[" + VersionTag + "] ProjectileShow. Pos=" + FormatVector(position) +
                            ", Dir=" + FormatVector(direction) +
                            ", Radius=" + _projectileRadius.ToString("0.00") +
                            ", Renderers=" + CountEnabledRenderers(_root) +
                            ", Lines=" + CountEnabledLines() +
                            ", Cam=" + CameraInfo());
        }

        public void UpdateProjectile(Vector3 position, Vector3 direction, float normalizedLife)
        {
            if (!_initialized || !_projectileVisible)
            {
                return;
            }

            if (_root == null)
            {
                CreateObjects(_projectileRadius, _impactRadius);
            }

            if (_root == null || _projectileRoot == null)
            {
                return;
            }

            _root.SetActive(true);
            _projectileRoot.SetActive(true);
            _impactRoot?.SetActive(false);

            direction = SafeNormalize(direction, Vector3.forward);
            _projectileRoot.transform.position = position;
            _projectileRoot.transform.rotation = SafeLookRotation(direction);

            float age = Mathf.Max(0f, Time.time - _projectileShownTime);
            float pulse = 1.0f + Mathf.Sin(Time.time * 24.0f) * 0.10f + Mathf.Sin(Time.time * 49.0f) * 0.035f;

            if (_projectileSphere != null)
            {
                _projectileSphere.transform.localPosition = Vector3.zero;
                _projectileSphere.transform.localScale = Vector3.one * (_projectileRadius * 2.0f * pulse);
            }

            UpdateProjectileRings(age, normalizedLife);
            UpdateProjectileTrails(position, direction, age, normalizedLife);
            UpdateProjectileLightnings(age, normalizedLife);
            UpdateProjectileMaterials(normalizedLife);

            if (Time.time >= _nextDebugLogTime)
            {
                _nextDebugLogTime = Time.time + 1.0f;
                MelonLogger.Msg("[" + VersionTag + "] ProjectileTick. Pos=" + FormatVector(position) +
                                ", Active=" + (_projectileRoot != null && _projectileRoot.activeInHierarchy) +
                                ", Renderers=" + CountEnabledRenderers(_root) +
                                ", Lines=" + CountEnabledLines() +
                                ", Cam=" + CameraInfo());
            }
        }

        public void ShowImpact(Vector3 position, float impactRadius, IList<Vector3> targetPositions)
        {
            if (!_initialized)
            {
                Initialize();
            }

            _impactRadius = Mathf.Max(0.5f, impactRadius);
            _projectileVisible = false;
            _impactVisible = true;
            _impactShownTime = Time.time;
            _nextDebugLogTime = 0f;

            if (_root == null)
            {
                CreateObjects(_projectileRadius, _impactRadius);
            }

            if (_root != null)
            {
                _root.SetActive(true);
            }

            if (_projectileRoot != null)
            {
                _projectileRoot.SetActive(false);
            }

            if (_impactRoot != null)
            {
                _impactRoot.SetActive(true);
                _impactRoot.transform.position = position;
                _impactRoot.transform.rotation = Quaternion.identity;
                _impactRoot.transform.localScale = Vector3.one;
            }

            ResetSparkSeeds();
            UpdateImpact(position, _impactRadius, 0f, targetPositions);

            MelonLogger.Msg("[" + VersionTag + "] ImpactShow. Pos=" + FormatVector(position) +
                            ", Radius=" + _impactRadius.ToString("0.00") +
                            ", Targets=" + (targetPositions != null ? targetPositions.Count : 0) +
                            ", Renderers=" + CountEnabledRenderers(_root) +
                            ", Lines=" + CountEnabledLines() +
                            ", Cam=" + CameraInfo());
        }

        public void UpdateImpact(Vector3 position, float impactRadius, float normalizedTime, IList<Vector3> targetPositions)
        {
            if (!_initialized || !_impactVisible)
            {
                return;
            }

            if (_root == null)
            {
                CreateObjects(_projectileRadius, impactRadius);
            }

            if (_root == null || _impactRoot == null)
            {
                return;
            }

            _impactRadius = Mathf.Max(0.5f, impactRadius);
            normalizedTime = Mathf.Clamp01(normalizedTime);

            _root.SetActive(true);
            _projectileRoot?.SetActive(false);
            _impactRoot.SetActive(true);
            _impactRoot.transform.position = position;

            float age = Mathf.Max(0f, Time.time - _impactShownTime);
            float pulse = 1.0f + Mathf.Sin(Time.time * 18.0f) * 0.06f;
            _impactRoot.transform.localScale = Vector3.one * pulse;

            UpdateImpactFlash(normalizedTime);
            UpdateImpactRings(normalizedTime);
            UpdateImpactLightnings(normalizedTime, age);
            UpdateTargetLines(position, targetPositions, normalizedTime, age);
            UpdateSparks(normalizedTime, age);
            UpdateImpactMaterials(normalizedTime, targetPositions != null ? targetPositions.Count : 0);

            if (Time.time >= _nextDebugLogTime)
            {
                _nextDebugLogTime = Time.time + 1.0f;
                MelonLogger.Msg("[" + VersionTag + "] ImpactTick. Pos=" + FormatVector(position) +
                                ", Active=" + (_impactRoot != null && _impactRoot.activeInHierarchy) +
                                ", Renderers=" + CountEnabledRenderers(_root) +
                                ", Lines=" + CountEnabledLines() +
                                ", Targets=" + (targetPositions != null ? targetPositions.Count : 0) +
                                ", Cam=" + CameraInfo());
            }
        }

        public void HideProjectile()
        {
            _projectileVisible = false;
            if (_projectileRoot != null)
            {
                _projectileRoot.SetActive(false);
            }

            HideProjectileTrails();
        }

        public void HideImpact()
        {
            _impactVisible = false;
            if (_impactRoot != null)
            {
                _impactRoot.SetActive(false);
            }

            HideTargetLines();
            HideImpactLightnings();
        }

        public void HideAll()
        {
            _projectileVisible = false;
            _impactVisible = false;

            if (_root != null)
            {
                _root.SetActive(false);
            }

            if (_projectileRoot != null)
            {
                _projectileRoot.SetActive(false);
            }

            if (_impactRoot != null)
            {
                _impactRoot.SetActive(false);
            }

            HideTargetLines();
            HideImpactLightnings();
            HideProjectileTrails();
            MelonLogger.Msg("[" + VersionTag + "] HideAll.");
        }

        public void Destroy()
        {
            SafeDestroy(_root);
            _root = null;
            _projectileRoot = null;
            _projectileSphere = null;
            _impactRoot = null;
            _impactFlashSphere = null;

            _projectileRings.Clear();
            _projectileTrails.Clear();
            _projectileLightnings.Clear();
            _impactRings.Clear();
            _impactLightnings.Clear();
            _targetLines.Clear();
            _sparks.Clear();
            _sparkSeeds.Clear();

            SafeDestroy(_coreMaterial);
            SafeDestroy(_ringMaterial);
            SafeDestroy(_trailMaterial);
            SafeDestroy(_lightningMaterial);
            SafeDestroy(_sparkMaterial);

            _coreMaterial = null;
            _ringMaterial = null;
            _trailMaterial = null;
            _lightningMaterial = null;
            _sparkMaterial = null;
        }

        private void CreateObjects(float projectileRadius, float impactRadius)
        {
            _projectileRadius = Mathf.Max(0.05f, projectileRadius);
            _impactRadius = Mathf.Max(0.5f, impactRadius);

            _root = new GameObject("GojoRed_EffectRoot_v1_RedOrbShockwave");
            _root.SetActive(false);

            _coreMaterial = CreateTransparentMaterial("GojoRed_v1_Core_Mat", new Color(1.0f, 0.08f, 0.03f, 0.85f));
            _ringMaterial = CreateTransparentMaterial("GojoRed_v1_Ring_Mat", new Color(1.0f, 0.34f, 0.22f, 0.95f));
            _trailMaterial = CreateTransparentMaterial("GojoRed_v1_Trail_Mat", new Color(1.0f, 0.18f, 0.08f, 0.72f));
            _lightningMaterial = CreateTransparentMaterial("GojoRed_v1_Lightning_Mat", new Color(1.0f, 0.78f, 0.62f, 1.0f));
            _sparkMaterial = CreateTransparentMaterial("GojoRed_v1_Spark_Mat", new Color(1.0f, 0.44f, 0.24f, 0.92f));

            CreateProjectileObjects();
            CreateImpactObjects();

            MelonLogger.Msg("[" + VersionTag + "] Created. ShaderCore=" + ShaderName(_coreMaterial) +
                            ", ShaderRing=" + ShaderName(_ringMaterial) +
                            ", ShaderLightning=" + ShaderName(_lightningMaterial) +
                            ", Renderers=" + CountEnabledRenderers(_root) +
                            ", Lines=" + CountEnabledLines());
        }

        private void CreateProjectileObjects()
        {
            _projectileRoot = new GameObject("GojoRed_ProjectileRoot");
            _projectileRoot.transform.SetParent(_root.transform, false);
            _projectileRoot.SetActive(false);

            _projectileSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            if (_projectileSphere != null)
            {
                _projectileSphere.name = "GojoRed_ProjectileCore";
                _projectileSphere.transform.SetParent(_projectileRoot.transform, false);
                _projectileSphere.transform.localPosition = Vector3.zero;
                _projectileSphere.transform.localRotation = Quaternion.identity;
                _projectileSphere.transform.localScale = Vector3.one * (_projectileRadius * 2.0f);

                Collider col = _projectileSphere.GetComponent<Collider>();
                if (col != null)
                {
                    SafeDestroy(col);
                }

                Renderer renderer = _projectileSphere.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material = _coreMaterial;
                }
            }

            for (int i = 0; i < ProjectileRingCount; i++)
            {
                LineRenderer lr = CreateLineObject(_projectileRoot.transform, "GojoRed_ProjectileRing_" + i, _ringMaterial, false, 0.018f);
                _projectileRings.Add(lr);
            }

            for (int i = 0; i < ProjectileTrailCount; i++)
            {
                LineRenderer lr = CreateLineObject(_root.transform, "GojoRed_ProjectileTrail_" + i, _trailMaterial, true, 0.025f);
                lr.positionCount = 5;
                _projectileTrails.Add(lr);
            }

            for (int i = 0; i < ProjectileLightningCount; i++)
            {
                LineRenderer lr = CreateLineObject(_projectileRoot.transform, "GojoRed_ProjectileLightning_" + i, _lightningMaterial, false, 0.014f);
                lr.positionCount = LightningSegments;
                _projectileLightnings.Add(lr);
            }
        }

        private void CreateImpactObjects()
        {
            _impactRoot = new GameObject("GojoRed_ImpactRoot");
            _impactRoot.transform.SetParent(_root.transform, false);
            _impactRoot.SetActive(false);

            _impactFlashSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            if (_impactFlashSphere != null)
            {
                _impactFlashSphere.name = "GojoRed_ImpactFlash";
                _impactFlashSphere.transform.SetParent(_impactRoot.transform, false);
                _impactFlashSphere.transform.localPosition = Vector3.zero;
                _impactFlashSphere.transform.localRotation = Quaternion.identity;
                _impactFlashSphere.transform.localScale = Vector3.one * 0.25f;

                Collider col = _impactFlashSphere.GetComponent<Collider>();
                if (col != null)
                {
                    SafeDestroy(col);
                }

                Renderer renderer = _impactFlashSphere.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material = _coreMaterial;
                }
            }

            for (int i = 0; i < ImpactRingCount; i++)
            {
                LineRenderer lr = CreateLineObject(_impactRoot.transform, "GojoRed_ImpactRing_" + i, _ringMaterial, false, 0.030f);
                _impactRings.Add(lr);
            }

            for (int i = 0; i < ImpactLightningCount; i++)
            {
                LineRenderer lr = CreateLineObject(_impactRoot.transform, "GojoRed_ImpactLightning_" + i, _lightningMaterial, false, 0.020f);
                lr.positionCount = LightningSegments;
                _impactLightnings.Add(lr);
            }

            for (int i = 0; i < TargetLineCount; i++)
            {
                LineRenderer lr = CreateLineObject(_root.transform, "GojoRed_TargetLine_" + i, _trailMaterial, true, 0.020f);
                lr.positionCount = 5;
                lr.enabled = false;
                _targetLines.Add(lr);
            }

            for (int i = 0; i < SparkCount; i++)
            {
                GameObject spark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                if (spark == null)
                {
                    continue;
                }

                spark.name = "GojoRed_ImpactSpark_" + i;
                spark.transform.SetParent(_impactRoot.transform, false);
                spark.transform.localPosition = Vector3.zero;
                spark.transform.localScale = Vector3.one * 0.035f;

                Collider col = spark.GetComponent<Collider>();
                if (col != null)
                {
                    SafeDestroy(col);
                }

                Renderer renderer = spark.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material = _sparkMaterial;
                }

                _sparks.Add(spark);
                _sparkSeeds.Add(SeedDirection(i));
            }
        }

        private LineRenderer CreateLineObject(Transform parent, string name, Material material, bool useWorldSpace, float width)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localRotation = Quaternion.identity;
            obj.transform.localScale = Vector3.one;

            LineRenderer line = obj.AddComponent<LineRenderer>();
            line.useWorldSpace = useWorldSpace;
            line.loop = false;
            line.positionCount = RingSegments;
            line.widthMultiplier = width;
            line.material = material;

            try
            {
                line.numCornerVertices = 4;
                line.numCapVertices = 4;
            }
            catch
            {
            }

            return line;
        }

        private void UpdateProjectileRings(float age, float normalizedLife)
        {
            for (int i = 0; i < _projectileRings.Count; i++)
            {
                LineRenderer lr = _projectileRings[i];
                if (lr == null)
                {
                    continue;
                }

                lr.enabled = true;
                lr.loop = true;
                lr.positionCount = RingSegments;
                lr.widthMultiplier = 0.018f + i * 0.004f;

                float r = _projectileRadius * (1.55f + i * 0.35f + Mathf.Sin(Time.time * (14f + i * 3f)) * 0.08f);
                RingPlane plane = i == 0 ? RingPlane.XY : (i == 1 ? RingPlane.XZ : RingPlane.YZ);
                SetRing(lr, r, plane, age * (2.0f + i));
            }
        }

        private void UpdateProjectileTrails(Vector3 position, Vector3 direction, float age, float normalizedLife)
        {
            direction = SafeNormalize(direction, Vector3.forward);
            Vector3 side = SafeNormalize(Vector3.Cross(Vector3.up, direction), Vector3.right);
            Vector3 up = SafeNormalize(Vector3.Cross(direction, side), Vector3.up);

            for (int i = 0; i < _projectileTrails.Count; i++)
            {
                LineRenderer lr = _projectileTrails[i];
                if (lr == null)
                {
                    continue;
                }

                lr.enabled = true;
                lr.loop = false;
                lr.positionCount = 5;
                lr.widthMultiplier = Mathf.Lerp(0.036f, 0.012f, i / (float)Mathf.Max(1, _projectileTrails.Count - 1));

                float spread = 0.045f + i * 0.025f;
                float length = 0.55f + i * 0.28f;
                Vector3 offset = side * Mathf.Sin(age * 22f + i * 1.7f) * spread + up * Mathf.Cos(age * 19f + i * 2.1f) * spread;

                for (int p = 0; p < 5; p++)
                {
                    float t = p / 4.0f;
                    Vector3 wobble = side * Mathf.Sin(age * 35f + p * 1.3f + i) * spread * (1f - t);
                    Vector3 point = position - direction * (length * t) + offset * (1f - t) + wobble;
                    lr.SetPosition(p, point);
                }
            }
        }

        private void UpdateProjectileLightnings(float age, float normalizedLife)
        {
            for (int i = 0; i < _projectileLightnings.Count; i++)
            {
                LineRenderer lr = _projectileLightnings[i];
                if (lr == null)
                {
                    continue;
                }

                lr.enabled = true;
                lr.loop = false;
                lr.positionCount = LightningSegments;
                lr.widthMultiplier = 0.010f + Mathf.Abs(Mathf.Sin(Time.time * 28f + i)) * 0.010f;

                Vector3 start = SeedDirection(i * 3 + 1) * _projectileRadius * 0.9f;
                Vector3 end = SeedDirection(i * 5 + 4) * _projectileRadius * (2.0f + Mathf.Sin(age * 8f + i) * 0.3f);
                SetJaggedLineLocal(lr, start, end, 0.035f, age + i * 0.37f);
            }
        }

        private void UpdateImpactFlash(float normalizedTime)
        {
            if (_impactFlashSphere == null)
            {
                return;
            }

            float t = Mathf.Clamp01(normalizedTime);
            float size = Mathf.Lerp(0.35f, _impactRadius * 0.55f, Mathf.Sin(t * Mathf.PI));
            _impactFlashSphere.transform.localPosition = Vector3.zero;
            _impactFlashSphere.transform.localScale = Vector3.one * size;
        }

        private void UpdateImpactRings(float normalizedTime)
        {
            float t = Mathf.Clamp01(normalizedTime);

            for (int i = 0; i < _impactRings.Count; i++)
            {
                LineRenderer lr = _impactRings[i];
                if (lr == null)
                {
                    continue;
                }

                lr.enabled = true;
                lr.loop = true;
                lr.positionCount = RingSegments;

                float phase = Mathf.Clamp01(t + i * 0.09f);
                float ringRadius = Mathf.Lerp(0.18f, _impactRadius * (0.75f + i * 0.12f), phase);
                float width = Mathf.Lerp(0.055f, 0.012f, phase);
                lr.widthMultiplier = width;

                RingPlane plane = i % 3 == 0 ? RingPlane.XZ : (i % 3 == 1 ? RingPlane.XY : RingPlane.YZ);
                SetRing(lr, ringRadius, plane, Time.time * (0.8f + i * 0.13f));
            }
        }

        private void UpdateImpactLightnings(float normalizedTime, float age)
        {
            float t = Mathf.Clamp01(normalizedTime);

            for (int i = 0; i < _impactLightnings.Count; i++)
            {
                LineRenderer lr = _impactLightnings[i];
                if (lr == null)
                {
                    continue;
                }

                lr.enabled = true;
                lr.loop = false;
                lr.positionCount = LightningSegments;

                Vector3 dir = SeedDirection(i * 7 + 2);
                Vector3 start = dir * Mathf.Lerp(0.08f, _impactRadius * 0.18f, t);
                Vector3 end = dir * Mathf.Lerp(_impactRadius * 0.45f, _impactRadius * 1.10f, t);

                lr.widthMultiplier = Mathf.Lerp(0.032f, 0.006f, t) * (0.7f + Mathf.Abs(Mathf.Sin(age * 27f + i)) * 0.6f);
                SetJaggedLineLocal(lr, start, end, _impactRadius * 0.035f, age * 2.3f + i);
            }
        }

        private void UpdateTargetLines(Vector3 impactPosition, IList<Vector3> targetPositions, float normalizedTime, float age)
        {
            if (targetPositions == null || targetPositions.Count == 0)
            {
                HideTargetLines();
                return;
            }

            int activeCount = Mathf.Min(_targetLines.Count, targetPositions.Count);

            for (int i = 0; i < _targetLines.Count; i++)
            {
                LineRenderer lr = _targetLines[i];
                if (lr == null)
                {
                    continue;
                }

                if (i >= activeCount)
                {
                    lr.enabled = false;
                    continue;
                }

                Vector3 target = targetPositions[i];
                Vector3 toTarget = target - impactPosition;
                Vector3 dir = SafeNormalize(toTarget, SeedDirection(i));
                Vector3 side = SafeNormalize(Vector3.Cross(Vector3.up, dir), Vector3.right);
                Vector3 up = SafeNormalize(Vector3.Cross(dir, side), Vector3.up);

                lr.enabled = true;
                lr.loop = false;
                lr.positionCount = 5;
                lr.widthMultiplier = Mathf.Lerp(0.032f, 0.008f, normalizedTime);

                for (int p = 0; p < 5; p++)
                {
                    float t = p / 4.0f;
                    float wobble = Mathf.Sin(age * 35f + i * 0.9f + p * 1.7f) * 0.07f * (1f - normalizedTime);
                    Vector3 point = Vector3.Lerp(impactPosition, target, t) + side * wobble + up * wobble * 0.55f;
                    lr.SetPosition(p, point);
                }
            }
        }

        private void UpdateSparks(float normalizedTime, float age)
        {
            float t = Mathf.Clamp01(normalizedTime);

            for (int i = 0; i < _sparks.Count; i++)
            {
                GameObject spark = _sparks[i];
                if (spark == null)
                {
                    continue;
                }

                Vector3 seed = i < _sparkSeeds.Count ? _sparkSeeds[i] : SeedDirection(i);
                float distance = Mathf.Lerp(0.12f, _impactRadius * UnityRandom01(i, 0.42f, 0.95f), Mathf.Pow(t, 0.65f));
                float spiral = age * (2.8f + (i % 5) * 0.25f);
                Vector3 side = SafeNormalize(Vector3.Cross(Vector3.up, seed), Vector3.right);
                Vector3 pos = seed * distance + side * Mathf.Sin(spiral + i) * 0.12f;
                float scale = Mathf.Lerp(0.075f, 0.0f, t);

                spark.transform.localPosition = pos;
                spark.transform.localScale = Vector3.one * scale;
            }
        }

        private void UpdateProjectileMaterials(float normalizedLife)
        {
            float pulse = Mathf.Abs(Mathf.Sin(Time.time * 26.0f));
            Color core = new Color(1.0f, 0.05f + pulse * 0.18f, 0.02f, 0.78f + pulse * 0.18f);
            Color ring = new Color(1.0f, 0.28f + pulse * 0.22f, 0.16f, 0.92f);
            Color trail = new Color(1.0f, 0.10f + pulse * 0.16f, 0.04f, 0.60f + pulse * 0.22f);
            SetMaterialColor(_coreMaterial, core);
            SetMaterialColor(_ringMaterial, ring);
            SetMaterialColor(_trailMaterial, trail);
            SetMaterialColor(_lightningMaterial, new Color(1.0f, 0.80f, 0.62f, 0.95f));
        }

        private void UpdateImpactMaterials(float normalizedTime, int targetCount)
        {
            float t = Mathf.Clamp01(normalizedTime);
            float pulse = Mathf.Abs(Mathf.Sin(Time.time * 22.0f));
            float boost = targetCount > 0 ? 0.15f : 0f;
            SetMaterialColor(_coreMaterial, new Color(1.0f, 0.08f + pulse * 0.22f, 0.02f, Mathf.Lerp(0.90f, 0.15f, t)));
            SetMaterialColor(_ringMaterial, new Color(1.0f, 0.30f + boost, 0.18f + boost * 0.4f, Mathf.Lerp(1.0f, 0.12f, t)));
            SetMaterialColor(_trailMaterial, new Color(1.0f, 0.12f, 0.04f, Mathf.Lerp(0.85f, 0.05f, t)));
            SetMaterialColor(_lightningMaterial, new Color(1.0f, 0.70f + pulse * 0.18f, 0.52f, Mathf.Lerp(1.0f, 0.10f, t)));
            SetMaterialColor(_sparkMaterial, new Color(1.0f, 0.38f + pulse * 0.20f, 0.18f, Mathf.Lerp(0.95f, 0.0f, t)));
        }

        private void SetRing(LineRenderer lr, float radius, RingPlane plane, float phase)
        {
            if (lr == null)
            {
                return;
            }

            lr.positionCount = RingSegments;

            for (int i = 0; i < RingSegments; i++)
            {
                float angle = (Mathf.PI * 2.0f * i) / RingSegments + phase;
                float x = Mathf.Cos(angle) * radius;
                float y = Mathf.Sin(angle) * radius;
                float wobble = Mathf.Sin(angle * 5f + Time.time * 9f) * radius * 0.025f;

                Vector3 p;
                switch (plane)
                {
                    case RingPlane.XZ:
                        p = new Vector3(x, wobble, y);
                        break;
                    case RingPlane.YZ:
                        p = new Vector3(wobble, x, y);
                        break;
                    default:
                        p = new Vector3(x, y, wobble);
                        break;
                }

                lr.SetPosition(i, p);
            }
        }

        private void SetJaggedLineLocal(LineRenderer lr, Vector3 start, Vector3 end, float jitter, float seed)
        {
            if (lr == null)
            {
                return;
            }

            int count = Mathf.Max(2, LightningSegments);
            lr.positionCount = count;

            Vector3 dir = SafeNormalize(end - start, Vector3.forward);
            Vector3 side = SafeNormalize(Vector3.Cross(Vector3.up, dir), Vector3.right);
            Vector3 up = SafeNormalize(Vector3.Cross(dir, side), Vector3.up);

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                float amp = Mathf.Sin(t * Mathf.PI) * jitter;
                float a = Mathf.Sin(seed * 12.9898f + i * 4.141f + Time.time * 41f);
                float b = Mathf.Cos(seed * 7.233f + i * 2.771f + Time.time * 37f);
                Vector3 offset = side * a * amp + up * b * amp;

                if (i == 0 || i == count - 1)
                {
                    offset = Vector3.zero;
                }

                lr.SetPosition(i, Vector3.Lerp(start, end, t) + offset);
            }
        }

        private void HideProjectileTrails()
        {
            for (int i = 0; i < _projectileTrails.Count; i++)
            {
                if (_projectileTrails[i] != null)
                {
                    _projectileTrails[i].enabled = false;
                }
            }
        }

        private void HideTargetLines()
        {
            for (int i = 0; i < _targetLines.Count; i++)
            {
                if (_targetLines[i] != null)
                {
                    _targetLines[i].enabled = false;
                }
            }
        }

        private void HideImpactLightnings()
        {
            for (int i = 0; i < _impactLightnings.Count; i++)
            {
                if (_impactLightnings[i] != null)
                {
                    _impactLightnings[i].enabled = false;
                }
            }
        }

        private void ResetSparkSeeds()
        {
            if (_sparkSeeds.Count == _sparks.Count)
            {
                return;
            }

            _sparkSeeds.Clear();
            for (int i = 0; i < _sparks.Count; i++)
            {
                _sparkSeeds.Add(SeedDirection(i));
            }
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
                try { shader = Shader.Find("Sprites/Default"); } catch { shader = null; }
            }

            if (shader == null)
            {
                try { shader = Shader.Find("Standard"); } catch { shader = null; }
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
                TrySetMaterialColor(mat, "_BaseColor", color);
                TrySetMaterialColor(mat, "_Color", color);
            }
            catch
            {
            }
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
            catch
            {
            }
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
            catch
            {
            }
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
            catch
            {
            }
        }

        private Vector3 SeedDirection(int index)
        {
            float a = index * 2.399963f;
            float z = 1.0f - 2.0f * ((index * 37 % 101) / 100.0f);
            float r = Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - z * z));
            return new Vector3(Mathf.Cos(a) * r, z, Mathf.Sin(a) * r).normalized;
        }

        private float UnityRandom01(int index, float min, float max)
        {
            float v = Mathf.Abs(Mathf.Sin(index * 12.9898f + 78.233f) * 43758.5453f);
            v = v - Mathf.Floor(v);
            return Mathf.Lerp(min, max, v);
        }

        private Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
        {
            if (v.sqrMagnitude < 0.0001f)
            {
                return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.forward;
            }

            return v.normalized;
        }

        private Quaternion SafeLookRotation(Vector3 direction)
        {
            direction = SafeNormalize(direction, Vector3.forward);
            try
            {
                return Quaternion.LookRotation(direction, Vector3.up);
            }
            catch
            {
                return Quaternion.identity;
            }
        }

        private int CountEnabledRenderers(GameObject root)
        {
            if (root == null)
            {
                return 0;
            }

            try
            {
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                int count = 0;
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null && renderers[i].enabled)
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
            CountLines(_projectileRings, ref count);
            CountLines(_projectileTrails, ref count);
            CountLines(_projectileLightnings, ref count);
            CountLines(_impactRings, ref count);
            CountLines(_impactLightnings, ref count);
            CountLines(_targetLines, ref count);
            return count;
        }

        private void CountLines(List<LineRenderer> lines, ref int count)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] != null && lines[i].enabled)
                {
                    count++;
                }
            }
        }

        private string CameraInfo()
        {
            try
            {
                Camera cam = Camera.main;
                if (cam == null)
                {
                    return "CameraMain=null";
                }

                return cam.name + "/mask=" + cam.cullingMask + "/pos=" + FormatVector(cam.transform.position);
            }
            catch
            {
                return "CameraInfoError";
            }
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
            catch
            {
            }

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

            try
            {
                UnityEngine.Object.Destroy(obj);
            }
            catch
            {
            }
        }

        private enum RingPlane
        {
            XY,
            XZ,
            YZ
        }
    }
}
