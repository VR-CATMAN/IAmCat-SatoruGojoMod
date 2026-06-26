using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace IAmCatGojoMod
{
    /// <summary>
    /// 瞬間移動の見た目担当。
    ///
    /// TeleportBlinkEffect_v1:
    /// - Initializeではログだけ
    /// - Show時にGameObject/Materialを遅延生成
    /// - 開始地点と到着地点に青白いリング
    /// - 移動軌跡に短い稲妻ライン
    /// - 短時間だけ表示して自動で消える
    /// </summary>
    public sealed class TeleportBlinkEffect
    {
        private const string VersionTag = "TeleportBlinkEffect_v1";

        private const int RingSegments = 72;
        private const int TrailLineCount = 8;
        private const int ParticleCount = 18;

        private GameObject _root;
        private LineRenderer _startRing;
        private LineRenderer _endRing;
        private readonly List<LineRenderer> _trailLines = new List<LineRenderer>();
        private readonly List<GameObject> _particles = new List<GameObject>();
        private readonly List<Vector3> _particleSeeds = new List<Vector3>();

        private Material _ringMaterial;
        private Material _trailMaterial;
        private Material _particleMaterial;

        private bool _initialized;
        private bool _visible;
        private float _showStartTime;
        private float _duration = 0.28f;

        private Vector3 _startPosition;
        private Vector3 _endPosition;

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

        public void Show(Vector3 startPosition, Vector3 endPosition, float duration)
        {
            if (!_initialized)
            {
                Initialize();
            }

            _startPosition = startPosition;
            _endPosition = endPosition;
            _duration = Mathf.Clamp(duration, 0.08f, 1.2f);
            _showStartTime = Time.time;
            _visible = true;

            if (_root == null)
            {
                CreateObjects();
            }

            if (_root != null)
            {
                _root.SetActive(true);
            }

            UpdateEffect();

            MelonLogger.Msg("[" + VersionTag + "] Show. Start=" + FormatVector(_startPosition) +
                            ", End=" + FormatVector(_endPosition) +
                            ", Duration=" + _duration.ToString("0.00"));
        }

        public void Hide()
        {
            _visible = false;

            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        public void UpdateEffect()
        {
            if (!_initialized || !_visible)
            {
                return;
            }

            if (_root == null)
            {
                CreateObjects();
            }

            if (_root == null)
            {
                return;
            }

            float t = Mathf.Clamp01((Time.time - _showStartTime) / Mathf.Max(0.01f, _duration));
            if (t >= 1.0f)
            {
                Hide();
                return;
            }

            _root.SetActive(true);

            float alpha = 1.0f - t;
            float pulse = 1.0f + Mathf.Sin(Time.time * 40.0f) * 0.08f;
            float ringRadius = Mathf.Lerp(0.22f, 0.75f, t) * pulse;

            UpdateRing(_startRing, _startPosition, ringRadius, alpha, 0.0f);
            UpdateRing(_endRing, _endPosition, ringRadius * 1.15f, alpha, Mathf.PI * 0.5f);
            UpdateTrailLines(t, alpha);
            UpdateParticles(t, alpha);
            UpdateMaterials(alpha);
        }

        private void CreateObjects()
        {
            _root = new GameObject("GojoTeleport_EffectRoot_v1");
            _root.SetActive(false);

            _ringMaterial = CreateTransparentMaterial("GojoTeleport_v1_Ring_Mat", new Color(0.55f, 0.96f, 1.0f, 0.92f));
            _trailMaterial = CreateTransparentMaterial("GojoTeleport_v1_Trail_Mat", new Color(0.72f, 0.98f, 1.0f, 0.95f));
            _particleMaterial = CreateTransparentMaterial("GojoTeleport_v1_Particle_Mat", new Color(0.85f, 1.0f, 1.0f, 0.92f));

            _startRing = CreateLine("GojoTeleport_v1_StartRing", _ringMaterial, 0.035f, RingSegments + 1);
            _endRing = CreateLine("GojoTeleport_v1_EndRing", _ringMaterial, 0.045f, RingSegments + 1);

            for (int i = 0; i < TrailLineCount; i++)
            {
                LineRenderer lr = CreateLine("GojoTeleport_v1_TrailLine_" + i, _trailMaterial, 0.025f, 4);
                _trailLines.Add(lr);
            }

            for (int i = 0; i < ParticleCount; i++)
            {
                GameObject p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                if (p == null)
                {
                    continue;
                }

                p.name = "GojoTeleport_v1_SparkParticle_" + i;
                p.transform.SetParent(_root.transform, false);
                p.transform.localScale = Vector3.one * 0.055f;

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

                _particles.Add(p);
                _particleSeeds.Add(new Vector3(
                    UnityEngine.Random.Range(-1.0f, 1.0f),
                    UnityEngine.Random.Range(-1.0f, 1.0f),
                    UnityEngine.Random.Range(-1.0f, 1.0f)
                ));
            }

            MelonLogger.Msg("[" + VersionTag + "] Created. Lines=" + (2 + _trailLines.Count) + ", Particles=" + _particles.Count);
        }

        private LineRenderer CreateLine(string name, Material material, float width, int positionCount)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(_root.transform, false);

            LineRenderer lr = obj.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = positionCount;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.material = material;
            lr.enabled = true;

            try
            {
                lr.numCornerVertices = 4;
                lr.numCapVertices = 4;
            }
            catch { }

            return lr;
        }

        private void UpdateRing(LineRenderer lr, Vector3 center, float radius, float alpha, float phase)
        {
            if (lr == null)
            {
                return;
            }

            lr.enabled = true;
            lr.startWidth = Mathf.Lerp(0.045f, 0.012f, 1.0f - alpha);
            lr.endWidth = lr.startWidth;

            Quaternion cameraFacing = Quaternion.identity;
            try
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    cameraFacing = Quaternion.LookRotation(cam.transform.forward, Vector3.up);
                }
            }
            catch { }

            for (int i = 0; i <= RingSegments; i++)
            {
                float angle = ((float)i / (float)RingSegments) * Mathf.PI * 2.0f + phase;
                Vector3 local = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
                lr.SetPosition(i, center + cameraFacing * local);
            }
        }

        private void UpdateTrailLines(float t, float alpha)
        {
            Vector3 path = _endPosition - _startPosition;
            float distance = path.magnitude;
            Vector3 forward = distance > 0.001f ? path / distance : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.001f)
            {
                right = Vector3.right;
            }
            right.Normalize();
            Vector3 up = Vector3.Cross(forward, right).normalized;

            for (int i = 0; i < _trailLines.Count; i++)
            {
                LineRenderer lr = _trailLines[i];
                if (lr == null)
                {
                    continue;
                }

                lr.enabled = true;
                lr.startWidth = Mathf.Lerp(0.035f, 0.006f, t);
                lr.endWidth = Mathf.Lerp(0.020f, 0.004f, t);

                float seed = (float)i * 17.13f;
                float offsetScale = Mathf.Lerp(0.18f, 0.04f, t);
                Vector3 offsetA = (right * Mathf.Sin(seed + Time.time * 12.0f) + up * Mathf.Cos(seed * 1.3f + Time.time * 15.0f)) * offsetScale;
                Vector3 offsetB = (right * Mathf.Cos(seed * 0.7f + Time.time * 18.0f) + up * Mathf.Sin(seed * 1.7f + Time.time * 11.0f)) * offsetScale;

                float head = Mathf.Clamp01(t + 0.30f + i * 0.025f);
                float tail = Mathf.Clamp01(t - 0.10f + i * 0.018f);

                Vector3 p0 = Vector3.Lerp(_startPosition, _endPosition, tail) + offsetA;
                Vector3 p1 = Vector3.Lerp(_startPosition, _endPosition, Mathf.Lerp(tail, head, 0.35f)) + offsetB;
                Vector3 p2 = Vector3.Lerp(_startPosition, _endPosition, Mathf.Lerp(tail, head, 0.70f)) - offsetA * 0.7f;
                Vector3 p3 = Vector3.Lerp(_startPosition, _endPosition, head) - offsetB * 0.4f;

                lr.SetPosition(0, p0);
                lr.SetPosition(1, p1);
                lr.SetPosition(2, p2);
                lr.SetPosition(3, p3);
            }
        }

        private void UpdateParticles(float t, float alpha)
        {
            Vector3 path = _endPosition - _startPosition;
            float distance = path.magnitude;
            Vector3 forward = distance > 0.001f ? path / distance : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.001f)
            {
                right = Vector3.right;
            }
            right.Normalize();
            Vector3 up = Vector3.Cross(forward, right).normalized;

            for (int i = 0; i < _particles.Count; i++)
            {
                GameObject p = _particles[i];
                if (p == null)
                {
                    continue;
                }

                Vector3 seed = i < _particleSeeds.Count ? _particleSeeds[i] : Vector3.zero;
                float localT = Mathf.Repeat(t + i * 0.071f, 1.0f);
                Vector3 basePos = Vector3.Lerp(_startPosition, _endPosition, localT);
                Vector3 wobble = (right * seed.x + up * seed.y) * Mathf.Lerp(0.34f, 0.04f, t);
                p.transform.position = basePos + wobble;
                p.transform.localScale = Vector3.one * Mathf.Lerp(0.085f, 0.015f, t);
                p.SetActive(alpha > 0.05f);
            }
        }

        private void UpdateMaterials(float alpha)
        {
            SetMaterialColor(_ringMaterial, new Color(0.55f, 0.96f, 1.0f, 0.92f * alpha));
            SetMaterialColor(_trailMaterial, new Color(0.72f, 0.98f, 1.0f, 0.95f * alpha));
            SetMaterialColor(_particleMaterial, new Color(0.85f, 1.0f, 1.0f, 0.92f * alpha));
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

        private string FormatVector(Vector3 v)
        {
            return "(" + v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ", " + v.z.ToString("0.00") + ")";
        }
    }
}
