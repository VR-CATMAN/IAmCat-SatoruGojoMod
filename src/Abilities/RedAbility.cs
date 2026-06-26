using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.XR;

namespace IAmCatGojoMod
{
    /// <summary>
    /// 術式反転「赫」 / Red
    ///
    /// RedAbility_v1_ProjectileImpact:
    /// - 右手前から小さい赤い球を高速発射
    /// - transform移動 + SphereCast/Overlapで命中判定
    /// - 命中地点で赤い衝撃波を発生
    /// - Rigidbody/NPC/kinematic対象を外側へ弾き飛ばす
    /// - NPCはAnimator/CharacterController一時停止、子Rigidbodyへの衝撃、Root傾き補正を試す
    /// </summary>
    public sealed class RedAbility
    {
        private const string VersionTag = "RedAbility_v1_ProjectileImpact";

        private readonly PlayerRigFinder _playerRigFinder;
        private readonly VrInput _input;
        private readonly RedEffect _effect = new RedEffect();

        private bool _initialized;
        private bool _projectileActive;
        private bool _impactActive;

        private Vector3 _castOrigin;
        private Vector3 _castForward;
        private Vector3 _projectilePosition;
        private Vector3 _previousProjectilePosition;
        private Vector3 _impactPosition;

        private float _projectileStartTime;
        private float _projectileEndTime;
        private float _impactStartTime;
        private float _impactEndTime;
        private float _nextStatusLogTime;

        private const float SpawnForwardOffset = 0.38f;
        private const float ProjectileRadius = 0.16f;
        private const float ProjectileSpeed = 24.0f;
        private const float ProjectileLifeSeconds = 2.0f;
        private const float ImpactRadius = 4.2f;
        private const float ImpactDuration = 1.15f;
        private const float MaxTargetMass = 900.0f;
        private const float RigidbodyImpulse = 42.0f;
        private const float RigidbodyUpImpulse = 5.5f;
        private const float NpcImpulse = 10.0f;
        private const float NpcRootPush = 0.55f;
        private const float KinematicPush = 0.65f;
        private const int MaxEffectTargets = 48;

        // true: 衝撃波終了時にAnimator/CharacterControllerを戻す。falseにすると倒れっぱなし寄りだがゲーム状態は荒れやすい。
        private const bool RestoreNpcControlsOnFinish = true;

        private readonly List<Vector3> _effectTargetPositions = new List<Vector3>(MaxEffectTargets);
        private readonly HashSet<int> _processedRigidbodies = new HashSet<int>();
        private readonly HashSet<int> _processedTransforms = new HashSet<int>();
        private readonly Dictionary<int, NpcKnockState> _knockedNpcs = new Dictionary<int, NpcKnockState>();

        public bool IsActive => _projectileActive || _impactActive;

        public RedAbility(PlayerRigFinder playerRigFinder, VrInput input)
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
            _effect.Initialize();
            MelonLogger.Msg("[" + VersionTag + "] Initialized.");
        }

        public void Cast()
        {
            if (!_initialized)
            {
                Initialize();
            }

            if (!TryGetCastPose(out _castOrigin, out _castForward, out string poseSource, out string poseDebug))
            {
                MelonLogger.Warning("[" + VersionTag + "] Cast failed. No right hand or camera pose.");
                return;
            }

            CancelInternal(false);
            RestoreKnockedNpcControls();
            ClearFrameCaches();

            _castForward = SafeNormalize(_castForward, Vector3.forward);
            _projectilePosition = _castOrigin + _castForward * SpawnForwardOffset;
            _previousProjectilePosition = _projectilePosition;
            _projectileStartTime = Time.time;
            _projectileEndTime = Time.time + ProjectileLifeSeconds;
            _nextStatusLogTime = 0f;
            _projectileActive = true;
            _impactActive = false;

            _effect.ShowProjectile(_projectilePosition, _castForward, ProjectileRadius);

            MelonLogger.Msg("[" + VersionTag + "] Cast / 赫 発射. " +
                            "Pose=" + poseSource +
                            ", Origin=" + FormatVector(_castOrigin) +
                            ", Forward=" + FormatVector(_castForward) +
                            ", Projectile=" + FormatVector(_projectilePosition) +
                            ", Speed=" + ProjectileSpeed +
                            ", Life=" + ProjectileLifeSeconds +
                            ", " + poseDebug);
        }

