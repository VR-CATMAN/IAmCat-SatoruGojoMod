using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace IAmCatGojoMod
{
    /// <summary>
    /// 無下限バリアの見た目担当。
    ///
    /// InfinityBarrierEffect_v1:
    /// - 猫の周囲に薄い青い透明球
    /// - バリア境界に青白いリング3本
    /// - 止めた/減速した物体の周囲に小さい粒子
    ///
    /// AssetBundleなし、UnityのPrimitiveとLineRendererだけで作る。
    /// </summary>
    public sealed class InfinityBarrierEffect
    {
        private const string VersionTag = "InfinityBarrierEffect_v1";

        private const int RingSegments = 96;
        private const int MaxSparkCount = 80;
        private const float SparkLifeSeconds = 0.45f;
        private const float SparkSize = 0.045f;
        private const float SparkSpawnInterval = 0.035f;

        private GameObject _root;
        private GameObject _sphere;
        private GameObject _ringXY;
        private GameObject _ringXZ;
        private GameObject _ringYZ;

        private Material _sphereMaterial;
        private Material _ringMaterial;
        private Material _sparkMaterial;

        private LineRenderer _ringXYRenderer;
        private LineRenderer _ringXZRenderer;
        private LineRenderer _ringYZRenderer;

        private readonly List<Spark> _sparks = new List<Spark>();

        private bool _visible;
        private float _radius;
        private float _nextSparkTime;

        public bool IsVisible => _visible;

        public void Show(Vector3 center, float radius)
        {
            if (_root == null)
            {
                CreateObjects(radius);
            }

            _visible = true;
            _radius = radius;

            if (_root != null)
            {
                _root.SetActive(true);
                _root.transform.position = center;
            }

            MelonLogger.Msg("[" + VersionTag + "] Show. Radius=" + radius);
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
            _sphere = null;
            _ringXY = null;
            _ringXZ = null;
            _ringYZ = null;

            _ringXYRenderer = null;
            _ringXZRenderer = null;
            _ringYZRenderer = null;

            SafeDestroy(_sphereMaterial);
            SafeDestroy(_ringMaterial);
            SafeDestroy(_sparkMaterial);

            _sphereMaterial = null;
            _ringMaterial = null;
            _sparkMaterial = null;
        }

        public void Update(Vector3 center, float radius, int affectedCount)
        {
            _radius = radius;

            if (!_visible)
            {
                UpdateSparks();
                return;
            }

            if (_root == null)
            {
                CreateObjects(radius);
            }

            if (_root != null)
            {
                _root.transform.position = center;

                // ほんの少し脈動。対象を止めているときは強く脈動。
                float pulseStrength = affectedCount > 0 ? 0.055f : 0.025f;
                float pulse = 1.0f + Mathf.Sin(Time.time * 5.0f) * pulseStrength;
                _root.transform.localScale = Vector3.one * pulse;
            }

            UpdateSphereMaterial(affectedCount);
            UpdateRingMaterial(affectedCount);
            UpdateSparks();
        }

        /// <summary>
        /// 物体を減速した位置に小さい青白い粒子を出す。
        /// 重くなりすぎないように内部で間引く。
        /// </summary>
        public void BurstAt(Vector3 position)
        {
            if (!_visible)
            {
                return;
            }

            if (Time.time < _nextSparkTime)
            {
                return;
            }

            _nextSparkTime = Time.time + SparkSpawnInterval;

            if (_sparks.Count >= MaxSparkCount)
            {
                RemoveOldestSpark();
            }

            GameObject spark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            if (spark == null)
            {
                return;
            }

            spark.name = "GojoInfinity_Spark";
            spark.transform.position = position + UnityEngine.Random.insideUnitSphere * 0.08f;
            spark.transform.localScale = Vector3.one * SparkSize;

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
                    _sparkMaterial = CreateTransparentMaterial("GojoInfinity_Spark_Mat", new Color(0.55f, 0.95f, 1.0f, 0.85f));
                }

                renderer.material = _sparkMaterial;
            }

            Vector3 velocity = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(0.12f, 0.35f);

            Spark s = new Spark
            {
                Object = spark,
                SpawnTime = Time.time,
                LifeSeconds = SparkLifeSeconds,
                Velocity = velocity,
                InitialScale = SparkSize
            };

            _sparks.Add(s);
        }

        private void CreateObjects(float radius)
        {
            _radius = radius;

            _root = new GameObject("GojoInfinity_BarrierEffect_Root");
            _root.SetActive(false);

            CreateSphere(radius);
            CreateRings(radius);

            MelonLogger.Msg("[" + VersionTag + "] Created.");
        }

        private void CreateSphere(float radius)
        {
            _sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            if (_sphere == null)
            {
                return;
            }

            _sphere.name = "GojoInfinity_TransparentSphere";
            _sphere.transform.SetParent(_root.transform, false);
            _sphere.transform.localPosition = Vector3.zero;
            _sphere.transform.localRotation = Quaternion.identity;
            _sphere.transform.localScale = Vector3.one * (radius * 2.0f);

            Collider col = _sphere.GetComponent<Collider>();
            if (col != null)
            {
                SafeDestroy(col);
            }

            _sphereMaterial = CreateTransparentMaterial("GojoInfinity_Sphere_Mat", new Color(0.15f, 0.65f, 1.0f, 0.16f));

            Renderer renderer = _sphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = _sphereMaterial;
            }
        }

        private void CreateRings(float radius)
        {
            _ringMaterial = CreateTransparentMaterial("GojoInfinity_Ring_Mat", new Color(0.70f, 0.95f, 1.0f, 0.85f));

            _ringXY = CreateRingObject("GojoInfinity_Ring_XY", radius, RingPlane.XY, out _ringXYRenderer);
            _ringXZ = CreateRingObject("GojoInfinity_Ring_XZ", radius, RingPlane.XZ, out _ringXZRenderer);
            _ringYZ = CreateRingObject("GojoInfinity_Ring_YZ", radius, RingPlane.YZ, out _ringYZRenderer);
        }

        private GameObject CreateRingObject(string name, float radius, RingPlane plane, out LineRenderer lineRenderer)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(_root.transform, false);
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localRotation = Quaternion.identity;
            obj.transform.localScale = Vector3.one;

            lineRenderer = obj.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = true;
            lineRenderer.positionCount = RingSegments;
            lineRenderer.widthMultiplier = 0.018f;
            lineRenderer.material = _ringMaterial;

            try
            {
                lineRenderer.numCornerVertices = 4;
                lineRenderer.numCapVertices = 4;
            }
            catch
            {
                // 環境によって未対応でも無視。
            }

            for (int i = 0; i < RingSegments; i++)
            {
                float angle = (Mathf.PI * 2.0f * i) / RingSegments;
                float x = Mathf.Cos(angle) * radius;
                float y = Mathf.Sin(angle) * radius;

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

                lineRenderer.SetPosition(i, p);
            }

            return obj;
        }

        private void UpdateSphereMaterial(int affectedCount)
        {
            if (_sphereMaterial == null)
            {
                return;
            }

            float alpha = affectedCount > 0 ? 0.23f : 0.14f;
            float glow = affectedCount > 0 ? 0.15f : 0.0f;

            Color c = new Color(0.12f + glow, 0.58f + glow, 1.0f, alpha);
            _sphereMaterial.color = c;

            TrySetMaterialColor(_sphereMaterial, "_BaseColor", c);
            TrySetMaterialColor(_sphereMaterial, "_Color", c);
        }

        private void UpdateRingMaterial(int affectedCount)
        {
            if (_ringMaterial == null)
            {
                return;
            }

            float pulse = Mathf.Abs(Mathf.Sin(Time.time * 7.0f));
            float alpha = affectedCount > 0 ? Mathf.Lerp(0.72f, 1.0f, pulse) : Mathf.Lerp(0.45f, 0.72f, pulse);

            Color c = new Color(0.65f, 0.95f, 1.0f, alpha);
            _ringMaterial.color = c;

            TrySetMaterialColor(_ringMaterial, "_BaseColor", c);
            TrySetMaterialColor(_ringMaterial, "_Color", c);

            float width = affectedCount > 0 ? 0.026f : 0.018f;
            if (_ringXYRenderer != null) _ringXYRenderer.widthMultiplier = width;
            if (_ringXZRenderer != null) _ringXZRenderer.widthMultiplier = width;
            if (_ringYZRenderer != null) _ringYZRenderer.widthMultiplier = width;
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

                float scale = Mathf.Lerp(s.InitialScale, 0.0f, t);
                s.Object.transform.localScale = Vector3.one * scale;
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

            // 透明描画設定。使えるShaderプロパティだけ試す。
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
