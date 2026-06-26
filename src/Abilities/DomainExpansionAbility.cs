using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace IAmCatGojoMod
{
    /// <summary>
    /// 領域展開 / 無量空処
    ///
    /// DomainExpansionAbility_v6_WhiteVoidRendererMask:
    /// - Phase 1改良版: 軽量化 + NPC灰色石化フィニッシュ
    /// - v6修正:
    ///   - v3の停止/灰色石化/軽量化は維持
    ///   - 領域中心はv5のHMD/Camera自分中心を維持
    ///   - Camera White Void + Renderer Mask方式を追加
    ///   - 自分の手/NPC/Gojoエフェクト以外のRendererを発動中だけ非表示
    ///   - 物理/Colliderは残すので、見た目だけ白空間へ切り替える
    /// - 重さ対策:
    ///   - Physics探索は発動時のみ
    ///   - Rigidbody停止対象は最大64個
    ///   - 展開中は保存済みリストだけを停止維持
    ///   - ログ頻度も控えめ
    /// - 演出/挙動:
    ///   - 発動中はNPC/物体を停止
    ///   - 終了時、NPCは元に戻さず灰色単色化
    ///   - 石化NPCはAnimator/CharacterControllerをOFFのまま、位置固定を維持
    ///   - 左トリガーなどCancel時は、展開中なら復元して中断
    ///   - 展開完了後に石化したNPCは基本そのまま残す
    /// </summary>
    public sealed class DomainExpansionAbility
    {
        private const string VersionTag = "DomainExpansionAbility_v6_WhiteVoidRendererMask";

        private readonly PlayerRigFinder _playerRigFinder;
        private readonly VrInput _input;
        private readonly DomainExpansionEffect _effect = new DomainExpansionEffect();

        private bool _initialized;
        private bool _active;

        private Vector3 _center;
        private float _startTime;
        private float _endTime;
        private float _nextStatusLogTime;

        private const float DomainRadius = 8.5f;
        private const float Duration = 4.5f;
        private const float RigidbodyMaxMass = 900.0f;
        private const int MaxRigidbodies = 64;
        private const int MaxEffectTargets = 24;
        private readonly Dictionary<int, RigidbodyFreezeState> _frozenRigidbodies = new Dictionary<int, RigidbodyFreezeState>();
        private readonly Dictionary<int, NpcFreezeState> _frozenNpcs = new Dictionary<int, NpcFreezeState>();
        private readonly Dictionary<int, NpcFreezeState> _stonedNpcs = new Dictionary<int, NpcFreezeState>();

        private readonly HashSet<int> _seenRigidbodies = new HashSet<int>();
        private readonly HashSet<int> _seenNpcRoots = new HashSet<int>();
        private readonly List<RigidbodyCandidate> _rigidbodyCandidates = new List<RigidbodyCandidate>();
        private readonly List<Vector3> _effectTargetPositions = new List<Vector3>(MaxEffectTargets);

        private Material _stoneMaterial;

        // v6 White Void Renderer Mask:
        // 発動中だけ世界のRendererを非表示にし、手/NPC/Gojoエフェクトだけ残す。
        // 毎フレーム全探索はせず、Cast時に一度キャッシュして、Update中は保存済みだけ維持する。
        private readonly Dictionary<int, RendererMaskState> _hiddenWorldRenderers = new Dictionary<int, RendererMaskState>();
        private readonly HashSet<int> _protectedRendererIds = new HashSet<int>();
        private const int MaxHiddenWorldRenderers = 6000;
        private float _nextRendererMaskMaintainTime;

        public bool IsActive => _active;

        public DomainExpansionAbility(PlayerRigFinder playerRigFinder, VrInput input)
        {
            _playerRigFinder = playerRigFinder;
            _input = input;
        }

        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            // Effect側はInitializeでは何も生成しない。Show時に遅延生成する。
            _effect.Initialize();

            MelonLogger.Msg("[" + VersionTag + "] Initialized.");
        }

        public void Cast()
        {
            if (!_initialized)
            {
                Initialize();
            }

            // 展開中にもう一度押したら、中断して復元。
            // 石化フィニッシュ後は復元しない。
            if (_active)
            {
                Cancel();
                return;
            }

            if (!TryGetDomainCenter(out _center, out string poseSource))
            {
                MelonLogger.Warning("[" + VersionTag + "] Cast failed. No player center.");
                return;
            }

            _startTime = Time.time;
            _endTime = Time.time + Duration;
            _nextStatusLogTime = 0f;
            _active = true;

            ClearCastCaches();
            RestoreActiveFrozenTargets();
            RestoreWhiteVoidRendererMask();

            DomainStats stats = CollectTargetsOnce(_center);
            MaintainFrozenTargets();
            MaintainStonedNpcs();

            // v6:
            // NPC収集後に保護Rendererリストを作り、手/NPC/Gojo以外の世界Rendererを隠す。
            // ここで隠すのはRenderer.enabledだけ。Collider/物理は触らない。
            RendererMaskStats rendererStats = ApplyWhiteVoidRendererMask(_center);

            _effect.Show(_center, DomainRadius);
            _effect.UpdateEffect(_center, DomainRadius, 0.0f, _effectTargetPositions, stats.NpcCount, stats.RigidbodyCount);

            MelonLogger.Msg(
                "[" + VersionTag + "] Cast / 領域展開『無量空処』. " +
                "Pose=" + poseSource +
                ", Center=" + FormatVector(_center) +
                ", Radius=" + DomainRadius.ToString("0.0") +
                ", Duration=" + Duration.ToString("0.0") +
                ", NPC=" + stats.NpcCount +
                ", Rigidbody=" + stats.RigidbodyCount +
                ", Candidates=" + stats.RigidbodyCandidates +
                ", RenderersTotal=" + rendererStats.Total +
                ", HiddenRenderers=" + rendererStats.Hidden +
                ", ProtectedRenderers=" + rendererStats.Protected +
                ", DisabledRenderers=" + rendererStats.AlreadyDisabled +
                ", LimitSkipped=" + rendererStats.LimitSkipped
            );
        }

        public void Cancel()
        {
            if (!_active)
            {
                // 完了後の石化NPCはCancelでは戻さない。
                // 左トリガーで過去の石化まで解除されると動画的に弱いので、あえて何もしない。
                RestoreWhiteVoidRendererMask();
                _effect.Hide();
                return;
            }

            _active = false;

            RestoreActiveFrozenTargets();
            RestoreWhiteVoidRendererMask();
            _effect.Hide();
            ClearCastCaches();

            MelonLogger.Msg("[" + VersionTag + "] Cancel / 領域中断。展開中だった対象は復元しました。");
        }

        public void Update()
        {
            // 石化NPCは領域終了後も固定を維持する。
            MaintainStonedNpcs();

            if (!_active)
            {
                return;
            }

            if (Time.time >= _endTime)
            {
                FinishAsStatue();
                return;
            }

            float normalizedTime = Mathf.Clamp01((Time.time - _startTime) / Duration);

            MaintainFrozenTargets();
            MaintainWhiteVoidRendererMask();
            _effect.UpdateEffect(_center, DomainRadius, normalizedTime, _effectTargetPositions, _frozenNpcs.Count, _frozenRigidbodies.Count);

            if (Time.time >= _nextStatusLogTime)
            {
                _nextStatusLogTime = Time.time + 1.0f;
                MelonLogger.Msg(
                    "[" + VersionTag + "] Active. " +
                    "NPC=" + _frozenNpcs.Count +
                    ", Rigidbody=" + _frozenRigidbodies.Count +
                    ", StonedNPC=" + _stonedNpcs.Count +
                    ", Remaining=" + Mathf.Max(0f, _endTime - Time.time).ToString("0.00")
                );
            }
        }

        private void FinishAsStatue()
        {
            if (!_active)
            {
                return;
            }

            _active = false;

            // Rigidbodyは展開終了後に物理復帰。ただし速度はゼロにしておく。
            ReleaseRigidbodiesStopped();

            int stoneCount = 0;

            foreach (KeyValuePair<int, NpcFreezeState> pair in _frozenNpcs)
            {
                NpcFreezeState state = pair.Value;
                if (state == null || state.Root == null)
                {
                    continue;
                }

                MakeNpcStatue(state);
                _stonedNpcs[pair.Key] = state;
                stoneCount++;
            }

            _frozenNpcs.Clear();

            RestoreWhiteVoidRendererMask();
            _effect.Hide();
            ClearCastCaches();

            MelonLogger.Msg(
                "[" + VersionTag + "] Finished / 領域終了。NPCを灰色石化しました。StoneNPC=" + stoneCount
            );
        }

        private bool TryGetDomainCenter(out Vector3 center, out string source)
        {
            center = Vector3.zero;
            source = "Unknown";

            // v5:
            // 領域展開は「自分が領域の中にいる」必要があるので、
            // Infinity用のBarrierCenterではなく、HMD/Cameraを基準にする。
            // BarrierCenterはゲーム内の猫/バリア都合で前方にズレることがあり、
            // 白い球を外側から見ているように見える原因になる。
            try
            {
                if (_playerRigFinder != null && _playerRigFinder.TryGetCameraTransform(out Transform camFromFinder) && camFromFinder != null)
                {
                    center = camFromFinder.position + Vector3.down * 0.65f;
                    source = "CameraSelfCenter(PlayerRigFinder)";
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    center = cam.transform.position + Vector3.down * 0.65f;
                    source = "CameraSelfCenter(Camera.main)";
                    return true;
                }
            }
            catch
            {
            }

            // 最後の保険としてだけBarrierCenterを使う。
            try
            {
                if (_playerRigFinder != null && _playerRigFinder.TryGetBarrierCenter(out center))
                {
                    source = "FallbackBarrierCenter";
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private DomainStats CollectTargetsOnce(Vector3 center)
        {
            DomainStats stats = new DomainStats();

            Collider[] hits = null;
            try
            {
                // v2のNonAlloc固定バッファは、ヒット数が多い場所で先頭の除外対象だけを拾ってしまい、
                // NPC/Rigidbodyが0件になることがあった。
                // 発動時1回だけなので、ここは全件取得のOverlapSphereを使う。
                hits = Physics.OverlapSphere(center, DomainRadius);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[" + VersionTag + "] OverlapSphere failed: " + ex.GetType().Name + ": " + ex.Message);
                return stats;
            }

            int hitCount = hits == null ? 0 : hits.Length;
            if (hitCount <= 0)
            {
                MelonLogger.Msg("[" + VersionTag + "] Scan result: HitCount=0");
                return stats;
            }

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = hits[i];
                if (hit == null || hit.transform == null)
                {
                    continue;
                }

                // 重要:
                // ここで先に IsExcludedTransform(hit.transform) しない。
                // NPCの子ボーン/手などが "Hand" を含むと、NPC Rootを見つける前に除外されるため。
                // NPCはRootを探してからRootに対して除外判定する。
                TryCollectNpc(hit.transform, ref stats);

                // Rigidbodyは従来通り、RigidbodyのTransformを見て除外する。
                TryCollectRigidbody(hit, center, ref stats);
            }

            _rigidbodyCandidates.Sort((a, b) => a.DistanceSqr.CompareTo(b.DistanceSqr));
            stats.RigidbodyCandidates = _rigidbodyCandidates.Count;

            int rbLimit = Mathf.Min(MaxRigidbodies, _rigidbodyCandidates.Count);
            for (int i = 0; i < rbLimit; i++)
            {
                Rigidbody rb = _rigidbodyCandidates[i].Rigidbody;
                if (rb == null)
                {
                    continue;
                }

                if (FreezeRigidbody(rb))
                {
                    stats.RigidbodyCount++;
                }
            }

            MelonLogger.Msg(
                "[" + VersionTag + "] Scan result. " +
                "HitCount=" + hitCount +
                ", Npc=" + stats.NpcCount +
                ", RigidbodyCandidates=" + stats.RigidbodyCandidates +
                ", RigidbodySelected=" + stats.RigidbodyCount
            );

            return stats;
        }

        private void TryCollectNpc(Transform start, ref DomainStats stats)
        {
            Transform npcRoot = FindNpcRoot(start);
            if (npcRoot == null || IsExcludedTransform(npcRoot))
            {
                return;
            }

            int id = SafeInstanceId(npcRoot.gameObject);
            if (id == 0 || _seenNpcRoots.Contains(id))
            {
                return;
            }

            _seenNpcRoots.Add(id);

            // すでに石化しているNPCは再収集しない。
            if (_stonedNpcs.ContainsKey(id))
            {
                return;
            }

            if (FreezeNpc(npcRoot))
            {
                stats.NpcCount++;
            }
        }

        private void TryCollectRigidbody(Collider hit, Vector3 center, ref DomainStats stats)
        {
            Rigidbody rb = null;
            try
            {
                rb = hit.attachedRigidbody;
            }
            catch
            {
                rb = null;
            }

            if (rb == null || rb.transform == null)
            {
                return;
            }

            if (IsExcludedTransform(rb.transform))
            {
                return;
            }

            try
            {
                if (rb.mass > RigidbodyMaxMass)
                {
                    return;
                }
            }
            catch
            {
            }

            int id = SafeInstanceId(rb.gameObject);
            if (id == 0 || _seenRigidbodies.Contains(id))
            {
                return;
            }

            _seenRigidbodies.Add(id);

            float distSqr = 999999f;
            try
            {
                distSqr = (rb.worldCenterOfMass - center).sqrMagnitude;
            }
            catch
            {
                distSqr = (rb.transform.position - center).sqrMagnitude;
            }

            _rigidbodyCandidates.Add(new RigidbodyCandidate
            {
                Rigidbody = rb,
                DistanceSqr = distSqr
            });
        }

        private bool FreezeNpc(Transform npcRoot)
        {
            if (npcRoot == null || IsExcludedTransform(npcRoot))
            {
                return false;
            }

            int id = SafeInstanceId(npcRoot.gameObject);
            if (id == 0)
            {
                return false;
            }

            if (_frozenNpcs.ContainsKey(id))
            {
                return true;
            }

            NpcFreezeState state = new NpcFreezeState();
            state.Root = npcRoot;
            state.RootPosition = npcRoot.position;
            state.RootRotation = npcRoot.rotation;

            try
            {
                Animator[] animators = npcRoot.GetComponentsInChildren<Animator>(true);
                if (animators != null)
                {
                    for (int i = 0; i < animators.Length; i++)
                    {
                        Animator animator = animators[i];
                        if (animator == null)
                        {
                            continue;
                        }

                        state.Animators.Add(new AnimatorState
                        {
                            Animator = animator,
                            Enabled = animator.enabled
                        });

                        animator.enabled = false;
                    }
                }
            }
            catch
            {
            }

            try
            {
                CharacterController[] controllers = npcRoot.GetComponentsInChildren<CharacterController>(true);
                if (controllers != null)
                {
                    for (int i = 0; i < controllers.Length; i++)
                    {
                        CharacterController controller = controllers[i];
                        if (controller == null)
                        {
                            continue;
                        }

                        state.Controllers.Add(new ControllerState
                        {
                            Controller = controller,
                            Enabled = controller.enabled
                        });

                        controller.enabled = false;
                    }
                }
            }
            catch
            {
            }

            try
            {
                Renderer[] renderers = npcRoot.GetComponentsInChildren<Renderer>(true);
                if (renderers != null)
                {
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        Renderer renderer = renderers[i];
                        if (renderer == null)
                        {
                            continue;
                        }

                        Material[] originalMaterials = null;
                        try
                        {
                            originalMaterials = renderer.materials;
                        }
                        catch
                        {
                            originalMaterials = null;
                        }

                        state.Renderers.Add(new RendererState
                        {
                            Renderer = renderer,
                            OriginalMaterials = originalMaterials
                        });
                    }
                }
            }
            catch
            {
            }

            _frozenNpcs[id] = state;
            AddEffectTarget(state.RootPosition + Vector3.up * 0.7f);

            MelonLogger.Msg(
                "[" + VersionTag + "] NPC freeze: " + SafeName(npcRoot) +
                ", Animators=" + state.Animators.Count +
                ", Controllers=" + state.Controllers.Count +
                ", Renderers=" + state.Renderers.Count
            );

            return true;
        }

        private bool FreezeRigidbody(Rigidbody rb)
        {
            if (rb == null || rb.transform == null)
            {
                return false;
            }

            if (IsExcludedTransform(rb.transform))
            {
                return false;
            }

            int id = SafeInstanceId(rb.gameObject);
            if (id == 0 || _frozenRigidbodies.ContainsKey(id))
            {
                return false;
            }

            RigidbodyFreezeState state = new RigidbodyFreezeState
            {
                Rigidbody = rb,
                IsKinematic = SafeIsKinematic(rb),
                UseGravity = SafeUseGravity(rb)
            };

            _frozenRigidbodies[id] = state;

            try
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                AddEffectTarget(rb.worldCenterOfMass);
            }
            catch
            {
                try
                {
                    AddEffectTarget(rb.transform.position);
                }
                catch
                {
                }
            }

            return true;
        }

        private void MaintainFrozenTargets()
        {
            foreach (KeyValuePair<int, RigidbodyFreezeState> pair in _frozenRigidbodies)
            {
                RigidbodyFreezeState state = pair.Value;
                Rigidbody rb = state.Rigidbody;
                if (rb == null)
                {
                    continue;
                }

                try
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                catch
                {
                }
            }

            foreach (KeyValuePair<int, NpcFreezeState> pair in _frozenNpcs)
            {
                MaintainNpcFrozen(pair.Value);
            }
        }

        private void MaintainStonedNpcs()
        {
            if (_stonedNpcs.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<int, NpcFreezeState> pair in _stonedNpcs)
            {
                MaintainNpcFrozen(pair.Value);
                EnsureNpcIsStone(pair.Value);
            }
        }

        private void MaintainNpcFrozen(NpcFreezeState state)
        {
            if (state == null || state.Root == null)
            {
                return;
            }

            try
            {
                state.Root.position = state.RootPosition;
                state.Root.rotation = state.RootRotation;
            }
            catch
            {
            }

            for (int i = 0; i < state.Animators.Count; i++)
            {
                try
                {
                    if (state.Animators[i].Animator != null)
                    {
                        state.Animators[i].Animator.enabled = false;
                    }
                }
                catch
                {
                }
            }

            for (int i = 0; i < state.Controllers.Count; i++)
            {
                try
                {
                    if (state.Controllers[i].Controller != null)
                    {
                        state.Controllers[i].Controller.enabled = false;
                    }
                }
                catch
                {
                }
            }
        }

        private void MakeNpcStatue(NpcFreezeState state)
        {
            if (state == null || state.Root == null)
            {
                return;
            }

            MaintainNpcFrozen(state);
            ApplyStoneMaterial(state);
        }

        private void EnsureNpcIsStone(NpcFreezeState state)
        {
            if (state == null || state.Root == null)
            {
                return;
            }

            // 何かのタイミングでMaterialを戻される可能性があるので、軽く維持。
            // NPC数は少ない想定なので負荷は小さい。
            ApplyStoneMaterial(state);
        }

        private void ApplyStoneMaterial(NpcFreezeState state)
        {
            if (state == null)
            {
                return;
            }

            Material stone = GetStoneMaterial();
            if (stone == null)
            {
                return;
            }

            for (int i = 0; i < state.Renderers.Count; i++)
            {
                RendererState rendererState = state.Renderers[i];
                Renderer renderer = rendererState.Renderer;
                if (renderer == null)
                {
                    continue;
                }

                try
                {
                    int materialCount = 1;

                    if (rendererState.OriginalMaterials != null && rendererState.OriginalMaterials.Length > 0)
                    {
                        materialCount = rendererState.OriginalMaterials.Length;
                    }
                    else
                    {
                        try
                        {
                            Material[] current = renderer.materials;
                            if (current != null && current.Length > 0)
                            {
                                materialCount = current.Length;
                            }
                        }
                        catch
                        {
                        }
                    }

                    Material[] mats = new Material[materialCount];
                    for (int m = 0; m < mats.Length; m++)
                    {
                        mats[m] = stone;
                    }

                    renderer.materials = mats;
                }
                catch
                {
                    try
                    {
                        renderer.material = stone;
                    }
                    catch
                    {
                    }
                }
            }
        }

        private Material GetStoneMaterial()
        {
            if (_stoneMaterial != null)
            {
                return _stoneMaterial;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                return null;
            }

            _stoneMaterial = new Material(shader);
            _stoneMaterial.name = "Gojo_UnlimitedVoid_StoneGray_Mat";
            _stoneMaterial.color = new Color(0.46f, 0.46f, 0.46f, 1.0f);

            try
            {
                _stoneMaterial.SetColor("_BaseColor", new Color(0.46f, 0.46f, 0.46f, 1.0f));
            }
            catch
            {
            }

            try
            {
                _stoneMaterial.SetColor("_Color", new Color(0.46f, 0.46f, 0.46f, 1.0f));
            }
            catch
            {
            }

            return _stoneMaterial;
        }

        private void RestoreActiveFrozenTargets()
        {
            ReleaseRigidbodiesStopped();

            foreach (KeyValuePair<int, NpcFreezeState> pair in _frozenNpcs)
            {
                RestoreNpc(pair.Value);
            }

            _frozenNpcs.Clear();
        }

        private void ReleaseRigidbodiesStopped()
        {
            foreach (KeyValuePair<int, RigidbodyFreezeState> pair in _frozenRigidbodies)
            {
                RigidbodyFreezeState state = pair.Value;
                Rigidbody rb = state.Rigidbody;
                if (rb == null)
                {
                    continue;
                }

                try
                {
                    rb.isKinematic = state.IsKinematic;
                    rb.useGravity = state.UseGravity;

                    // 領域解除時に急に暴れるのを防ぐため速度はゼロのまま。
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                catch
                {
                }
            }

            _frozenRigidbodies.Clear();
        }

        private void RestoreNpc(NpcFreezeState state)
        {
            if (state == null)
            {
                return;
            }

            for (int i = 0; i < state.Animators.Count; i++)
            {
                AnimatorState animatorState = state.Animators[i];
                try
                {
                    if (animatorState.Animator != null)
                    {
                        animatorState.Animator.enabled = animatorState.Enabled;
                    }
                }
                catch
                {
                }
            }

            for (int i = 0; i < state.Controllers.Count; i++)
            {
                ControllerState controllerState = state.Controllers[i];
                try
                {
                    if (controllerState.Controller != null)
                    {
                        controllerState.Controller.enabled = controllerState.Enabled;
                    }
                }
                catch
                {
                }
            }

            for (int i = 0; i < state.Renderers.Count; i++)
            {
                RendererState rendererState = state.Renderers[i];
                try
                {
                    if (rendererState.Renderer != null && rendererState.OriginalMaterials != null)
                    {
                        rendererState.Renderer.materials = rendererState.OriginalMaterials;
                    }
                }
                catch
                {
                }
            }

            try
            {
                if (state.Root != null)
                {
                    state.Root.position = state.RootPosition;
                    state.Root.rotation = state.RootRotation;
                }
            }
            catch
            {
            }
        }

        private RendererMaskStats ApplyWhiteVoidRendererMask(Vector3 center)
        {
            RendererMaskStats stats = new RendererMaskStats();

            RestoreWhiteVoidRendererMask();
            BuildProtectedRendererIdSet();

            Renderer[] renderers = null;
            try
            {
                // 発動時1回だけ。
                // activeなRendererだけ取れれば、今見えている家具/壁/床/小物はだいたい対象に入る。
                renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[" + VersionTag + "] Renderer mask scan failed: " + ex.GetType().Name + ": " + ex.Message);
                return stats;
            }

            if (renderers == null || renderers.Length == 0)
            {
                MelonLogger.Msg("[" + VersionTag + "] Renderer mask scan result: Total=0");
                return stats;
            }

            stats.Total = renderers.Length;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                int id = SafeInstanceId(renderer);
                if (id == 0)
                {
                    continue;
                }

                bool enabled = false;
                try
                {
                    enabled = renderer.enabled;
                }
                catch
                {
                    enabled = false;
                }

                if (!enabled)
                {
                    stats.AlreadyDisabled++;
                    continue;
                }

                if (IsProtectedRenderer(renderer))
                {
                    stats.Protected++;
                    continue;
                }

                if (_hiddenWorldRenderers.Count >= MaxHiddenWorldRenderers)
                {
                    stats.LimitSkipped++;
                    continue;
                }

                try
                {
                    _hiddenWorldRenderers[id] = new RendererMaskState
                    {
                        Renderer = renderer,
                        Enabled = enabled
                    };

                    renderer.enabled = false;
                    stats.Hidden++;
                }
                catch
                {
                }
            }

            _nextRendererMaskMaintainTime = Time.time + 0.25f;

            MelonLogger.Msg(
                "[" + VersionTag + "] WhiteVoidRendererMask applied. " +
                "Total=" + stats.Total +
                ", Hidden=" + stats.Hidden +
                ", Protected=" + stats.Protected +
                ", AlreadyDisabled=" + stats.AlreadyDisabled +
                ", LimitSkipped=" + stats.LimitSkipped
            );

            return stats;
        }

        private void MaintainWhiteVoidRendererMask()
        {
            if (_hiddenWorldRenderers.Count == 0)
            {
                return;
            }

            if (Time.time < _nextRendererMaskMaintainTime)
            {
                return;
            }

            _nextRendererMaskMaintainTime = Time.time + 0.35f;

            int maintained = 0;
            foreach (KeyValuePair<int, RendererMaskState> pair in _hiddenWorldRenderers)
            {
                Renderer renderer = pair.Value.Renderer;
                if (renderer == null)
                {
                    continue;
                }

                try
                {
                    if (renderer.enabled)
                    {
                        renderer.enabled = false;
                    }

                    maintained++;
                }
                catch
                {
                }
            }
        }

        private void RestoreWhiteVoidRendererMask()
        {
            if (_hiddenWorldRenderers.Count == 0)
            {
                _protectedRendererIds.Clear();
                return;
            }

            int restored = 0;
            foreach (KeyValuePair<int, RendererMaskState> pair in _hiddenWorldRenderers)
            {
                RendererMaskState state = pair.Value;
                Renderer renderer = state.Renderer;
                if (renderer == null)
                {
                    continue;
                }

                try
                {
                    renderer.enabled = state.Enabled;
                    restored++;
                }
                catch
                {
                }
            }

            _hiddenWorldRenderers.Clear();
            _protectedRendererIds.Clear();

            MelonLogger.Msg("[" + VersionTag + "] WhiteVoidRendererMask restored. Restored=" + restored);
        }

        private void BuildProtectedRendererIdSet()
        {
            _protectedRendererIds.Clear();

            AddProtectedRenderersFromNpcStates(_frozenNpcs);
            AddProtectedRenderersFromNpcStates(_stonedNpcs);
        }

        private void AddProtectedRenderersFromNpcStates(Dictionary<int, NpcFreezeState> states)
        {
            if (states == null || states.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<int, NpcFreezeState> pair in states)
            {
                NpcFreezeState npc = pair.Value;
                if (npc == null)
                {
                    continue;
                }

                for (int i = 0; i < npc.Renderers.Count; i++)
                {
                    Renderer renderer = npc.Renderers[i].Renderer;
                    if (renderer == null)
                    {
                        continue;
                    }

                    int id = SafeInstanceId(renderer);
                    if (id != 0)
                    {
                        _protectedRendererIds.Add(id);
                    }
                }
            }
        }

        private bool IsProtectedRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                return true;
            }

            int id = SafeInstanceId(renderer);
            if (id != 0 && _protectedRendererIds.Contains(id))
            {
                return true;
            }

            Transform t = null;
            try
            {
                t = renderer.transform;
            }
            catch
            {
                t = null;
            }

            if (t == null)
            {
                return true;
            }

            string path = GetPath(t).ToLowerInvariant();

            // 自分の手/カメラ/VRリグ/UI/Gojoエフェクトは残す。
            // ここを広めに保護することで、「自分の手が消える」事故を避ける。
            if (
                path.Contains("player") ||
                path.Contains("hmd") ||
                path.Contains("camera") ||
                path.Contains("hand") ||
                path.Contains("controller") ||
                path.Contains("xr") ||
                path.Contains("tracking") ||
                path.Contains("rig") ||
                path.Contains("ui") ||
                path.Contains("canvas") ||
                path.Contains("gojo") ||
                path.Contains("domain") ||
                path.Contains("ability")
            )
            {
                return true;
            }

            // NPCは見えるままにする。
            // CollectTargetsOnceで取れたNPCはRendererIdで保護済みだが、
            // Root検出漏れの保険として名前ベースでも保護する。
            if (LooksLikeNpcName(path))
            {
                return true;
            }

            return false;
        }

        private void ClearCastCaches()
        {
            _seenRigidbodies.Clear();
            _seenNpcRoots.Clear();
            _rigidbodyCandidates.Clear();
            _effectTargetPositions.Clear();
        }

        private void AddEffectTarget(Vector3 position)
        {
            if (_effectTargetPositions.Count >= MaxEffectTargets)
            {
                return;
            }

            _effectTargetPositions.Add(position);
        }

        private Transform FindNpcRoot(Transform start)
        {
            if (start == null)
            {
                return null;
            }

            Transform current = start;
            Transform best = null;
            int guard = 0;

            while (current != null && guard < 14)
            {
                string name = SafeName(current);
                if (LooksLikeNpcName(name) || HasNpcComponents(current))
                {
                    best = current;
                }

                current = current.parent;
                guard++;
            }

            return best;
        }

        private bool HasNpcComponents(Transform t)
        {
            if (t == null)
            {
                return false;
            }

            try
            {
                Animator animator = t.GetComponent<Animator>();
                if (animator != null)
                {
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                CharacterController controller = t.GetComponent<CharacterController>();
                if (controller != null)
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool LooksLikeNpcName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            string lower = name.ToLowerInvariant();

            return
                lower.Contains("charactercontainer") ||
                lower.Contains("babka") ||
                lower.Contains("granny") ||
                lower.Contains("grandma") ||
                lower.Contains("dog") ||
                lower.Contains("pigeon") ||
                lower.Contains("mechanic") ||
                lower.Contains("smoker") ||
                lower.Contains("streetgirl") ||
                lower.Contains("npc");
        }

        private bool IsExcludedTransform(Transform t)
        {
            if (t == null)
            {
                return true;
            }

            string path = GetPath(t).ToLowerInvariant();

            return
                path.Contains("player") ||
                path.Contains("hmd") ||
                path.Contains("camera") ||
                path.Contains("hand") ||
                path.Contains("controller") ||
                path.Contains("xr") ||
                path.Contains("ui") ||
                path.Contains("canvas") ||
                path.Contains("scenecontext") ||
                path.Contains("projectcontext") ||
                path.Contains("gojo");
        }

        private static bool SafeIsKinematic(Rigidbody rb)
        {
            try
            {
                return rb != null && rb.isKinematic;
            }
            catch
            {
                return false;
            }
        }

        private static bool SafeUseGravity(Rigidbody rb)
        {
            try
            {
                return rb != null && rb.useGravity;
            }
            catch
            {
                return true;
            }
        }

        private static int SafeInstanceId(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return 0;
            }

            try
            {
                return obj.GetInstanceID();
            }
            catch
            {
                return 0;
            }
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

        private string GetPath(Transform t)
        {
            if (t == null)
            {
                return "null";
            }

            try
            {
                string path = t.name;
                Transform current = t.parent;
                int guard = 0;

                while (current != null && guard < 18)
                {
                    path = current.name + "/" + path;
                    current = current.parent;
                    guard++;
                }

                return path;
            }
            catch
            {
                return SafeName(t);
            }
        }

        private string FormatVector(Vector3 v)
        {
            return "(" + v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ", " + v.z.ToString("0.00") + ")";
        }

        private sealed class DomainStats
        {
            public int NpcCount;
            public int RigidbodyCount;
            public int RigidbodyCandidates;
        }

        private struct RigidbodyCandidate
        {
            public Rigidbody Rigidbody;
            public float DistanceSqr;
        }

        private sealed class NpcFreezeState
        {
            public Transform Root;
            public Vector3 RootPosition;
            public Quaternion RootRotation;
            public readonly List<AnimatorState> Animators = new List<AnimatorState>();
            public readonly List<ControllerState> Controllers = new List<ControllerState>();
            public readonly List<RendererState> Renderers = new List<RendererState>();
        }

        private struct AnimatorState
        {
            public Animator Animator;
            public bool Enabled;
        }

        private struct ControllerState
        {
            public CharacterController Controller;
            public bool Enabled;
        }

        private struct RendererState
        {
            public Renderer Renderer;
            public Material[] OriginalMaterials;
        }

        private struct RendererMaskState
        {
            public Renderer Renderer;
            public bool Enabled;
        }

        private struct RendererMaskStats
        {
            public int Total;
            public int Hidden;
            public int Protected;
            public int AlreadyDisabled;
            public int LimitSkipped;
        }

        private struct RigidbodyFreezeState
        {
            public Rigidbody Rigidbody;
            public bool IsKinematic;
            public bool UseGravity;
        }
    }
}