        public void Cancel()
        {
            if (!_projectileActive && !_impactActive)
            {
                return;
            }

            CancelInternal(true);
            MelonLogger.Msg("[" + VersionTag + "] Cancel / 赫 解除");
        }

        public void Update()
        {
            if (_projectileActive)
            {
                UpdateProjectile();
            }

            if (_impactActive)
            {
                UpdateImpact();
            }
        }

        private void UpdateProjectile()
        {
            if (!_projectileActive)
            {
                return;
            }

            float now = Time.time;
            float dt = Mathf.Max(Time.deltaTime, 0.001f);
            float normalizedLife = Mathf.Clamp01((now - _projectileStartTime) / ProjectileLifeSeconds);

            _previousProjectilePosition = _projectilePosition;
            Vector3 next = _projectilePosition + _castForward * ProjectileSpeed * dt;

            if (TryFindProjectileHit(_previousProjectilePosition, next, out Vector3 hitPosition, out string hitName))
            {
                Detonate(hitPosition, "Hit=" + hitName);
                return;
            }

            _projectilePosition = next;
            _effect.UpdateProjectile(_projectilePosition, _castForward, normalizedLife);

            if (now >= _projectileEndTime)
            {
                Detonate(_projectilePosition, "LifeEnd");
                return;
            }

            if (Time.time >= _nextStatusLogTime)
            {
                _nextStatusLogTime = Time.time + 0.35f;
                MelonLogger.Msg("[" + VersionTag + "] ProjectileActive. Pos=" + FormatVector(_projectilePosition) +
                                ", Remaining=" + Mathf.Max(0f, _projectileEndTime - Time.time).ToString("0.00"));
            }
        }

        private void UpdateImpact()
        {
            if (!_impactActive)
            {
                return;
            }

            if (Time.time >= _impactEndTime)
            {
                FinishImpact();
                return;
            }

            float normalized = Mathf.Clamp01((Time.time - _impactStartTime) / ImpactDuration);
            _effect.UpdateImpact(_impactPosition, ImpactRadius, normalized, _effectTargetPositions);
        }

        private bool TryFindProjectileHit(Vector3 from, Vector3 to, out Vector3 hitPosition, out string hitName)
        {
            hitPosition = to;
            hitName = "none";

            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance <= 0.001f)
            {
                return false;
            }

            Vector3 dir = delta / distance;

