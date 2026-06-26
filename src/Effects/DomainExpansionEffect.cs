using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace IAmCatGojoMod
{
    /// <summary>
    /// 領域展開エフェクト
    ///
    /// DomainExpansionEffect_v21_Il2CppImageLoadFix:
    /// - Phase 4.2: White Void → Gyuin → Rich Galaxy Black Hole Universe方式
    /// - 白い板/白い部屋/白い反転球は引き続き廃止
    /// - Ability v6のRenderer Maskと組み合わせ、Camera背景/Fogで白い空間を作る
    /// - 発動中だけカメラ背景とRenderSettings Fogを変更し、Hide時に復元
    /// - Geometryで白背景を作らないので、VRでの張りぼて感を減らす
    /// - 領域ドーム/足元リング/白バーストリング/拘束リングは残す
    ///
    /// 注意:
    /// - 床・壁・家具の非表示はAbility v6側のRenderer Maskが担当
    /// - 白空間の後、ギュイーン吸い込みトンネル演出を追加
    /// - v11の白→黒ギュイーン放射収束ラインを維持
    /// - v12では黒空間の奥にブラックホール宇宙を追加
    /// - v13ではCreateBlackHoleUniverseObjectsの挿入位置ミスによる大量コンパイルエラーを修正
    /// - v14ではギュイーン後、静かな宇宙空間へ放り出される感じに調整
    /// - v15ではブラックホール周辺に銀河の渦、星雲の霞、多めの星を追加
    /// - 静けさは維持しつつ、画面奥がリッチな宇宙背景になるよう強化
    /// - 黒い中心核、紫/青/白の降着円盤、星の流れ、重力歪みリングを出す
    /// </summary>
    public sealed class DomainExpansionEffect
    {
        private const string VersionTag = "DomainExpansionEffect_v21_Il2CppImageLoadFix";

        private bool _initialized;
        private bool _created;
        private bool _visible;

        private float _showTime;

        private GameObject _root;
        private GameObject _dome;
        private GameObject _groundRingRoot;
        private GameObject _targetRingRoot;
        private GameObject _whiteBurstRingRoot;
        private GameObject _pullTunnelRoot;
        private GameObject _blackHoleRoot;
        private GameObject _blackHoleCore;
        private GameObject _universeImageRoot;
        private GameObject _universeImageQuad;

        private Material _domeMaterial;
        private Material _lineMaterial;
        private Material _targetMaterial;
        private Material _whiteLineMaterial;
        private Material _tunnelLineMaterial;
        private Material _tunnelRingMaterial;
        private Material _blackHoleCoreMaterial;
        private Material _blackHoleRingMaterial;
        private Material _blackHoleStarMaterial;
        private Material _galaxyArmMaterial;
        private Material _nebulaVeilMaterial;
        private Material _universeImageMaterial;
        private Texture2D _universeTexture;

        private bool _universeTextureLoadAttempted;
        private bool _universeTextureLoaded;
        private string _loadedUniverseImagePath;
        private bool _universeDebugEnvironmentLogged;
        private bool _universeQuadDebugLogged;

        private const string UniverseImageRelativePath = "UserData/IAmCatGojoMod/domain_blackhole_background.png";
        private const string UniverseRawRelativePath = "UserData/IAmCatGojoMod/domain_blackhole_background_1024x576_rgba.bytes";
        private const string UniversePngFileName = "domain_blackhole_background.png";
        private const string UniverseRawFileName = "domain_blackhole_background_1024x576_rgba.bytes";
        private const int UniverseRawWidth = 1024;
        private const int UniverseRawHeight = 576;

        private readonly List<LineRenderer> _groundRings = new List<LineRenderer>();
        private readonly List<LineRenderer> _targetRings = new List<LineRenderer>();
        private readonly List<LineRenderer> _whiteBurstRings = new List<LineRenderer>();
        private readonly List<LineRenderer> _tunnelLines = new List<LineRenderer>();
        private readonly List<LineRenderer> _tunnelRings = new List<LineRenderer>();
        private readonly List<LineRenderer> _blackHoleRings = new List<LineRenderer>();
        private readonly List<LineRenderer> _blackHoleStarStreaks = new List<LineRenderer>();
        private readonly List<LineRenderer> _galaxyArms = new List<LineRenderer>();
        private readonly List<LineRenderer> _nebulaVeils = new List<LineRenderer>();

        private const int GroundRingCount = 3;
        private const int TargetRingCount = 12;
        private const int WhiteBurstRingCount = 4;
        private const int TunnelLineCount = 92;
        private const int TunnelRingCount = 5;
        private const int BlackHoleRingCount = 8;
        private const int BlackHoleStarStreakCount = 128;
        private const int GalaxyArmCount = 7;
        private const int NebulaVeilCount = 5;

        private const int RingSegments = 64;
        private const int TargetRingSegments = 32;
        private const int BurstRingSegments = 96;
        private const int TunnelRingSegments = 96;
        private const int BlackHoleRingSegments = 128;
        private const int GalaxyArmSegments = 150;
        private const int NebulaVeilSegments = 96;

        private const float WhiteInSeconds = 0.32f;
        private const float WhiteHoldSeconds = 1.15f;
        private const float WhiteOutSeconds = 1.55f;

        private Camera _targetCamera;
        private bool _cameraStateSaved;
        private CameraClearFlags _savedClearFlags;
        private Color _savedBackgroundColor;
        private int _savedCullingMask;

        private bool _renderSettingsSaved;
        private bool _savedFog;
        private Color _savedFogColor;
        private FogMode _savedFogMode;
        private float _savedFogDensity;
        private float _savedFogStartDistance;
        private float _savedFogEndDistance;
        private Material _savedSkybox;

        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            // 重要: Initializeでは何も生成しない。
            // I Am CatのVR描画では、Show時の遅延生成が安定。
            MelonLogger.Msg("[" + VersionTag + "] Initialized. DeferredCreate=True");
        }

        public void Show(Vector3 center, float radius)
        {
            if (!_initialized)
            {
                Initialize();
            }

            if (!_created || _root == null)
            {
                CreateObjects();
            }

            _visible = true;
            _showTime = Time.time;
            _universeQuadDebugLogged = false;

            SaveAndApplyWhiteVoidCamera();

            if (_root != null)
            {
                _root.SetActive(true);
                _root.transform.position = center;
            }

            UpdateDome(radius, 0.0f, 0.0f);
            UpdateGroundRings(radius, 0.0f, 0.0f);
            UpdateWhiteBurstRings(radius, 0.0f);
            UpdateTargetRings(null, 0.0f);

            MelonLogger.Msg("[" + VersionTag + "] Show. Center=" + FormatVector(center) + ", Radius=" + radius.ToString("0.00"));
        }

        public void Hide()
        {
            if (!_visible && (_root == null || !_root.activeSelf))
            {
                RestoreWhiteVoidCamera();
                return;
            }

            _visible = false;

            RestoreWhiteVoidCamera();

            if (_root != null)
            {
                _root.SetActive(false);
            }

            MelonLogger.Msg("[" + VersionTag + "] Hide.");
        }

        public void UpdateEffect(
            Vector3 center,
            float radius,
            float normalizedTime,
            List<Vector3> targetPositions,
            int npcCount,
            int rigidbodyCount)
        {
            if (!_created || _root == null || !_visible)
            {
                return;
            }

            float elapsed = Time.time - _showTime;

            // 発動中にCamera.mainが差し替わる/復活する可能性があるので軽く維持。
            MaintainWhiteVoidCamera();

            _root.transform.position = center;

            UpdateDome(radius, normalizedTime, elapsed);
            UpdateGroundRings(radius, normalizedTime, elapsed);
            UpdateWhiteBurstRings(radius, elapsed);
            UpdatePullTunnel(radius, elapsed);
            UpdateBlackHoleUniverse(radius, elapsed);
            UpdateTargetRings(targetPositions, elapsed);
        }

        private void CreateObjects()
        {
            _created = true;

            _root = new GameObject("Gojo_DomainExpansionEffect_Root_v21_DebugImagePath");
            UnityEngine.Object.DontDestroyOnLoad(_root);

            _domeMaterial = CreateTransparentMaterial("Gojo_Domain_Dome_Mat_v15", new Color(0.70f, 0.92f, 1.0f, 0.16f));
            _lineMaterial = CreateLineMaterial("Gojo_Domain_Line_Mat_v15", new Color(0.80f, 0.96f, 1.0f, 0.88f));
            _targetMaterial = CreateLineMaterial("Gojo_Domain_TargetRing_Mat_v15", new Color(0.92f, 0.98f, 1.0f, 0.94f));
            _whiteLineMaterial = CreateLineMaterial("Gojo_Domain_WhiteBurst_Mat_v15", new Color(1.0f, 1.0f, 1.0f, 0.98f));
            _tunnelLineMaterial = CreateLineMaterial("Gojo_Domain_PullTunnelLine_Mat_v15", new Color(0.78f, 0.92f, 1.0f, 0.88f));
            _tunnelRingMaterial = CreateLineMaterial("Gojo_Domain_PullTunnelRing_Mat_v15", new Color(0.92f, 0.98f, 1.0f, 0.78f));
            _blackHoleCoreMaterial = CreateTransparentMaterial("Gojo_Domain_BlackHoleCore_Mat_v15", new Color(0.0f, 0.0f, 0.0f, 0.96f));
            _blackHoleRingMaterial = CreateLineMaterial("Gojo_Domain_BlackHoleRing_Mat_v15", new Color(0.82f, 0.55f, 1.0f, 0.92f));
            _blackHoleStarMaterial = CreateLineMaterial("Gojo_Domain_BlackHoleStars_Mat_v15", new Color(0.82f, 0.92f, 1.0f, 0.78f));
            _galaxyArmMaterial = CreateLineMaterial("Gojo_Domain_GalaxyArms_Mat_v15", new Color(0.62f, 0.78f, 1.0f, 0.62f));
            _nebulaVeilMaterial = CreateLineMaterial("Gojo_Domain_NebulaVeils_Mat_v15", new Color(0.38f, 0.32f, 0.72f, 0.34f));

            CreateDome();
            CreateGroundRings();
            CreateWhiteBurstRings();
            CreatePullTunnelObjects();
            CreateImageUniverseObjects();
            CreateTargetRings();

            _root.SetActive(false);

            MelonLogger.Msg(
                "[" + VersionTag + "] CreateObjects completed. " +
                "Rings=" + _groundRings.Count +
                ", WhiteBurstRings=" + _whiteBurstRings.Count +
                ", TunnelLines=" + _tunnelLines.Count +
                ", TunnelRings=" + _tunnelRings.Count +
                ", UniverseImageQuad=" + (_universeImageQuad != null ? "True" : "False") +
                ", ImageLoaded=" + (_universeTextureLoaded ? "True" : "False") +
                ", TargetRings=" + _targetRings.Count
            );
        }

        private void CreateDome()
        {
            _dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _dome.name = "Gojo_Domain_Dome_v15";
            _dome.transform.SetParent(_root.transform, false);

            RemoveCollider(_dome);

            try
            {
                Renderer renderer = _dome.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material = _domeMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }
            catch
            {
            }
        }

        private void CreateGroundRings()
        {
            _groundRingRoot = new GameObject("Gojo_Domain_GroundRings_v15");
            _groundRingRoot.transform.SetParent(_root.transform, false);

            for (int i = 0; i < GroundRingCount; i++)
            {
                GameObject ringObj = new GameObject("Gojo_Domain_GroundRing_v15_" + i);
                ringObj.transform.SetParent(_groundRingRoot.transform, false);

                LineRenderer lr = ringObj.AddComponent<LineRenderer>();
                SetupLine(lr, _lineMaterial, 0.026f + i * 0.010f, RingSegments + 1, true);

                _groundRings.Add(lr);
            }
        }

        private void CreateWhiteBurstRings()
        {
            _whiteBurstRingRoot = new GameObject("Gojo_Domain_WhiteBurstRings_v15");
            _whiteBurstRingRoot.transform.SetParent(_root.transform, false);

            for (int i = 0; i < WhiteBurstRingCount; i++)
            {
                GameObject ringObj = new GameObject("Gojo_Domain_WhiteBurstRing_v15_" + i);
                ringObj.transform.SetParent(_whiteBurstRingRoot.transform, false);

                LineRenderer lr = ringObj.AddComponent<LineRenderer>();
                SetupLine(lr, _whiteLineMaterial, 0.065f + i * 0.018f, BurstRingSegments + 1, true);

                _whiteBurstRings.Add(lr);
            }
        }

        private void CreatePullTunnelObjects()
        {
            _pullTunnelRoot = new GameObject("Gojo_Domain_PullTunnelRoot_v15");
            _pullTunnelRoot.transform.SetParent(_root.transform, false);

            for (int i = 0; i < TunnelLineCount; i++)
            {
                GameObject lineObj = new GameObject("Gojo_Domain_PullTunnelLine_v15_" + i);
                lineObj.transform.SetParent(_pullTunnelRoot.transform, false);

                LineRenderer lr = lineObj.AddComponent<LineRenderer>();
                SetupLine(lr, _tunnelLineMaterial, 0.014f, 2, false);
                lineObj.SetActive(false);

                _tunnelLines.Add(lr);
            }



            for (int i = 0; i < TunnelRingCount; i++)
            {
                GameObject ringObj = new GameObject("Gojo_Domain_PullTunnelRing_v15_" + i);
                ringObj.transform.SetParent(_pullTunnelRoot.transform, false);

                LineRenderer lr = ringObj.AddComponent<LineRenderer>();
                SetupLine(lr, _tunnelRingMaterial, 0.018f, TunnelRingSegments + 1, true);
                ringObj.SetActive(false);

                _tunnelRings.Add(lr);
            }

            _pullTunnelRoot.SetActive(false);
        }

        private void CreateImageUniverseObjects()
        {
            _universeImageRoot = new GameObject("Gojo_Domain_ImageUniverseRoot_v21");
            _universeImageRoot.transform.SetParent(_root.transform, false);

            _universeImageQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _universeImageQuad.name = "Gojo_Domain_BlackHoleBackgroundQuad_v21";
            _universeImageQuad.transform.SetParent(_universeImageRoot.transform, false);
            RemoveCollider(_universeImageQuad);

            _universeTexture = LoadUniverseTextureOrFallback();
            LogLoadedUniverseTextureInfo();
            _universeImageMaterial = CreateBackgroundTextureMaterial("Gojo_Domain_ImageUniverse_Mat_v21", _universeTexture);

            try
            {
                Renderer renderer = _universeImageQuad.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material = _universeImageMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }
            catch
            {
            }

            _universeImageQuad.SetActive(false);
            _universeImageRoot.SetActive(false);

            if (_universeTextureLoaded)
            {
                MelonLogger.Msg("[" + VersionTag + "] Universe image loaded: " + _loadedUniverseImagePath);
            }
            else
            {
                MelonLogger.Warning("[" + VersionTag + "] Universe image not found or failed. Using dark fallback. Put PNG here: " + UniverseImageRelativePath + " or RAW here: " + UniverseRawRelativePath);
            }
        }

        private void CreateBlackHoleUniverseObjects()
        {
            _blackHoleRoot = new GameObject("Gojo_Domain_BlackHoleUniverseRoot_v15");
            _blackHoleRoot.transform.SetParent(_root.transform, false);

            _blackHoleCore = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _blackHoleCore.name = "Gojo_Domain_BlackHoleCore_v15";
            _blackHoleCore.transform.SetParent(_blackHoleRoot.transform, false);
            RemoveCollider(_blackHoleCore);

            try
            {
                Renderer renderer = _blackHoleCore.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material = _blackHoleCoreMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }
            catch
            {
            }

            for (int i = 0; i < BlackHoleRingCount; i++)
            {
                GameObject ringObj = new GameObject("Gojo_Domain_BlackHoleAccretionRing_v15_" + i);
                ringObj.transform.SetParent(_blackHoleRoot.transform, false);

                LineRenderer lr = ringObj.AddComponent<LineRenderer>();
                SetupLine(lr, _blackHoleRingMaterial, 0.018f, BlackHoleRingSegments + 1, true);
                ringObj.SetActive(false);

                _blackHoleRings.Add(lr);
            }

            for (int i = 0; i < GalaxyArmCount; i++)
            {
                GameObject armObj = new GameObject("Gojo_Domain_GalaxySpiralArm_v15_" + i);
                armObj.transform.SetParent(_blackHoleRoot.transform, false);

                LineRenderer lr = armObj.AddComponent<LineRenderer>();
                SetupLine(lr, _galaxyArmMaterial, 0.022f, GalaxyArmSegments + 1, false);
                armObj.SetActive(false);

                _galaxyArms.Add(lr);
            }

            for (int i = 0; i < NebulaVeilCount; i++)
            {
                GameObject veilObj = new GameObject("Gojo_Domain_NebulaVeil_v15_" + i);
                veilObj.transform.SetParent(_blackHoleRoot.transform, false);

                LineRenderer lr = veilObj.AddComponent<LineRenderer>();
                SetupLine(lr, _nebulaVeilMaterial, 0.050f, NebulaVeilSegments + 1, true);
                veilObj.SetActive(false);

                _nebulaVeils.Add(lr);
            }

            for (int i = 0; i < BlackHoleStarStreakCount; i++)
            {
                GameObject starObj = new GameObject("Gojo_Domain_BlackHoleStarStreak_v15_" + i);
                starObj.transform.SetParent(_blackHoleRoot.transform, false);

                LineRenderer lr = starObj.AddComponent<LineRenderer>();
                SetupLine(lr, _blackHoleStarMaterial, 0.010f, 2, false);
                starObj.SetActive(false);

                _blackHoleStarStreaks.Add(lr);
            }

            _blackHoleRoot.SetActive(false);
        }

        private void CreateTargetRings()
        {
            _targetRingRoot = new GameObject("Gojo_Domain_TargetRings_v15");
            _targetRingRoot.transform.SetParent(_root.transform, false);

            for (int i = 0; i < TargetRingCount; i++)
            {
                GameObject ringObj = new GameObject("Gojo_Domain_TargetRing_v15_" + i);
                ringObj.transform.SetParent(_targetRingRoot.transform, false);

                LineRenderer lr = ringObj.AddComponent<LineRenderer>();
                SetupLine(lr, _targetMaterial, 0.017f, TargetRingSegments + 1, true);

                ringObj.SetActive(false);
                _targetRings.Add(lr);
            }
        }

        private void SaveAndApplyWhiteVoidCamera()
        {
            try
            {
                _targetCamera = Camera.main;
            }
            catch
            {
                _targetCamera = null;
            }

            if (_targetCamera != null && !_cameraStateSaved)
            {
                try
                {
                    _savedClearFlags = _targetCamera.clearFlags;
                    _savedBackgroundColor = _targetCamera.backgroundColor;
                    _savedCullingMask = _targetCamera.cullingMask;
                    _cameraStateSaved = true;
                }
                catch
                {
                }
            }

            if (!_renderSettingsSaved)
            {
                try
                {
                    _savedFog = RenderSettings.fog;
                    _savedFogColor = RenderSettings.fogColor;
                    _savedFogMode = RenderSettings.fogMode;
                    _savedFogDensity = RenderSettings.fogDensity;
                    _savedFogStartDistance = RenderSettings.fogStartDistance;
                    _savedFogEndDistance = RenderSettings.fogEndDistance;
                    _savedSkybox = RenderSettings.skybox;
                    _renderSettingsSaved = true;
                }
                catch
                {
                }
            }

            ApplyWhiteVoidCamera(0.0f);

            string camName = _targetCamera != null ? SafeName(_targetCamera) : "null";
            MelonLogger.Msg("[" + VersionTag + "] Camera white void applied. Camera=" + camName);
        }

        private void MaintainWhiteVoidCamera()
        {
            if (!_visible)
            {
                return;
            }

            try
            {
                if (_targetCamera == null)
                {
                    _targetCamera = Camera.main;
                }
            }
            catch
            {
            }

            float elapsed = Time.time - _showTime;
            ApplyWhiteVoidCamera(elapsed);
        }

        private void ApplyWhiteVoidCamera(float elapsed)
        {
            VoidPhase phase = GetVoidPhase(elapsed);
            Color bg = GetVoidBackgroundColor(elapsed);
            Color fog = GetVoidFogColor(elapsed);

            if (_targetCamera != null)
            {
                try
                {
                    _targetCamera.clearFlags = CameraClearFlags.SolidColor;
                    _targetCamera.backgroundColor = bg;

                    // cullingMaskはAbility v6側のRendererMaskでやる。
                    // Layer構成不明の状態で触ると手/NPCまで消える可能性があるため、ここでは維持。
                    _targetCamera.cullingMask = _savedCullingMask;
                }
                catch
                {
                }
            }

            try
            {
                RenderSettings.skybox = null;
                RenderSettings.fog = true;
                RenderSettings.fogColor = fog;
                RenderSettings.fogMode = FogMode.ExponentialSquared;

                // 白フェーズでは白く飛ばす。
                // 黒フェーズではFogを少し弱め、手/NPC/リングが黒霧に潰れすぎないようにする。
                if (phase == VoidPhase.WhiteFlash)
                {
                    float s = GetWhiteFlashStrength(elapsed);
                    RenderSettings.fogDensity = Mathf.Lerp(0.045f, 0.13f, s);
                    RenderSettings.fogStartDistance = 0.0f;
                    RenderSettings.fogEndDistance = Mathf.Lerp(4.0f, 1.2f, s);
                }
                else if (phase == VoidPhase.Transition)
                {
                    float t = GetWhiteToBlackT(elapsed);
                    RenderSettings.fogDensity = Mathf.Lerp(0.11f, 0.035f, t);
                    RenderSettings.fogStartDistance = 0.0f;
                    RenderSettings.fogEndDistance = Mathf.Lerp(1.4f, 7.5f, t);
                }
                else
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 0.8f);
                    RenderSettings.fogDensity = Mathf.Lerp(0.010f, 0.020f, pulse);
                    RenderSettings.fogStartDistance = 0.0f;
                    RenderSettings.fogEndDistance = 12.0f;
                }
            }
            catch
            {
            }
        }

        private enum VoidPhase
        {
            WhiteFlash,
            Transition,
            BlackVoid
        }

        private VoidPhase GetVoidPhase(float elapsed)
        {
            if (elapsed < 0.72f)
            {
                return VoidPhase.WhiteFlash;
            }

            if (elapsed < 1.25f)
            {
                return VoidPhase.Transition;
            }

            return VoidPhase.BlackVoid;
        }

        private float GetWhiteFlashStrength(float elapsed)
        {
            // 最初は真っ白に飛ばす。本家っぽい「現実が消える」一瞬。
            if (elapsed < 0.30f)
            {
                return Smooth(elapsed / 0.30f);
            }

            if (elapsed < 0.72f)
            {
                return 1.0f;
            }

            return 0.0f;
        }

        private float GetWhiteToBlackT(float elapsed)
        {
            // 0.72〜1.25秒で白→黒へギュイーン遷移。
            return Smooth(Mathf.Clamp01((elapsed - 0.72f) / 0.53f));
        }

        private Color GetVoidBackgroundColor(float elapsed)
        {
            VoidPhase phase = GetVoidPhase(elapsed);

            if (phase == VoidPhase.WhiteFlash)
            {
                return Color.white;
            }

            if (phase == VoidPhase.Transition)
            {
                float t = GetWhiteToBlackT(elapsed);
                Color mid = new Color(0.62f, 0.22f, 0.85f, 1.0f);
                Color nearBlack = new Color(0.001f, 0.001f, 0.006f, 1.0f);

                // 白→一瞬青紫→黒。単純な灰色落ちより「吸い込まれる」感じにする。
                if (t < 0.46f)
                {
                    return Color.Lerp(Color.white, mid, Smooth(t / 0.46f));
                }

                return Color.Lerp(mid, nearBlack, Smooth((t - 0.46f) / 0.54f));
            }

            // 黒空間。完全な黒より、少しだけ青紫を残す。
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 1.4f);
            return Color.Lerp(new Color(0.000f, 0.000f, 0.006f, 1.0f), new Color(0.010f, 0.010f, 0.030f, 1.0f), pulse * 0.55f);
        }

        private Color GetVoidFogColor(float elapsed)
        {
            VoidPhase phase = GetVoidPhase(elapsed);

            if (phase == VoidPhase.WhiteFlash)
            {
                return Color.white;
            }

            if (phase == VoidPhase.Transition)
            {
                float t = GetWhiteToBlackT(elapsed);
                return Color.Lerp(Color.white, new Color(0.01f, 0.012f, 0.025f, 1.0f), t);
            }

            return new Color(0.006f, 0.008f, 0.018f, 1.0f);
        }

        private void RestoreWhiteVoidCamera()
        {
            if (_cameraStateSaved && _targetCamera != null)
            {
                try
                {
                    _targetCamera.clearFlags = _savedClearFlags;
                    _targetCamera.backgroundColor = _savedBackgroundColor;
                    _targetCamera.cullingMask = _savedCullingMask;
                }
                catch
                {
                }
            }

            if (_renderSettingsSaved)
            {
                try
                {
                    RenderSettings.fog = _savedFog;
                    RenderSettings.fogColor = _savedFogColor;
                    RenderSettings.fogMode = _savedFogMode;
                    RenderSettings.fogDensity = _savedFogDensity;
                    RenderSettings.fogStartDistance = _savedFogStartDistance;
                    RenderSettings.fogEndDistance = _savedFogEndDistance;
                    RenderSettings.skybox = _savedSkybox;
                }
                catch
                {
                }
            }

            _cameraStateSaved = false;
            _renderSettingsSaved = false;
            _targetCamera = null;
        }

        private float GetWhiteStrength(float elapsed)
        {
            // エフェクトの色補間用。
            // Camera背景はGetVoidBackgroundColorで制御する。
            if (elapsed < 0.72f)
            {
                return 1.0f;
            }

            if (elapsed < 1.42f)
            {
                return Mathf.Lerp(1.0f, 0.18f, GetWhiteToBlackT(elapsed));
            }

            // 黒空間中は白寄りの線を残すが、ドーム全体は暗くする。
            return 0.16f + 0.06f * Mathf.Sin(Time.time * 2.2f);
        }

        private float GetBlackVoidStrength(float elapsed)
        {
            return Smooth(Mathf.Clamp01((elapsed - 0.75f) / 0.75f));
        }

        private void UpdateDome(float radius, float normalizedTime, float elapsed)
        {
            if (_dome == null)
            {
                return;
            }

            float open = Mathf.Clamp01(normalizedTime / 0.16f);
            float scale = Mathf.Lerp(0.25f, radius * 2.0f, Smooth(open));

            _dome.transform.localPosition = Vector3.zero;
            _dome.transform.localScale = new Vector3(scale, scale, scale);

            if (_domeMaterial != null)
            {
                float white = GetWhiteStrength(elapsed);
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4.0f);
                float alpha = Mathf.Lerp(0.09f, 0.19f, pulse);

                float black = GetBlackVoidStrength(elapsed);
                Color paleBlue = new Color(0.70f, 0.92f, 1.0f, alpha);
                Color whiteBlue = new Color(0.97f, 1.0f, 1.0f, alpha + 0.04f);
                Color darkPurple = new Color(0.15f, 0.08f, 0.32f, alpha * 0.62f);
                Color baseColor = Color.Lerp(paleBlue, whiteBlue, Mathf.Clamp01(white));
                SetMaterialColor(_domeMaterial, Color.Lerp(baseColor, darkPurple, black));
            }
        }

        private void UpdateGroundRings(float radius, float normalizedTime, float elapsed)
        {
            float white = GetWhiteStrength(elapsed);

            if (_lineMaterial != null)
            {
                float black = GetBlackVoidStrength(elapsed);
                Color blueLine = new Color(0.72f, 0.95f, 1.0f, 0.82f);
                Color whiteLine = new Color(1.0f, 1.0f, 1.0f, 0.96f);
                Color voidLine = new Color(0.62f, 0.78f, 1.0f, 0.86f);
                Color baseLine = Color.Lerp(blueLine, whiteLine, Mathf.Clamp01(white));
                SetMaterialColor(_lineMaterial, Color.Lerp(baseLine, voidLine, black));
            }

            for (int i = 0; i < _groundRings.Count; i++)
            {
                LineRenderer lr = _groundRings[i];
                if (lr == null)
                {
                    continue;
                }

                float t = Mathf.Repeat(normalizedTime + i * 0.22f + Time.time * 0.025f, 1.0f);
                float ringRadius = Mathf.Lerp(0.35f, radius * (0.88f + i * 0.05f), Smooth(t));
                float y = -0.85f + i * 0.16f;

                lr.widthMultiplier = 0.024f + i * 0.010f + white * 0.014f;
                SetRingPositions(lr, ringRadius, y, RingSegments);
            }
        }

        private void UpdateWhiteBurstRings(float radius, float elapsed)
        {
            for (int i = 0; i < _whiteBurstRings.Count; i++)
            {
                LineRenderer lr = _whiteBurstRings[i];
                if (lr == null)
                {
                    continue;
                }

                float phase = elapsed - i * 0.11f;
                bool active = phase >= 0.0f && phase <= 1.45f;
                lr.gameObject.SetActive(active);

                if (!active)
                {
                    continue;
                }

                float t = Mathf.Clamp01(phase / 1.45f);
                float r = Mathf.Lerp(0.2f, radius * 1.42f, Smooth(t));
                float y = -0.72f + i * 0.19f;

                lr.widthMultiplier = Mathf.Lerp(0.105f, 0.014f, t);
                SetRingPositions(lr, r, y, BurstRingSegments);
            }

            if (_whiteLineMaterial != null)
            {
                float whiteAlpha = Mathf.Lerp(1.0f, 0.0f, Mathf.Clamp01(elapsed / 1.50f));
                SetMaterialColor(_whiteLineMaterial, new Color(1.0f, 1.0f, 1.0f, whiteAlpha));
            }
        }

        private void UpdatePullTunnel(float radius, float elapsed)
        {
            if (_pullTunnelRoot == null)
            {
                return;
            }

            // v11:
            // 白→黒に落ちた直後、参考画像のように「画面全域から一点へ吸い込まれる」放射線を出す。
            // 0.55秒から出始め、黒空間に落ちる1.2秒付近で最大化。
            float fadeIn = Smooth(Mathf.Clamp01((elapsed - 0.52f) / 0.42f));
            float fadeOut = 1.0f - Smooth(Mathf.Clamp01((elapsed - 1.85f) / 0.55f));
            float strength = Mathf.Clamp01(fadeIn * fadeOut);

            bool active = strength > 0.02f;
            _pullTunnelRoot.SetActive(active);

            if (!active)
            {
                SetTunnelObjectsActive(false);
                return;
            }

            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;
            Vector3 up = Vector3.up;

            try
            {
                Camera cam = Camera.main;
                if (cam != null && _root != null)
                {
                    forward = _root.transform.InverseTransformDirection(cam.transform.forward).normalized;
                    right = _root.transform.InverseTransformDirection(cam.transform.right).normalized;
                    up = _root.transform.InverseTransformDirection(cam.transform.up).normalized;
                }
            }
            catch
            {
            }

            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }

            if (right.sqrMagnitude < 0.001f)
            {
                right = Vector3.right;
            }

            if (up.sqrMagnitude < 0.001f)
            {
                up = Vector3.up;
            }

            forward.Normalize();
            right.Normalize();
            up.Normalize();

            float black = GetBlackVoidStrength(elapsed);

            // 消失点をカメラ前方やや右/上の奥に置く。
            // 原作っぽく「黒い穴へ吸い込まれる」感じにするため、完全中央より少しズラす。
            Vector3 vanishingPoint =
                forward * (radius * 1.75f) +
                right * (radius * 0.12f * Mathf.Sin(Time.time * 0.85f)) +
                up * (radius * (0.10f + 0.035f * Mathf.Sin(Time.time * 1.15f)));

            UpdateVanishingPointLines(radius, strength, black, forward, right, up, vanishingPoint);
            UpdateVanishingPointRings(radius, strength, black, forward, right, up, vanishingPoint);
        }

        private void SetTunnelObjectsActive(bool active)
        {
            for (int i = 0; i < _tunnelLines.Count; i++)
            {
                if (_tunnelLines[i] != null)
                {
                    _tunnelLines[i].gameObject.SetActive(active);
                }
            }

            for (int i = 0; i < _tunnelRings.Count; i++)
            {
                if (_tunnelRings[i] != null)
                {
                    _tunnelRings[i].gameObject.SetActive(active);
                }
            }
        }

        private void UpdateVanishingPointLines(
            float radius,
            float strength,
            float black,
            Vector3 forward,
            Vector3 right,
            Vector3 up,
            Vector3 vanishingPoint)
        {
            if (_tunnelLineMaterial != null)
            {
                float flicker = 0.84f + 0.16f * Mathf.Sin(Time.time * 18.0f);
                float alpha = Mathf.Lerp(0.45f, 0.98f, black) * strength * flicker;

                Color white = new Color(1.0f, 1.0f, 1.0f, alpha);
                Color pink = new Color(1.0f, 0.26f, 0.92f, alpha * 0.95f);
                Color purple = new Color(0.54f, 0.22f, 1.0f, alpha * 0.82f);
                Color cyan = new Color(0.45f, 0.90f, 1.0f, alpha * 0.75f);

                float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * 3.0f);
                Color colorA = Color.Lerp(white, pink, wave);
                Color colorB = Color.Lerp(cyan, purple, 0.5f + 0.5f * Mathf.Sin(Time.time * 2.1f));
                SetMaterialColor(_tunnelLineMaterial, Color.Lerp(colorA, colorB, 0.42f * black));
            }

            float rush = Time.time * 4.65f;
            float spin = Time.time * 2.2f;

            // 画面の手前側/周辺から消失点へ向かう長い線。
            // 3D空間上では、消失点より手前に大きな円盤を置き、その外周から一点へ伸ばす。
            for (int i = 0; i < _tunnelLines.Count; i++)
            {
                LineRenderer lr = _tunnelLines[i];
                if (lr == null)
                {
                    continue;
                }

                lr.gameObject.SetActive(true);

                float seed = (float)i / Mathf.Max(1, _tunnelLines.Count);
                float lane = Mathf.Repeat(seed + rush, 1.0f);
                float angle = seed * Mathf.PI * 2.0f + spin * (0.25f + 0.75f * seed);

                // 画面全体に散らすため、円周だけでなく半径にもばらつきを入れる。
                float radialNoise = 0.62f + 0.48f * Mathf.Abs(Mathf.Sin(seed * 37.19f + Time.time * 0.9f));
                float spread = radius * Mathf.Lerp(0.55f, 1.72f, radialNoise);

                // レーンが流れることで線が奥へ走るように見える。
                float frontDepth = Mathf.Lerp(radius * 0.12f, radius * 0.95f, lane);
                float backDepth = Mathf.Lerp(radius * 2.40f, radius * 1.30f, lane);

                Vector3 sideDir = Mathf.Cos(angle) * right + Mathf.Sin(angle) * up;

                Vector3 outer = forward * frontDepth + sideDir * spread + up * 0.35f;
                Vector3 inner = Vector3.Lerp(outer, vanishingPoint, Mathf.Lerp(0.72f, 0.96f, black));

                // 線の長さを伸ばす。消失点に向かって鋭く収束させる。
                Vector3 p1 = outer;
                Vector3 p2 = inner + forward * (0.22f * radius * lane);

                // 一部の線は消失点を少し越える。参考画像の長いスピード線っぽくする。
                if ((i % 5) == 0)
                {
                    p2 = Vector3.Lerp(p2, vanishingPoint + forward * radius * 0.75f, 0.55f);
                }

                float widthPulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 13.0f + i * 0.71f);
                float thick = ((i % 7) == 0) ? 1.75f : 1.0f;
                lr.widthMultiplier = Mathf.Lerp(0.010f, 0.041f, strength) * widthPulse * thick;

                lr.SetPosition(0, p1);
                lr.SetPosition(1, p2);
            }
        }

        private void UpdateVanishingPointRings(
            float radius,
            float strength,
            float black,
            Vector3 forward,
            Vector3 right,
            Vector3 up,
            Vector3 vanishingPoint)
        {
            if (_tunnelRingMaterial != null)
            {
                float alpha = Mathf.Lerp(0.0f, 0.72f, strength) * Mathf.Lerp(0.6f, 1.0f, black);
                Color magenta = new Color(1.0f, 0.28f, 0.92f, alpha);
                Color violet = new Color(0.50f, 0.24f, 1.0f, alpha * 0.88f);
                Color white = new Color(1.0f, 1.0f, 1.0f, alpha * 0.82f);
                SetMaterialColor(_tunnelRingMaterial, Color.Lerp(Color.Lerp(magenta, violet, 0.55f), white, 0.20f + 0.20f * Mathf.Sin(Time.time * 3.5f)));
            }

            // リングは主役ではなく、消失点付近の歪み/ブラックホール感用に控えめ。
            for (int i = 0; i < _tunnelRings.Count; i++)
            {
                LineRenderer lr = _tunnelRings[i];
                if (lr == null)
                {
                    continue;
                }

                lr.gameObject.SetActive(true);

                float seed = (float)i / Mathf.Max(1, _tunnelRings.Count);
                float flow = Mathf.Repeat(seed + Time.time * 1.15f, 1.0f);

                float depth = Mathf.Lerp(radius * 1.70f, radius * 0.90f, flow);
                float ringRadius = Mathf.Lerp(radius * 0.82f, radius * 0.16f, flow) * Mathf.Lerp(0.55f, 1.0f, black);
                float wobble = 1.0f + 0.15f * Mathf.Sin(Time.time * 4.0f + i * 1.3f);

                Vector3 center = Vector3.Lerp(forward * depth + up * 0.35f, vanishingPoint, 0.45f + 0.45f * flow);
                float rot = Time.time * (2.8f + i * 0.25f) + i * 0.65f;

                lr.widthMultiplier = Mathf.Lerp(0.008f, 0.025f, strength) * Mathf.Lerp(0.8f, 0.35f, flow);

                for (int s = 0; s <= TunnelRingSegments; s++)
                {
                    float a = (Mathf.PI * 2.0f * s) / TunnelRingSegments + rot;
                    float localR = ringRadius * wobble * (1.0f + 0.08f * Mathf.Sin(a * 5.0f + Time.time * 7.0f));
                    Vector3 p = center + (Mathf.Cos(a) * right + Mathf.Sin(a) * up) * localR;
                    lr.SetPosition(s, p);
                }
            }
        }

        private void UpdateBlackHoleUniverse(float radius, float elapsed)
        {
            if (_universeImageRoot == null || _universeImageQuad == null)
            {
                return;
            }

            // v16:
            // コード生成の銀河/ブラックホールをやめ、ギュイーン後に1枚絵の宇宙背景を出す。
            // 1.35秒あたりから出現し、以後は静かな最終背景として残す。
            float appear = Smooth(Mathf.Clamp01((elapsed - 1.35f) / 0.65f));
            float strength = Mathf.Clamp01(appear);

            bool active = strength > 0.02f;
            _universeImageRoot.SetActive(active);
            _universeImageQuad.SetActive(active);

            if (!active)
            {
                SetBlackHoleObjectsActive(false);
                return;
            }

            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;
            Vector3 up = Vector3.up;

            try
            {
                Camera cam = Camera.main;
                if (cam != null && _root != null)
                {
                    forward = _root.transform.InverseTransformDirection(cam.transform.forward).normalized;
                    right = _root.transform.InverseTransformDirection(cam.transform.right).normalized;
                    up = _root.transform.InverseTransformDirection(cam.transform.up).normalized;
                }
            }
            catch
            {
            }

            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }

            if (right.sqrMagnitude < 0.001f)
            {
                right = Vector3.right;
            }

            if (up.sqrMagnitude < 0.001f)
            {
                up = Vector3.up;
            }

            forward.Normalize();
            right.Normalize();
            up.Normalize();

            // 背景画像はカメラ前方奥に大きく貼る。
            // 画像自体が「左にブラックホール、右に余白」構図なので、Quadは中央配置でOK。
            float distance = Mathf.Max(radius * 2.65f, 7.0f);
            Vector3 imageCenter =
                forward * distance +
                up * (radius * 0.10f);

            _universeImageQuad.transform.localPosition = imageCenter;
            _universeImageQuad.transform.localRotation = Quaternion.LookRotation(forward, up);

            float width = Mathf.Max(radius * 5.80f, 16.0f);
            float height = width * 9.0f / 16.0f;
            float popScale = Mathf.Lerp(0.82f, 1.0f, strength);
            _universeImageQuad.transform.localScale = new Vector3(width * popScale, height * popScale, 1.0f);

            if (!_universeQuadDebugLogged && strength > 0.95f)
            {
                _universeQuadDebugLogged = true;
                MelonLogger.Msg(
                    "[" + VersionTag + "] Universe quad visible debug: " +
                    "LocalPos=" + FormatVector(_universeImageQuad.transform.localPosition) +
                    " LocalScale=" + FormatVector(_universeImageQuad.transform.localScale) +
                    " Distance=" + distance.ToString("0.00") +
                    " Width=" + width.ToString("0.00") +
                    " Height=" + height.ToString("0.00") +
                    " TextureLoaded=" + _universeTextureLoaded +
                    " Texture=" + (_universeTexture == null ? "null" : (_universeTexture.width + "x" + _universeTexture.height))
                );
            }

            if (_universeImageMaterial != null)
            {
                float alpha = Mathf.Lerp(0.0f, 1.0f, strength);
                Color tint = new Color(1.0f, 1.0f, 1.0f, alpha);
                SetMaterialColor(_universeImageMaterial, tint);
            }
        }

        private void SetBlackHoleObjectsActive(bool active)
        {
            if (_universeImageRoot != null)
            {
                _universeImageRoot.SetActive(active);
            }

            if (_universeImageQuad != null)
            {
                _universeImageQuad.SetActive(active);
            }

            // v15以前のコード生成ブラックホール用オブジェクトが残っていた場合の保険。
            if (_blackHoleCore != null)
            {
                _blackHoleCore.SetActive(active);
            }

            for (int i = 0; i < _blackHoleRings.Count; i++)
            {
                if (_blackHoleRings[i] != null)
                {
                    _blackHoleRings[i].gameObject.SetActive(active);
                }
            }

            for (int i = 0; i < _galaxyArms.Count; i++)
            {
                if (_galaxyArms[i] != null)
                {
                    _galaxyArms[i].gameObject.SetActive(active);
                }
            }

            for (int i = 0; i < _nebulaVeils.Count; i++)
            {
                if (_nebulaVeils[i] != null)
                {
                    _nebulaVeils[i].gameObject.SetActive(active);
                }
            }

            for (int i = 0; i < _blackHoleStarStreaks.Count; i++)
            {
                if (_blackHoleStarStreaks[i] != null)
                {
                    _blackHoleStarStreaks[i].gameObject.SetActive(active);
                }
            }
        }

        private void UpdateBlackHoleCore(
            float radius,
            float strength,
            Vector3 forward,
            Vector3 right,
            Vector3 up,
            Vector3 center)
        {
            if (_blackHoleCore == null)
            {
                return;
            }

            _blackHoleCore.SetActive(true);
            _blackHoleCore.transform.localPosition = center;

            // 黒背景に埋もれないよう、中心は黒、外周リングで輪郭を見せる。
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 0.85f);
            float coreScale = radius * Mathf.Lerp(0.34f, 0.62f, strength) * (0.98f + 0.025f * pulse);
            _blackHoleCore.transform.localScale = new Vector3(coreScale, coreScale, coreScale);

            if (_blackHoleCoreMaterial != null)
            {
                SetMaterialColor(_blackHoleCoreMaterial, new Color(0.0f, 0.0f, 0.0f, Mathf.Lerp(0.78f, 1.0f, strength)));
            }
        }

        private void UpdateRichGalaxyNebula(
            float radius,
            float strength,
            Vector3 forward,
            Vector3 right,
            Vector3 up,
            Vector3 center)
        {
            UpdateGalaxySpiralArms(radius, strength, forward, right, up, center);
            UpdateNebulaVeils(radius, strength, forward, right, up, center);
        }

        private void UpdateGalaxySpiralArms(
            float radius,
            float strength,
            Vector3 forward,
            Vector3 right,
            Vector3 up,
            Vector3 center)
        {
            if (_galaxyArmMaterial != null)
            {
                float slowPulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 0.55f);
                float alpha = Mathf.Lerp(0.0f, 0.58f, strength) * (0.86f + 0.14f * slowPulse);
                Color blue = new Color(0.32f, 0.68f, 1.0f, alpha * 0.78f);
                Color violet = new Color(0.66f, 0.34f, 1.0f, alpha * 0.70f);
                Color milky = new Color(0.82f, 0.94f, 1.0f, alpha * 0.62f);
                SetMaterialColor(_galaxyArmMaterial, Color.Lerp(Color.Lerp(blue, violet, slowPulse), milky, 0.34f));
            }

            float baseRotation = Time.time * 0.18f;

            for (int i = 0; i < _galaxyArms.Count; i++)
            {
                LineRenderer lr = _galaxyArms[i];
                if (lr == null)
                {
                    continue;
                }

                lr.gameObject.SetActive(true);

                float seed = (float)i / Mathf.Max(1, _galaxyArms.Count);
                float phase = seed * Mathf.PI * 2.0f + baseRotation;
                float armOffset = seed * 0.55f;

                // 銀河の腕。ブラックホールを中心に、ゆるい渦を大きく描く。
                // 速く吸い込まれる線ではなく、静かな銀河円盤として見せる。
                for (int s = 0; s <= GalaxyArmSegments; s++)
                {
                    float t = (float)s / GalaxyArmSegments;

                    float angle = phase + t * Mathf.PI * 2.15f + Mathf.Sin(t * 5.5f + i) * 0.10f;
                    float radial = radius * Mathf.Lerp(0.46f, 2.05f, t);
                    float thicknessWave = 1.0f + 0.08f * Mathf.Sin(t * 18.0f + Time.time * 0.65f + i);

                    Vector3 disk =
                        right * Mathf.Cos(angle) * radial * thicknessWave +
                        up * Mathf.Sin(angle) * radial * 0.26f * thicknessWave;

                    // わずかな奥行きと歪み。VRで平面一枚に見えにくくする。
                    Vector3 depth =
                        forward * (Mathf.Sin(angle * 1.7f + armOffset) * radial * 0.045f) +
                        up * (Mathf.Sin(t * Mathf.PI * 3.0f + i) * radius * 0.025f);

                    Vector3 p = center + disk + depth;
                    lr.SetPosition(s, p);
                }

                lr.widthMultiplier = Mathf.Lerp(0.010f, 0.048f, strength) * Mathf.Lerp(1.20f, 0.75f, seed);
            }
        }

        private void UpdateNebulaVeils(
            float radius,
            float strength,
            Vector3 forward,
            Vector3 right,
            Vector3 up,
            Vector3 center)
        {
            if (_nebulaVeilMaterial != null)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 0.42f);
                float alpha = Mathf.Lerp(0.0f, 0.28f, strength) * (0.85f + 0.15f * pulse);
                Color deepBlue = new Color(0.10f, 0.22f, 0.55f, alpha * 0.75f);
                Color violet = new Color(0.42f, 0.22f, 0.72f, alpha);
                Color pale = new Color(0.70f, 0.86f, 1.0f, alpha * 0.35f);
                SetMaterialColor(_nebulaVeilMaterial, Color.Lerp(Color.Lerp(deepBlue, violet, pulse), pale, 0.18f));
            }

            float slow = Time.time * 0.08f;

            for (int i = 0; i < _nebulaVeils.Count; i++)
            {
                LineRenderer lr = _nebulaVeils[i];
                if (lr == null)
                {
                    continue;
                }

                lr.gameObject.SetActive(true);

                float seed = (float)i / Mathf.Max(1, _nebulaVeils.Count);
                float veilRadius = radius * Mathf.Lerp(1.20f, 2.70f, seed);
                float squash = Mathf.Lerp(0.18f, 0.40f, seed);
                float rot = slow + seed * Mathf.PI * 2.0f;

                Vector3 offset =
                    right * Mathf.Sin(seed * 7.3f) * radius * 0.22f +
                    up * Mathf.Cos(seed * 5.1f) * radius * 0.10f +
                    forward * Mathf.Sin(seed * 3.9f) * radius * 0.12f;

                for (int s = 0; s <= NebulaVeilSegments; s++)
                {
                    float a = (Mathf.PI * 2.0f * s) / NebulaVeilSegments + rot;
                    float noisy = 1.0f + 0.18f * Mathf.Sin(a * 3.0f + Time.time * 0.35f + i) + 0.08f * Mathf.Sin(a * 8.0f + i);
                    Vector3 p =
                        center +
                        offset +
                        right * Mathf.Cos(a) * veilRadius * noisy +
                        up * Mathf.Sin(a) * veilRadius * squash * noisy +
                        forward * Mathf.Sin(a * 2.0f + i) * radius * 0.05f;

                    lr.SetPosition(s, p);
                }

                lr.widthMultiplier = Mathf.Lerp(0.020f, 0.090f, strength) * Mathf.Lerp(1.25f, 0.65f, seed);
            }
        }

        private void UpdateBlackHoleAccretionRings(
            float radius,
            float strength,
            Vector3 forward,
            Vector3 right,
            Vector3 up,
            Vector3 center)
        {
            if (_blackHoleRingMaterial != null)
            {
                float alpha = Mathf.Lerp(0.0f, 0.88f, strength);
                Color paleBlue = new Color(0.58f, 0.86f, 1.0f, alpha * 0.72f);
                Color mistWhite = new Color(0.92f, 0.98f, 1.0f, alpha * 0.82f);
                Color softViolet = new Color(0.60f, 0.48f, 1.0f, alpha * 0.45f);
                float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * 0.95f);
                SetMaterialColor(_blackHoleRingMaterial, Color.Lerp(Color.Lerp(paleBlue, mistWhite, 0.55f), softViolet, 0.22f * wave));
            }

            float spin = Time.time * 0.85f;

            for (int i = 0; i < _blackHoleRings.Count; i++)
            {
                LineRenderer lr = _blackHoleRings[i];
                if (lr == null)
                {
                    continue;
                }

                lr.gameObject.SetActive(true);

                float seed = (float)i / Mathf.Max(1, _blackHoleRings.Count);
                float orbitRadius = radius * Mathf.Lerp(0.48f, 1.38f, seed);
                float squash = Mathf.Lerp(0.16f, 0.34f, seed);
                float tilt = Mathf.Lerp(-0.42f, 0.42f, seed) + Mathf.Sin(Time.time * 0.8f + i) * 0.06f;
                float rot = spin * Mathf.Lerp(1.35f, 0.45f, seed) + seed * Mathf.PI * 2.0f;

                lr.widthMultiplier = Mathf.Lerp(0.008f, 0.024f, strength) * Mathf.Lerp(1.25f, 0.58f, seed);

                for (int s = 0; s <= BlackHoleRingSegments; s++)
                {
                    float a = (Mathf.PI * 2.0f * s) / BlackHoleRingSegments + rot;

                    // 円盤を楕円にして、ブラックホールの降着円盤っぽくする。
                    float r = orbitRadius * (1.0f + 0.05f * Mathf.Sin(a * 5.0f + Time.time * 6.0f + i));
                    Vector3 radial = right * Mathf.Cos(a) * r + up * Mathf.Sin(a) * r * squash;

                    // 少し奥行き方向にねじる。VRでも平板感を減らす。
                    Vector3 depthWarp = forward * (Mathf.Sin(a + tilt) * orbitRadius * 0.045f);

                    Vector3 p = center + radial + depthWarp;
                    lr.SetPosition(s, p);
                }
            }
        }

        private void UpdateBlackHoleStarStreaks(
            float radius,
            float strength,
            Vector3 forward,
            Vector3 right,
            Vector3 up,
            Vector3 center)
        {
            if (_blackHoleStarMaterial != null)
            {
                float flicker = 0.88f + 0.12f * Mathf.Sin(Time.time * 3.2f);
                float alpha = Mathf.Lerp(0.0f, 0.54f, strength) * flicker;
                Color white = new Color(0.92f, 0.98f, 1.0f, alpha);
                Color blue = new Color(0.42f, 0.74f, 1.0f, alpha * 0.72f);
                Color violet = new Color(0.68f, 0.52f, 1.0f, alpha * 0.50f);
                SetMaterialColor(_blackHoleStarMaterial, Color.Lerp(Color.Lerp(blue, violet, 0.25f + 0.25f * Mathf.Sin(Time.time * 0.8f)), white, 0.55f));
            }

            // v14:
            // 星は吸い込み線ではなく、静かな宇宙の点/短い霞として扱う。
            // ごくゆっくり漂わせるだけにして、ギュイーン空間との差を出す。
            float drift = Time.time * 0.10f;
            float twinkleTime = Time.time * 2.6f;

            for (int i = 0; i < _blackHoleStarStreaks.Count; i++)
            {
                LineRenderer lr = _blackHoleStarStreaks[i];
                if (lr == null)
                {
                    continue;
                }

                lr.gameObject.SetActive(true);

                float seed = (float)i / Mathf.Max(1, _blackHoleStarStreaks.Count);
                float angle = seed * Mathf.PI * 2.0f * 5.0f + drift + Mathf.Sin(seed * 31.7f) * 0.65f;
                float ring = radius * Mathf.Lerp(0.65f, 3.05f, Mathf.Abs(Mathf.Sin(seed * 21.13f)));
                float height = radius * Mathf.Lerp(-1.25f, 1.25f, Mathf.Abs(Mathf.Sin(seed * 17.71f + 1.7f)));

                Vector3 side = Mathf.Cos(angle) * right + Mathf.Sin(angle) * up;
                Vector3 star = center + side * ring + up * height * 0.35f + forward * radius * Mathf.Lerp(-0.35f, 0.55f, Mathf.Abs(Mathf.Sin(seed * 9.43f)));

                float twinkle = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(twinkleTime + seed * 19.0f));
                float length = radius * Mathf.Lerp(0.010f, 0.045f, twinkle) * strength;

                // 点に見えるくらい短い線。たまに少しだけ霞の筋にする。
                if ((i % 13) == 0)
                {
                    length *= 3.0f;
                }

                Vector3 tangent = (-Mathf.Sin(angle) * right + Mathf.Cos(angle) * up).normalized;
                Vector3 p1 = star - tangent * length;
                Vector3 p2 = star + tangent * length;

                float width = Mathf.Lerp(0.0028f, 0.010f, strength) * Mathf.Lerp(0.65f, 1.45f, twinkle);
                if ((i % 17) == 0)
                {
                    width *= 1.7f;
                }

                lr.widthMultiplier = width;
                lr.SetPosition(0, p1);
                lr.SetPosition(1, p2);
            }
        }

        private void UpdateTargetRings(List<Vector3> targetPositions, float elapsed)
        {
            int count = targetPositions == null ? 0 : Mathf.Min(targetPositions.Count, _targetRings.Count);

            float reveal = Mathf.Clamp01((elapsed - 0.85f) / 0.65f);
            float alpha = Mathf.Lerp(0.0f, 0.96f, Smooth(reveal));

            if (_targetMaterial != null)
            {
                SetMaterialColor(_targetMaterial, new Color(0.92f, 0.98f, 1.0f, alpha));
            }

            for (int i = 0; i < _targetRings.Count; i++)
            {
                LineRenderer lr = _targetRings[i];
                if (lr == null)
                {
                    continue;
                }

                GameObject obj = lr.gameObject;
                if (i >= count || alpha <= 0.02f)
                {
                    obj.SetActive(false);
                    continue;
                }

                obj.SetActive(true);

                Vector3 world = targetPositions[i];
                Vector3 local = _root.transform.InverseTransformPoint(world);

                obj.transform.localPosition = local + Vector3.up * 0.22f;
                obj.transform.localRotation = Quaternion.Euler(90f, Time.time * 55.0f + i * 19f, 0f);

                float r = 0.24f + 0.035f * Mathf.Sin(Time.time * 7.0f + i);
                SetLocalRingPositions(lr, r, TargetRingSegments);
            }
        }

        private Texture2D LoadUniverseTextureOrFallback()
        {
            _universeTextureLoadAttempted = true;
            _universeTextureLoaded = false;
            _loadedUniverseImagePath = null;

            LogUniverseDebugEnvironment();

            string[] pngPaths = GetUniverseCandidatePaths(UniversePngFileName);
            LogCandidatePaths("PNG", pngPaths);
            LogUniverseFolderContents("PNG", pngPaths);

            string loadedPath;
            Texture2D pngTexture = TryLoadPngTextureByReflection(pngPaths, out loadedPath);
            if (pngTexture != null)
            {
                _universeTextureLoaded = true;
                _loadedUniverseImagePath = loadedPath;
                return pngTexture;
            }

            // PNGデコードがIL2CPP参照環境で失敗する場合の保険。
            // 同梱のRGBA生データはTexture2D.SetPixels32だけで読めるので、ImageConversion参照に依存しない。
            string[] rawPaths = GetUniverseCandidatePaths(UniverseRawFileName);
            LogCandidatePaths("RAW", rawPaths);
            LogUniverseFolderContents("RAW", rawPaths);

            Texture2D rawTexture = TryLoadRawRgbaTexture(rawPaths, UniverseRawWidth, UniverseRawHeight, out loadedPath);
            if (rawTexture != null)
            {
                _universeTextureLoaded = true;
                _loadedUniverseImagePath = loadedPath;
                return rawTexture;
            }

            return CreateFallbackUniverseTexture();
        }

        private Texture2D TryLoadPngTextureByReflection(string[] paths, out string loadedPath)
        {
            loadedPath = null;

            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                try
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    FileInfo info = new FileInfo(path);
                    MelonLogger.Msg("[" + VersionTag + "] PNG candidate found: " + path + " Size=" + info.Length + " bytes");

                    byte[] bytes = File.ReadAllBytes(path);
                    LogPngBytesDebug(path, bytes);
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    tex.name = "Gojo_Domain_BlackHoleBackground_Texture_v21";

                    if (TryLoadImageByReflection(tex, bytes))
                    {
                        ApplyUniverseTextureSettings(tex);
                        loadedPath = path;
                        return tex;
                    }

                    MelonLogger.Warning("[" + VersionTag + "] PNG exists but Unity ImageConversion failed: " + path);

                    try
                    {
                        UnityEngine.Object.Destroy(tex);
                    }
                    catch
                    {
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning("[" + VersionTag + "] Failed to read PNG: " + path + " / " + ex.Message);
                }
            }

            return null;
        }

        private Texture2D TryLoadRawRgbaTexture(string[] paths, int width, int height, out string loadedPath)
        {
            loadedPath = null;
            int pixelCount = width * height;
            int expectedBytes = pixelCount * 4;

            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                try
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    FileInfo info = new FileInfo(path);
                    MelonLogger.Msg("[" + VersionTag + "] RAW candidate found: " + path + " Size=" + info.Length + " bytes");

                    byte[] bytes = File.ReadAllBytes(path);
                    if (bytes == null || bytes.Length != expectedBytes)
                    {
                        MelonLogger.Warning(
                            "[" + VersionTag + "] RAW size mismatch: " + path +
                            " Expected=" + expectedBytes + " Actual=" + (bytes == null ? 0 : bytes.Length)
                        );
                        continue;
                    }

                    Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    tex.name = "Gojo_Domain_BlackHoleBackground_RAWTexture_v21";

                    Color32[] pixels = new Color32[pixelCount];
                    for (int p = 0; p < pixelCount; p++)
                    {
                        int b = p * 4;
                        pixels[p] = new Color32(bytes[b], bytes[b + 1], bytes[b + 2], bytes[b + 3]);
                    }

                    tex.SetPixels32(pixels);
                    tex.Apply(false, false);
                    ApplyUniverseTextureSettings(tex);

                    loadedPath = path;
                    return tex;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning("[" + VersionTag + "] Failed to load RAW: " + path + " / " + ex.Message);
                }
            }

            return null;
        }

        private string[] GetUniverseCandidatePaths(string fileName)
        {
            List<string> paths = new List<string>();

            string current = null;
            string baseDir = null;
            string appData = null;
            string appRoot = null;
            string melonUserData = null;

            try
            {
                current = Directory.GetCurrentDirectory();
            }
            catch
            {
            }

            try
            {
                baseDir = AppDomain.CurrentDomain.BaseDirectory;
            }
            catch
            {
            }

            try
            {
                appData = Application.dataPath;
                if (!string.IsNullOrEmpty(appData))
                {
                    appRoot = Directory.GetParent(appData).FullName;
                }
            }
            catch
            {
            }

            melonUserData = TryGetMelonEnvironmentString("UserDataDirectory");

            AddUniverseCandidatePath(paths, Path.Combine("UserData", "IAmCatGojoMod", fileName));
            AddUniverseCandidatePath(paths, Path.Combine("IAmCatGojoMod", fileName));
            AddUniverseCandidatePath(paths, fileName);

            if (!string.IsNullOrEmpty(appRoot))
            {
                AddUniverseCandidatePath(paths, Path.Combine(appRoot, "UserData", "IAmCatGojoMod", fileName));
            }

            if (!string.IsNullOrEmpty(melonUserData))
            {
                AddUniverseCandidatePath(paths, Path.Combine(melonUserData, "IAmCatGojoMod", fileName));
                AddUniverseCandidatePath(paths, Path.Combine(melonUserData, fileName));
            }

            if (!string.IsNullOrEmpty(current))
            {
                AddUniverseCandidatePath(paths, Path.Combine(current, "UserData", "IAmCatGojoMod", fileName));
                AddUniverseCandidatePath(paths, Path.Combine(current, "IAmCatGojoMod", fileName));
                AddUniverseCandidatePath(paths, Path.Combine(current, fileName));
            }

            if (!string.IsNullOrEmpty(baseDir))
            {
                AddUniverseCandidatePath(paths, Path.Combine(baseDir, "UserData", "IAmCatGojoMod", fileName));
                AddUniverseCandidatePath(paths, Path.Combine(baseDir, "IAmCatGojoMod", fileName));
                AddUniverseCandidatePath(paths, Path.Combine(baseDir, fileName));
            }

            return paths.ToArray();
        }

        private void AddUniverseCandidatePath(List<string> paths, string path)
        {
            if (paths == null || string.IsNullOrEmpty(path))
            {
                return;
            }

            string normalized = path;
            try
            {
                normalized = Path.GetFullPath(path);
            }
            catch
            {
            }

            for (int i = 0; i < paths.Count; i++)
            {
                if (string.Equals(paths[i], normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            paths.Add(normalized);
        }

        private void LogCandidatePaths(string kind, string[] paths)
        {
            if (paths == null)
            {
                return;
            }

            try
            {
                MelonLogger.Msg("[" + VersionTag + "] " + kind + " search paths:");
                for (int i = 0; i < paths.Length; i++)
                {
                    string path = paths[i];
                    bool exists = false;
                    bool dirExists = false;
                    long size = -1;
                    string dir = "";

                    try
                    {
                        exists = File.Exists(path);
                    }
                    catch
                    {
                    }

                    try
                    {
                        dir = Path.GetDirectoryName(path);
                        dirExists = !string.IsNullOrEmpty(dir) && Directory.Exists(dir);
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (exists)
                        {
                            size = new FileInfo(path).Length;
                        }
                    }
                    catch
                    {
                    }

                    MelonLogger.Msg(
                        "[" + VersionTag + "]   " + kind + "[" + i + "] " + path +
                        " Exists=" + exists +
                        " Size=" + size +
                        " DirExists=" + dirExists +
                        " Dir=" + dir
                    );
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[" + VersionTag + "] LogCandidatePaths failed: " + ex.Message);
            }
        }

        private void LogUniverseDebugEnvironment()
        {
            if (_universeDebugEnvironmentLogged)
            {
                return;
            }

            _universeDebugEnvironmentLogged = true;

            MelonLogger.Msg("[" + VersionTag + "] ===== Universe Image Debug Environment =====");
            LogDebugPathValue("Directory.GetCurrentDirectory", SafeGetCurrentDirectory());
            LogDebugPathValue("AppDomain.CurrentDomain.BaseDirectory", SafeGetBaseDirectory());
            LogDebugPathValue("Application.dataPath", SafeGetApplicationPath("dataPath"));
            LogDebugPathValue("Application.persistentDataPath", SafeGetApplicationPath("persistentDataPath"));
            LogDebugPathValue("Application.streamingAssetsPath", SafeGetApplicationPath("streamingAssetsPath"));
            LogDebugPathValue("MelonEnvironment.UserDataDirectory", TryGetMelonEnvironmentString("UserDataDirectory"));
            LogDebugPathValue("MelonEnvironment.ModsDirectory", TryGetMelonEnvironmentString("ModsDirectory"));
            LogDebugPathValue("MelonEnvironment.GameRootDirectory", TryGetMelonEnvironmentString("GameRootDirectory"));
            LogDebugPathValue("Expected PNG Relative", UniverseImageRelativePath);
            LogDebugPathValue("Expected RAW Relative", UniverseRawRelativePath);
            MelonLogger.Msg("[" + VersionTag + "] =========================================");
        }

        private void LogDebugPathValue(string label, string value)
        {
            try
            {
                MelonLogger.Msg("[" + VersionTag + "] " + label + " = " + (string.IsNullOrEmpty(value) ? "<null/empty>" : value));
            }
            catch
            {
            }
        }

        private string SafeGetCurrentDirectory()
        {
            try
            {
                return Directory.GetCurrentDirectory();
            }
            catch (Exception ex)
            {
                return "<error: " + ex.Message + ">";
            }
        }

        private string SafeGetBaseDirectory()
        {
            try
            {
                return AppDomain.CurrentDomain.BaseDirectory;
            }
            catch (Exception ex)
            {
                return "<error: " + ex.Message + ">";
            }
        }

        private string SafeGetApplicationPath(string propertyName)
        {
            try
            {
                if (propertyName == "dataPath")
                {
                    return Application.dataPath;
                }
                if (propertyName == "persistentDataPath")
                {
                    return Application.persistentDataPath;
                }
                if (propertyName == "streamingAssetsPath")
                {
                    return Application.streamingAssetsPath;
                }
            }
            catch (Exception ex)
            {
                return "<error: " + ex.Message + ">";
            }

            return null;
        }

        private string TryGetMelonEnvironmentString(string propertyName)
        {
            try
            {
                Type type = Type.GetType("MelonLoader.MelonEnvironment, MelonLoader");
                if (type == null)
                {
                    type = typeof(MelonLogger).Assembly.GetType("MelonLoader.MelonEnvironment");
                }

                if (type == null)
                {
                    return null;
                }

                PropertyInfo prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
                if (prop != null)
                {
                    object value = prop.GetValue(null, null);
                    return value == null ? null : value.ToString();
                }

                FieldInfo field = type.GetField(propertyName, BindingFlags.Public | BindingFlags.Static);
                if (field != null)
                {
                    object value = field.GetValue(null);
                    return value == null ? null : value.ToString();
                }
            }
            catch
            {
            }

            return null;
        }

        private void LogUniverseFolderContents(string kind, string[] candidatePaths)
        {
            if (candidatePaths == null)
            {
                return;
            }

            List<string> dirs = new List<string>();

            for (int i = 0; i < candidatePaths.Length; i++)
            {
                try
                {
                    string dir = Path.GetDirectoryName(candidatePaths[i]);
                    if (string.IsNullOrEmpty(dir))
                    {
                        continue;
                    }

                    bool exists = false;
                    for (int d = 0; d < dirs.Count; d++)
                    {
                        if (string.Equals(dirs[d], dir, StringComparison.OrdinalIgnoreCase))
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (!exists)
                    {
                        dirs.Add(dir);
                    }
                }
                catch
                {
                }
            }

            for (int i = 0; i < dirs.Count; i++)
            {
                string dir = dirs[i];
                try
                {
                    bool exists = Directory.Exists(dir);
                    MelonLogger.Msg("[" + VersionTag + "] " + kind + " dir[" + i + "] " + dir + " Exists=" + exists);

                    if (!exists)
                    {
                        continue;
                    }

                    string[] files = Directory.GetFiles(dir);
                    MelonLogger.Msg("[" + VersionTag + "] " + kind + " dir[" + i + "] FileCount=" + files.Length);

                    int max = Mathf.Min(files.Length, 32);
                    for (int f = 0; f < max; f++)
                    {
                        string file = files[f];
                        long size = -1;
                        try
                        {
                            size = new FileInfo(file).Length;
                        }
                        catch
                        {
                        }

                        MelonLogger.Msg("[" + VersionTag + "]   file[" + f + "] " + Path.GetFileName(file) + " Size=" + size);
                    }

                    if (files.Length > max)
                    {
                        MelonLogger.Msg("[" + VersionTag + "]   ... " + (files.Length - max) + " more files omitted");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning("[" + VersionTag + "] Failed to list dir: " + dir + " / " + ex.Message);
                }
            }
        }

        private void LogPngBytesDebug(string path, byte[] bytes)
        {
            try
            {
                bool signatureOk = false;
                if (bytes != null && bytes.Length >= 8)
                {
                    signatureOk =
                        bytes[0] == 0x89 &&
                        bytes[1] == 0x50 &&
                        bytes[2] == 0x4E &&
                        bytes[3] == 0x47 &&
                        bytes[4] == 0x0D &&
                        bytes[5] == 0x0A &&
                        bytes[6] == 0x1A &&
                        bytes[7] == 0x0A;
                }

                MelonLogger.Msg(
                    "[" + VersionTag + "] PNG bytes debug: " + path +
                    " Length=" + (bytes == null ? -1 : bytes.Length) +
                    " Header=" + BytesToHex(bytes, 16) +
                    " PngSignatureOk=" + signatureOk
                );
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[" + VersionTag + "] LogPngBytesDebug failed: " + ex.Message);
            }
        }

        private string BytesToHex(byte[] bytes, int maxBytes)
        {
            if (bytes == null)
            {
                return "<null>";
            }

            int count = Mathf.Min(bytes.Length, maxBytes);
            char[] c = new char[count * 3];
            const string hex = "0123456789ABCDEF";

            for (int i = 0; i < count; i++)
            {
                byte b = bytes[i];
                c[i * 3 + 0] = hex[b >> 4];
                c[i * 3 + 1] = hex[b & 0x0F];
                c[i * 3 + 2] = ' ';
            }

            return new string(c);
        }

        private void LogLoadedUniverseTextureInfo()
        {
            try
            {
                if (_universeTexture == null)
                {
                    MelonLogger.Warning("[" + VersionTag + "] Universe texture is null after load.");
                    return;
                }

                MelonLogger.Msg(
                    "[" + VersionTag + "] Universe texture final: " +
                    "Loaded=" + _universeTextureLoaded +
                    " Path=" + (_loadedUniverseImagePath ?? "<fallback>") +
                    " Size=" + _universeTexture.width + "x" + _universeTexture.height +
                    " Format=" + _universeTexture.format +
                    " Name=" + _universeTexture.name
                );
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[" + VersionTag + "] LogLoadedUniverseTextureInfo failed: " + ex.Message);
            }
        }

        private void LogImageConversionMethods(Type imageConversionType)
        {
            try
            {
                if (imageConversionType == null)
                {
                    return;
                }

                MethodInfo[] methods = imageConversionType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                int printed = 0;
                for (int i = 0; i < methods.Length && printed < 24; i++)
                {
                    MethodInfo m = methods[i];
                    if (m == null || m.Name != "LoadImage")
                    {
                        continue;
                    }

                    ParameterInfo[] ps = m.GetParameters();
                    string sig = m.ReturnType.Name + " " + m.Name + "(";
                    for (int p = 0; p < ps.Length; p++)
                    {
                        if (p > 0)
                        {
                            sig += ", ";
                        }
                        sig += ps[p].ParameterType.FullName + " " + ps[p].Name;
                    }
                    sig += ")";
                    MelonLogger.Msg("[" + VersionTag + "] ImageConversion method: " + sig);
                    printed++;
                }
            }
            catch
            {
            }
        }

        private void ApplyUniverseTextureSettings(Texture2D tex)
        {
            if (tex == null)
            {
                return;
            }

            try
            {
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                tex.anisoLevel = 2;
            }
            catch
            {
            }
        }

        private bool TryLoadImageByReflection(Texture2D texture, byte[] bytes)
        {
            if (texture == null || bytes == null || bytes.Length <= 0)
            {
                MelonLogger.Warning("[" + VersionTag + "] TryLoadImageByReflection skipped. TextureNull=" + (texture == null) + " Bytes=" + (bytes == null ? -1 : bytes.Length));
                return false;
            }

            try
            {
                Type imageConversionType = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                MelonLogger.Msg("[" + VersionTag + "] ImageConversion type check: UnityEngine.ImageConversionModule -> " + (imageConversionType != null));

                if (imageConversionType == null)
                {
                    imageConversionType = Type.GetType("UnityEngine.ImageConversion, UnityEngine.CoreModule");
                    MelonLogger.Msg("[" + VersionTag + "] ImageConversion type check: UnityEngine.CoreModule -> " + (imageConversionType != null));
                }
                if (imageConversionType == null)
                {
                    imageConversionType = Type.GetType("UnityEngine.ImageConversion, UnityEngine");
                    MelonLogger.Msg("[" + VersionTag + "] ImageConversion type check: UnityEngine -> " + (imageConversionType != null));
                }

                if (imageConversionType == null)
                {
                    MelonLogger.Warning("[" + VersionTag + "] ImageConversion type not found in expected assemblies.");
                    return false;
                }

                MelonLogger.Msg("[" + VersionTag + "] ImageConversion type resolved: " + imageConversionType.FullName + " / Assembly=" + imageConversionType.Assembly.FullName);

                // IL2CPP版UnityEngine.ImageConversion.LoadImageは byte[] ではなく
                // Il2CppStructArray<byte> を要求する。
                // v20ログ:
                // Boolean LoadImage(UnityEngine.Texture2D tex, Il2CppStructArray<byte> data, Boolean markNonReadable)
                Il2CppStructArray<byte> il2cppBytes = new Il2CppStructArray<byte>(bytes.Length);
                for (int i = 0; i < bytes.Length; i++)
                {
                    il2cppBytes[i] = bytes[i];
                }

                MethodInfo method = FindLoadImageMethodForIl2CppBytes(imageConversionType, il2cppBytes.GetType());
                if (method == null)
                {
                    MelonLogger.Warning("[" + VersionTag + "] ImageConversion type exists, but IL2CPP LoadImage overload was not found.");
                    LogImageConversionMethods(imageConversionType);
                    return false;
                }

                ParameterInfo[] parameters = method.GetParameters();
                MelonLogger.Msg(
                    "[" + VersionTag + "] LoadImage IL2CPP method resolved. ParamCount=" + parameters.Length +
                    " DataParam=" + parameters[1].ParameterType.FullName +
                    " ReturnType=" + method.ReturnType.FullName
                );

                object result;
                if (parameters.Length == 2)
                {
                    result = method.Invoke(null, new object[] { texture, il2cppBytes });
                }
                else
                {
                    result = method.Invoke(null, new object[] { texture, il2cppBytes, false });
                }

                bool loaded = true;
                if (result is bool)
                {
                    loaded = (bool)result;
                }

                MelonLogger.Msg(
                    "[" + VersionTag + "] LoadImage invoke result=" + (result == null ? "null" : result.ToString()) +
                    " Loaded=" + loaded +
                    " TextureSize=" + texture.width + "x" + texture.height +
                    " Format=" + texture.format
                );

                return loaded && texture.width > 2 && texture.height > 2;
            }
            catch (TargetInvocationException ex)
            {
                string inner = ex.InnerException != null ? (ex.InnerException.GetType().FullName + ": " + ex.InnerException.Message) : "null";
                MelonLogger.Warning("[" + VersionTag + "] Reflection LoadImage TargetInvocationException. Inner=" + inner + " Outer=" + ex.Message);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[" + VersionTag + "] Reflection LoadImage failed: " + ex.GetType().FullName + " / " + ex.Message);
            }

            return false;
        }

        private MethodInfo FindLoadImageMethodForIl2CppBytes(Type imageConversionType, Type dataType)
        {
            if (imageConversionType == null || dataType == null)
            {
                return null;
            }

            try
            {
                MethodInfo[] methods = imageConversionType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                MethodInfo twoArg = null;
                MethodInfo threeArg = null;

                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method == null || method.Name != "LoadImage")
                    {
                        continue;
                    }

                    ParameterInfo[] ps = method.GetParameters();
                    if (ps == null)
                    {
                        continue;
                    }

                    if (ps.Length == 2 &&
                        ps[0].ParameterType == typeof(Texture2D) &&
                        ps[1].ParameterType.IsAssignableFrom(dataType))
                    {
                        twoArg = method;
                    }

                    if (ps.Length == 3 &&
                        ps[0].ParameterType == typeof(Texture2D) &&
                        ps[1].ParameterType.IsAssignableFrom(dataType) &&
                        ps[2].ParameterType == typeof(bool))
                    {
                        threeArg = method;
                    }
                }

                // markNonReadable=falseを明示できる3引数版を優先。
                if (threeArg != null)
                {
                    return threeArg;
                }

                return twoArg;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[" + VersionTag + "] FindLoadImageMethodForIl2CppBytes failed: " + ex.GetType().FullName + " / " + ex.Message);
                return null;
            }
        }

        private Texture2D CreateFallbackUniverseTexture()
        {
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.name = "Gojo_Domain_BlackHoleBackground_FallbackTexture_v21";

            try
            {
                Color c0 = new Color(0.0f, 0.0f, 0.012f, 1.0f);
                Color c1 = new Color(0.010f, 0.012f, 0.035f, 1.0f);
                tex.SetPixels(new Color[] { c0, c1, c1, c0 });
                tex.Apply(false, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
            }
            catch
            {
            }

            return tex;
        }

        private Material CreateBackgroundTextureMaterial(string name, Texture2D texture)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Texture");
            }
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material mat = new Material(shader);
            mat.name = name;

            SetMaterialColor(mat, new Color(1.0f, 1.0f, 1.0f, 0.0f));
            SetMaterialTexture(mat, texture);

            try
            {
                // 背景QuadはNPC/手の奥に置く前提なので、基本は通常描画。
                // Cull Offにして、Quadの向きが逆でもVRで見えるようにする。
                mat.SetInt("_Cull", 0);
                mat.SetInt("_ZWrite", 1);
                mat.renderQueue = 2000;
            }
            catch
            {
            }

            try
            {
                mat.SetFloat("_Surface", 0f);
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            catch
            {
            }

            return mat;
        }

        private void SetMaterialTexture(Material mat, Texture2D texture)
        {
            if (mat == null || texture == null)
            {
                return;
            }

            try
            {
                mat.mainTexture = texture;
            }
            catch
            {
            }

            try
            {
                mat.SetTexture("_BaseMap", texture);
            }
            catch
            {
            }

            try
            {
                mat.SetTexture("_MainTex", texture);
            }
            catch
            {
            }
        }

        private void SetupLine(LineRenderer lr, Material material, float width, int positionCount, bool loop)
        {
            lr.useWorldSpace = false;
            lr.positionCount = positionCount;
            lr.loop = loop;
            lr.widthMultiplier = width;
            lr.material = material;
            lr.numCornerVertices = 3;
            lr.numCapVertices = 3;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
        }

        private void SetRingPositions(LineRenderer lr, float radius, float y, int segments)
        {
            if (lr == null)
            {
                return;
            }

            for (int i = 0; i <= segments; i++)
            {
                float a = (Mathf.PI * 2.0f * i) / segments;
                float x = Mathf.Cos(a) * radius;
                float z = Mathf.Sin(a) * radius;
                lr.SetPosition(i, new Vector3(x, y, z));
            }
        }

        private void SetLocalRingPositions(LineRenderer lr, float radius, int segments)
        {
            if (lr == null)
            {
                return;
            }

            for (int i = 0; i <= segments; i++)
            {
                float a = (Mathf.PI * 2.0f * i) / segments;
                float x = Mathf.Cos(a) * radius;
                float z = Mathf.Sin(a) * radius;
                lr.SetPosition(i, new Vector3(x, 0f, z));
            }
        }

        private void RemoveCollider(GameObject obj)
        {
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

        private Material CreateTransparentMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            Material mat = new Material(shader);
            mat.name = name;
            SetMaterialColor(mat, color);

            try
            {
                // URP Unlit透明設定
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.SetInt("_Cull", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = 3000;
            }
            catch
            {
            }

            try
            {
                // Standard透明設定
                mat.SetFloat("_Mode", 3f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.SetInt("_Cull", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }
            catch
            {
            }

            return mat;
        }

        private Material CreateLineMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
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
                mat.SetInt("_Cull", 0);
                mat.renderQueue = 3100;
            }
            catch
            {
            }

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
            catch
            {
            }

            try
            {
                mat.SetColor("_BaseColor", color);
            }
            catch
            {
            }

            try
            {
                mat.SetColor("_Color", color);
            }
            catch
            {
            }
        }

        private float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3.0f - 2.0f * x);
        }

        private string FormatVector(Vector3 v)
        {
            return "(" + v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ", " + v.z.ToString("0.00") + ")";
        }

        private static string SafeName(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return "null";
            }

            try
            {
                return obj.name ?? "unnamed";
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