            try
            {
                RaycastHit[] hits = Physics.SphereCastAll(from, ProjectileRadius, dir, distance + ProjectileRadius * 0.5f);
                if (hits != null && hits.Length > 0)
                {
                    float bestDistance = 999999f;
                    bool found = false;

                    for (int i = 0; i < hits.Length; i++)
                    {
                        RaycastHit hit = hits[i];
                        Collider col = hit.collider;
                        if (col == null)
                        {
                            continue;
                        }

                        if (ShouldIgnoreProjectileCollider(col))
                        {
                            continue;
                        }

                        float d = hit.distance;
                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            hitPosition = hit.point.sqrMagnitude > 0.0001f ? hit.point : from + dir * d;
                            hitName = SafeName(col);
                            found = true;
                        }
                    }

                    if (found)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogThrottled("[" + VersionTag + "] SphereCastAll failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            try
            {
                Collider[] overlaps = Physics.OverlapSphere(to, ProjectileRadius * 1.35f);
                if (overlaps != null)
                {
                    for (int i = 0; i < overlaps.Length; i++)
                    {
                        Collider col = overlaps[i];
                        if (col == null || ShouldIgnoreProjectileCollider(col))
                        {
                            continue;
                        }

                        hitPosition = SafeColliderCenter(col, to);
                        hitName = SafeName(col);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogThrottled("[" + VersionTag + "] Projectile OverlapSphere failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            return false;
        }

        private void Detonate(Vector3 position, string reason)
        {
            _projectileActive = false;
            _impactActive = true;
            _impactPosition = position;
            _impactStartTime = Time.time;
            _impactEndTime = Time.time + ImpactDuration;
            _nextStatusLogTime = 0f;

            _effect.HideProjectile();

            RedStats stats = ApplyImpact(position);
            _effect.ShowImpact(position, ImpactRadius, _effectTargetPositions);

            MelonLogger.Msg("[" + VersionTag + "] Impact / 赫 着弾. " + reason +
                            ", Pos=" + FormatVector(position) +
                            ", RbTargets=" + stats.RigidbodyTargetCount +
                            ", RbPushed=" + stats.RigidbodyPushedCount +
                            ", KinematicPushed=" + stats.KinematicPushedCount +
                            ", NpcPushed=" + stats.NpcPushedCount +
                            ", NpcKnocked=" + stats.NpcKnockedCount +
                            ", ChildRbImpulse=" + stats.NpcChildRigidbodyImpulseCount +
                            ", EffectTargets=" + _effectTargetPositions.Count);
        }

        private RedStats ApplyImpact(Vector3 center)
        {
            RedStats stats = new RedStats();
            ClearFrameCaches();

            Collider[] hits;
            try
            {
                hits = Physics.OverlapSphere(center, ImpactRadius);
            }
            catch (Exception ex)
            {
                LogThrottled("[" + VersionTag + "] Impact OverlapSphere failed: " + ex.GetType().Name + ": " + ex.Message);
                return stats;
            }

            if (hits == null || hits.Length == 0)
            {
                return stats;
            }

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit == null || IsExcludedTransform(hit.transform))
                {
                    continue;
                }

                Rigidbody rb = null;
                try { rb = hit.attachedRigidbody; } catch { rb = null; }

                if (rb != null)
                {
                    int rbId = SafeInstanceId(rb);
                    if (rbId != 0 && _processedRigidbodies.Contains(rbId))
                    {
                        continue;
                    }

                    if (rbId != 0)
                    {
                        _processedRigidbodies.Add(rbId);
                    }

                    try
                    {
                        if (IsValidDynamicRigidbodyTarget(rb, hit))
                        {
                            ApplyRigidbodyRepulse(rb, hit, center, ref stats);
                            continue;
                        }

                        if (IsValidKinematicTarget(rb, hit))
                        {
                            ApplyKinematicRepulse(rb, hit, center, ref stats);
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogThrottled("[" + VersionTag + "] Rigidbody repulse failed: " + SafeName(rb) + " / " + ex.GetType().Name + ": " + ex.Message);
                    }
                }

                try
                {
                    Transform npcRoot = FindNpcRoot(hit.transform);
                    if (npcRoot != null)
                    {
                        int id = SafeInstanceId(npcRoot);
                        if (id != 0 && _processedTransforms.Contains(id))
                        {
                            continue;
                        }

                        if (id != 0)
                        {
                            _processedTransforms.Add(id);
                        }

                        ApplyNpcRepulse(npcRoot, hit, center, ref stats);
                    }
                }
                catch (Exception ex)
                {
                    LogThrottled("[" + VersionTag + "] NPC repulse failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            return stats;
        }

        private void ApplyRigidbodyRepulse(Rigidbody rb, Collider hit, Vector3 center, ref RedStats stats)
        {
            Vector3 sample = rb.worldCenterOfMass;
            Vector3 away = sample - center;
            float distance = away.magnitude;

            if (distance > ImpactRadius)
            {
                return;
            }

            away = SafeNormalize(away, _castForward + Vector3.up * 0.15f);
            float intensity = ComputeIntensity(distance, ImpactRadius);
            float massScale = 1.0f / Mathf.Sqrt(Mathf.Max(0.35f, rb.mass));
            float impulse = RigidbodyImpulse * (0.25f + 0.75f * intensity) * Mathf.Clamp(massScale, 0.18f, 1.25f);

            rb.AddForce(away * impulse + Vector3.up * RigidbodyUpImpulse * (0.3f + intensity), ForceMode.Impulse);
            rb.AddTorque(UnityEngine.Random.onUnitSphere * impulse * 0.35f, ForceMode.Impulse);

            stats.RigidbodyTargetCount++;
            stats.RigidbodyPushedCount++;
            AddEffectTarget(sample);
        }

        private void ApplyKinematicRepulse(Rigidbody rb, Collider hit, Vector3 center, ref RedStats stats)
        {
            Transform t = rb != null ? rb.transform : null;
            if (t == null || IsExcludedTransform(t))
            {
                return;
            }

            Vector3 sample = SafeColliderCenter(hit, t.position);
            Vector3 away = sample - center;
            float distance = away.magnitude;
            if (distance > ImpactRadius)
            {
                return;
            }

            away = SafeNormalize(away, _castForward + Vector3.up * 0.15f);
            float intensity = ComputeIntensity(distance, ImpactRadius);
            t.position += away * KinematicPush * (0.25f + 0.75f * intensity) + Vector3.up * 0.10f * intensity;

            stats.KinematicPushedCount++;
            AddEffectTarget(sample);
        }

        private void ApplyNpcRepulse(Transform npcRoot, Collider hit, Vector3 center, ref RedStats stats)
        {
            if (npcRoot == null || IsExcludedTransform(npcRoot))
            {
                return;
            }

            Vector3 sample = SafeColliderCenter(hit, npcRoot.position);
            Vector3 away = sample - center;
            float distance = away.magnitude;
            if (distance > ImpactRadius)
            {
                return;
            }

            away = SafeNormalize(away, _castForward + Vector3.up * 0.20f);
            float intensity = ComputeIntensity(distance, ImpactRadius);

            int id = SafeInstanceId(npcRoot);
            if (id != 0 && !_knockedNpcs.ContainsKey(id))
            {
                NpcKnockState state = TryApplyNpcKnockdown(npcRoot, away, intensity, ref stats);
                _knockedNpcs[id] = state;
            }

            npcRoot.position += away * NpcRootPush * (0.35f + 0.65f * intensity) + Vector3.up * 0.12f * intensity;
            TiltNpcRootAway(npcRoot, away, intensity);

            stats.NpcPushedCount++;
            AddEffectTarget(sample);
        }

        private NpcKnockState TryApplyNpcKnockdown(Transform npcRoot, Vector3 away, float intensity, ref RedStats stats)
        {
            NpcKnockState state = new NpcKnockState();
            if (npcRoot == null)
            {
                return state;
            }

            stats.NpcKnockedCount++;

            try
            {
                Animator[] animators = npcRoot.GetComponentsInChildren<Animator>();
                if (animators != null)
                {
                    for (int i = 0; i < animators.Length; i++)
                    {
                        Animator animator = animators[i];
                        if (animator == null)
                        {
                            continue;
                        }

                        bool wasEnabled = false;
                        try { wasEnabled = animator.enabled; } catch { wasEnabled = false; }
                        if (wasEnabled)
                        {
                            state.DisabledAnimators.Add(animator);
                            animator.enabled = false;
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                CharacterController[] controllers = npcRoot.GetComponentsInChildren<CharacterController>();
                if (controllers != null)
                {
                    for (int i = 0; i < controllers.Length; i++)
                    {
                        CharacterController controller = controllers[i];
                        if (controller == null)
                        {
                            continue;
                        }

                        bool wasEnabled = false;
                        try { wasEnabled = controller.enabled; } catch { wasEnabled = false; }
                        if (wasEnabled)
                        {
                            state.DisabledControllers.Add(controller);
                            controller.enabled = false;
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                Rigidbody[] bodies = npcRoot.GetComponentsInChildren<Rigidbody>();
                if (bodies != null)
                {
                    for (int i = 0; i < bodies.Length; i++)
                    {
                        Rigidbody body = bodies[i];
                        if (body == null || IsExcludedTransform(body.transform))
                        {
                            continue;
                        }

                        try
                        {
                            if (body.isKinematic)
                            {
                                body.isKinematic = false;
                            }

                            body.useGravity = true;
                            body.AddForce(away * NpcImpulse * (0.6f + intensity) + Vector3.up * NpcImpulse * 0.20f, ForceMode.Impulse);
                            body.AddTorque(UnityEngine.Random.onUnitSphere * NpcImpulse * 0.60f, ForceMode.Impulse);
                            stats.NpcChildRigidbodyImpulseCount++;
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }

            MelonLogger.Msg("[" + VersionTag + "] NPC red knockdown attempt: " + SafeName(npcRoot) +
                            ", DisabledAnimators=" + state.DisabledAnimators.Count +
                            ", DisabledControllers=" + state.DisabledControllers.Count +
                            ", Away=" + FormatVector(away) +
                            ", ChildRbImpulse=" + stats.NpcChildRigidbodyImpulseCount);

            return state;
        }

        private void TiltNpcRootAway(Transform npcRoot, Vector3 away, float intensity)
        {
            if (npcRoot == null)
            {
                return;
            }

            try
            {
                Vector3 axis = Vector3.Cross(Vector3.up, away);
                if (axis.sqrMagnitude < 0.001f)
                {
                    axis = npcRoot.right;
                }

                float angle = Mathf.Lerp(10f, 35f, Mathf.Clamp01(intensity));
                npcRoot.rotation = Quaternion.AngleAxis(angle, axis.normalized) * npcRoot.rotation;
            }
            catch
            {
            }
        }

        private void FinishImpact()
        {
            if (!_impactActive)
            {
                return;
            }

            _impactActive = false;
            _effect.HideImpact();
            ClearFrameCaches();
            RestoreKnockedNpcControls();

            MelonLogger.Msg("[" + VersionTag + "] Impact finished / 赫 衝撃波終了");
        }

        private void CancelInternal(bool hideEffect)
        {
            _projectileActive = false;
            _impactActive = false;
            ClearFrameCaches();
            RestoreKnockedNpcControls();

            if (hideEffect)
            {
                _effect.HideAll();
            }
        }

        private void ClearFrameCaches()
        {
            _processedRigidbodies.Clear();
            _processedTransforms.Clear();
            _effectTargetPositions.Clear();
        }

        private void RestoreKnockedNpcControls()
        {
            if (!RestoreNpcControlsOnFinish || _knockedNpcs.Count == 0)
            {
                _knockedNpcs.Clear();
                return;
            }

            foreach (KeyValuePair<int, NpcKnockState> pair in _knockedNpcs)
            {
                NpcKnockState state = pair.Value;

                for (int i = 0; i < state.DisabledAnimators.Count; i++)
                {
                    Animator animator = state.DisabledAnimators[i];
                    if (animator == null)
                    {
                        continue;
                    }

                    try { animator.enabled = true; } catch { }
                }

                for (int i = 0; i < state.DisabledControllers.Count; i++)
                {
                    CharacterController controller = state.DisabledControllers[i];
                    if (controller == null)
                    {
                        continue;
                    }

                    try { controller.enabled = true; } catch { }
                }
            }

            _knockedNpcs.Clear();
        }

        private bool TryGetCastPose(out Vector3 origin, out Vector3 forward, out string source, out string debug)
        {
            origin = Vector3.zero;
            forward = Vector3.forward;
            source = "None";
            debug = "PoseDebug=None";

            if (TryGetWorldRightHandPose(out Vector3 handPosition, out Quaternion handRotation, out Transform camTransform, out string poseDebug))
            {
                origin = handPosition;

                if (camTransform != null)
                {
                    // Quest/OpenXRのcontroller forwardは上/横にズレる場合があるので、Blueと同じくCamera forwardを照準に使う。
                    forward = camTransform.forward;
                }
                else
                {
                    forward = PickControllerAxisClosestToCamera(handRotation, Vector3.forward);
                }

                forward = SafeNormalize(forward, Vector3.forward);
                source = "RightHandPosition_CameraForwardAim";
                debug = poseDebug + " Aim=CameraForwardFromHand";
                return true;
            }

            try
            {
                if (_playerRigFinder != null && _playerRigFinder.TryGetCameraTransform(out Transform camFallback) && camFallback != null)
                {
                    origin = camFallback.position + camFallback.right * 0.18f - camFallback.up * 0.10f;
                    forward = camFallback.forward;
                    source = "CameraTransformFallback";
                    debug = "PoseDebug=CameraTransformFallback";
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
                    origin = cam.transform.position + cam.transform.right * 0.18f - cam.transform.up * 0.10f;
                    forward = cam.transform.forward;
                    source = "CameraMainFallback";
                    debug = "PoseDebug=CameraMainFallback";
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryGetWorldRightHandPose(out Vector3 worldPosition, out Quaternion worldRotation, out Transform camTransform, out string debug)
        {
            worldPosition = Vector3.zero;
            worldRotation = Quaternion.identity;
            camTransform = null;
            debug = "PoseDebug=RightHandWorldFailed";

            try
            {
                InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);

                if (!right.isValid || !head.isValid)
                {
                    debug = "PoseDebug=InvalidDevice Right=" + right.isValid + " Head=" + head.isValid;
                    return false;
                }

                bool hasRightPos = right.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 rightLocalPos);
                bool hasRightRot = right.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rightLocalRot);
                bool hasHeadPos = head.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 headLocalPos);
                bool hasHeadRot = head.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion headLocalRot);

                if (!hasRightPos || !hasRightRot || !hasHeadPos || !hasHeadRot)
                {
                    debug = "PoseDebug=MissingFeature RPos=" + hasRightPos + " RRot=" + hasRightRot + " HPos=" + hasHeadPos + " HRot=" + hasHeadRot;
                    return false;
                }

                try
                {
                    if (_playerRigFinder != null)
                    {
                        _playerRigFinder.TryGetCameraTransform(out camTransform);
                    }
                }
                catch
                {
                    camTransform = null;
                }

                if (camTransform == null)
                {
                    Camera cam = Camera.main;
                    if (cam != null)
                    {
                        camTransform = cam.transform;
                    }
                }

                if (camTransform == null)
                {
                    debug = "PoseDebug=NoCameraForTrackingToWorld";
                    return false;
                }

                Quaternion trackingToWorldRot = camTransform.rotation * Quaternion.Inverse(headLocalRot);
                Vector3 trackingToWorldPos = camTransform.position - (trackingToWorldRot * headLocalPos);

                worldPosition = trackingToWorldPos + (trackingToWorldRot * rightLocalPos);
                worldRotation = trackingToWorldRot * rightLocalRot;

                Vector3 axisForward = worldRotation * Vector3.forward;
                Vector3 axisBack = worldRotation * Vector3.back;
                Vector3 axisUp = worldRotation * Vector3.up;
                Vector3 axisRight = worldRotation * Vector3.right;

                debug = "PoseDebug=RightLocal" + FormatVector(rightLocalPos) +
                        " HeadLocal" + FormatVector(headLocalPos) +
                        " CamWorld" + FormatVector(camTransform.position) +
                        " WorldHand" + FormatVector(worldPosition) +
                        " ControllerAxes(F" + FormatVector(axisForward) +
                        " B" + FormatVector(axisBack) +
                        " U" + FormatVector(axisUp) +
                        " R" + FormatVector(axisRight) + ")";

                return true;
            }
            catch (Exception ex)
            {
                debug = "PoseDebug=Exception " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private Vector3 PickControllerAxisClosestToCamera(Quaternion rot, Vector3 fallback)
        {
            Vector3[] axes = new Vector3[]
            {
                rot * Vector3.forward,
                rot * Vector3.back,
                rot * Vector3.up,
                rot * Vector3.down,
                rot * Vector3.right,
                rot * Vector3.left
            };

            Vector3 best = fallback;
            float bestDot = -999f;

            for (int i = 0; i < axes.Length; i++)
            {
                Vector3 a = axes[i];
                if (a.sqrMagnitude < 0.001f)
                {
                    continue;
                }

                a.Normalize();
                float d = Vector3.Dot(a, fallback.normalized);
                if (d > bestDot)
                {
                    bestDot = d;
                    best = a;
                }
            }

            return best;
        }

        private bool ShouldIgnoreProjectileCollider(Collider col)
        {
            if (col == null)
            {
                return true;
            }

            try
            {
                if (!col.enabled || col.isTrigger)
                {
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                if (IsExcludedTransform(col.transform))
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }

            return false;
        }

        private bool IsValidDynamicRigidbodyTarget(Rigidbody rb, Collider hit)
        {
            if (rb == null)
            {
                return false;
            }

            try
            {
                if (rb.gameObject == null || !rb.gameObject.activeInHierarchy)
                {
                    return false;
                }

                if (rb.isKinematic)
                {
                    return false;
                }

                if (rb.mass > MaxTargetMass)
                {
                    return false;
                }

                if (IsExcludedTransform(rb.transform))
                {
                    return false;
                }

                if (hit != null && IsExcludedTransform(hit.transform))
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        private bool IsValidKinematicTarget(Rigidbody rb, Collider hit)
        {
            if (rb == null)
            {
                return false;
            }

            try
            {
                if (rb.gameObject == null || !rb.gameObject.activeInHierarchy)
                {
                    return false;
                }

                if (!rb.isKinematic)
                {
                    return false;
                }

                if (rb.mass > MaxTargetMass)
                {
                    return false;
                }

                if (IsExcludedTransform(rb.transform))
                {
                    return false;
                }

                if (hit != null && IsExcludedTransform(hit.transform))
                {
                    return false;
                }

                return LooksLikeNpcOrCharacter(rb.transform) || FindNpcRoot(rb.transform) != null;
            }
            catch
            {
                return false;
            }
        }

        private Transform FindNpcRoot(Transform start)
        {
            if (start == null || IsExcludedTransform(start))
            {
                return null;
            }

            Transform current = start;
            Transform namedCandidate = null;

            for (int depth = 0; depth < 12 && current != null; depth++)
            {
                if (IsHardStopTransform(current))
                {
                    break;
                }

                if (IsExcludedTransform(current))
                {
                    return null;
                }

                try
                {
                    Animator animator = current.GetComponent<Animator>();
                    if (animator != null)
                    {
                        return current;
                    }
                }
                catch { }

                try
                {
                    CharacterController controller = current.GetComponent<CharacterController>();
                    if (controller != null)
                    {
                        return current;
                    }
                }
                catch { }

                if (LooksLikeNpcName(current.name))
                {
                    namedCandidate = current;
                }

                current = current.parent;
            }

            return namedCandidate;
        }

        private bool LooksLikeNpcOrCharacter(Transform t)
        {
            if (t == null)
            {
                return false;
            }

            Transform current = t;
            for (int depth = 0; depth < 8 && current != null; depth++)
            {
                if (LooksLikeNpcName(current.name))
                {
                    return true;
                }

                try
                {
                    if (current.GetComponent<Animator>() != null || current.GetComponent<CharacterController>() != null)
                    {
                        return true;
                    }
                }
                catch { }

                current = current.parent;
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
            return lower.Contains("babka") ||
                   lower.Contains("grandma") ||
                   lower.Contains("grandmother") ||
                   lower.Contains("granny") ||
                   lower.Contains("npc") ||
                   lower.Contains("enemy") ||
                   lower.Contains("dog") ||
                   lower.Contains("puppy") ||
                   lower.Contains("pigeon") ||
                   lower.Contains("human") ||
                   lower.Contains("character") ||
                   lower.Contains("creature") ||
                   lower.Contains("agent");
        }

        private bool IsExcludedTransform(Transform t)
        {
            if (t == null)
            {
                return true;
            }

            Transform current = t;
            for (int depth = 0; depth < 12 && current != null; depth++)
            {
                string n = current.name;
                if (string.IsNullOrEmpty(n))
                {
                    current = current.parent;
                    continue;
                }

                string lower = n.ToLowerInvariant();
                if (lower.Contains("gojored") ||
                    lower.Contains("gojoblue") ||
                    lower.Contains("gojoinfinity") ||
                    lower.Contains("player") ||
                    lower.Contains("xr origin") ||
                    lower.Contains("xrorigin") ||
                    lower.Contains("xr rig") ||
                    lower.Contains("camera") ||
                    lower.Contains("main camera") ||
                    lower.Contains("hmd") ||
                    lower.Contains("controller") ||
                    lower.Contains("left hand") ||
                    lower.Contains("right hand") ||
                    lower.Contains("handtracking") ||
                    lower.Contains("ui") ||
                    lower.Contains("canvas") ||
                    lower.Contains("eventsystem") ||
                    lower.Contains("scenecontext") ||
                    lower.Contains("projectcontext"))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private bool IsHardStopTransform(Transform t)
        {
            if (t == null)
            {
                return true;
            }

            string n = t.name;
            if (string.IsNullOrEmpty(n))
            {
                return false;
            }

            string lower = n.ToLowerInvariant();
            return lower.Contains("scenecontext") || lower.Contains("projectcontext") || lower.Contains("environment") || lower.Contains("level") || lower.Contains("map");
        }

        private void AddEffectTarget(Vector3 position)
        {
            if (_effectTargetPositions.Count < MaxEffectTargets)
            {
                _effectTargetPositions.Add(position);
            }
        }

        private float ComputeIntensity(float distance, float radius)
        {
            if (radius <= 0.001f)
            {
                return 1.0f;
            }

            return Mathf.Clamp01(1.0f - (distance / radius));
        }

        private Vector3 SafeColliderCenter(Collider hit, Vector3 fallback)
        {
            if (hit == null)
            {
                return fallback;
            }

            try
            {
                return hit.bounds.center;
            }
            catch
            {
                return fallback;
            }
        }

        private Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
        {
            if (v.sqrMagnitude < 0.0001f)
            {
                return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.forward;
            }

            return v.normalized;
        }

        private int SafeInstanceId(UnityEngine.Object obj)
        {
            try
            {
                return obj != null ? obj.GetInstanceID() : 0;
            }
            catch
            {
                return 0;
            }
        }

        private string SafeName(UnityEngine.Object obj)
        {
            try
            {
                return obj != null ? obj.name : "null";
            }
            catch
            {
                return "name_error";
            }
        }

        private string FormatVector(Vector3 v)
        {
            return "(" + v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ", " + v.z.ToString("0.00") + ")";
        }

        private void LogThrottled(string message)
        {
            if (Time.time >= _nextStatusLogTime)
            {
                _nextStatusLogTime = Time.time + 0.75f;
                MelonLogger.Warning(message);
            }
        }

        private struct RedStats
        {
            public int RigidbodyTargetCount;
            public int RigidbodyPushedCount;
            public int KinematicPushedCount;
            public int NpcPushedCount;
            public int NpcKnockedCount;
            public int NpcChildRigidbodyImpulseCount;
        }

        private sealed class NpcKnockState
        {
            public readonly List<Animator> DisabledAnimators = new List<Animator>();
            public readonly List<CharacterController> DisabledControllers = new List<CharacterController>();
        }
    }
}
