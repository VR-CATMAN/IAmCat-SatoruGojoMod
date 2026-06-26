using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.XR;

namespace IAmCatGojoMod
{
    /// <summary>
    /// 蒼 / Blue
    ///
    /// BlueAbility_v3_HandForwardKnockdown:
    /// - v2で判明した「Oculus TouchのdeviceRotation forwardが上/横を向く」問題を回避
    /// - 吸引中心は「右手の位置 + HMD/Cameraの前方向」に固定して、手元から前へ出る見た目を優先
    /// - NPC吸引時にAnimator/CharacterController一時停止、子Rigidbodyへの衝撃、Root傾き補正を試す
    /// - ラグドール化できる構成なら倒れやすく、できない構成でも倒されている風の傾きを作る
    /// </summary>
    public sealed class BlueAbility
    {
        private const string VersionTag = "BlueAbility_v3_HandForwardKnockdown";

        private readonly PlayerRigFinder _playerRigFinder;
        private readonly VrInput _input;
        private readonly BlueEffect _effect = new BlueEffect();

        private bool _initialized;
        private bool _active;

        private Vector3 _corePosition;
        private Vector3 _castOrigin;
        private Vector3 _castForward;
        private float _castStartTime;
        private float _castEndTime;
        private float _nextStatusLogTime;

        // v3は動画映え重視。少し長め・強め。
        private const float CastDistance = 1.65f;
        private const float PullRadius = 5.5f;
        private const float Duration = 2.7f;
        private const float MaxPullForce = 72.0f;
        private const float CoreRadius = 0.65f;
        private const float CoreDamping = 0.72f;
        private const float MaxTargetMass = 650.0f;

        // NPC/kinematic向け。Transform直移動 + ノックダウン補助。
        private const float NpcPullSpeed = 11.0f;
        private const float KinematicPullSpeed = 10.0f;
        private const float MaxNpcSampleDistance = 6.2f;
        private const float NpcKnockImpulse = 5.5f;
        private const float NpcKnockTorque = 8.0f;
        private const float NpcTiltSpeed = 6.5f;
        private const int MaxEffectTargets = 40;

        // true: 蒼終了時にAnimator/CharacterControllerを戻す。falseにすると倒れっぱなし寄りになるがゲーム状態は荒れやすい。
        private const bool RestoreNpcControlsOnFinish = true;

        private readonly List<Vector3> _effectTargetPositions = new List<Vector3>(MaxEffectTargets);
        private readonly HashSet<int> _processedRigidbodies = new HashSet<int>();
        private readonly HashSet<int> _processedTransforms = new HashSet<int>();
        private readonly Dictionary<int, NpcKnockState> _knockedNpcs = new Dictionary<int, NpcKnockState>();

        public bool IsActive => _active;

        public BlueAbility(PlayerRigFinder playerRigFinder, VrInput input)
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

            _castForward = SafeNormalize(_castForward, Vector3.forward);
            _corePosition = _castOrigin + _castForward * CastDistance;
            _castStartTime = Time.time;
            _castEndTime = Time.time + Duration;
            _nextStatusLogTime = 0f;
            _active = true;

            RestoreKnockedNpcControls();
            ClearFrameCaches();

            _effect.Show(_corePosition, PullRadius, CoreRadius);

            MelonLogger.Msg(
                "[" + VersionTag + "] Cast / 蒼 発動. " +
                "Pose=" + poseSource +
                ", Origin=" + FormatVector(_castOrigin) +
                ", Forward=" + FormatVector(_castForward) +
                ", Core=" + FormatVector(_corePosition) +
                ", Radius=" + PullRadius +
                ", Duration=" + Duration +
                ", " + poseDebug
            );
        }

        public void Cancel()
        {
            if (!_active)
            {
                return;
            }

            _active = false;
            _effect.Hide();
            ClearFrameCaches();
            RestoreKnockedNpcControls();

            MelonLogger.Msg("[" + VersionTag + "] Cancel / 蒼 解除");
        }

        public void Update()
        {
            if (!_active)
            {
                _effect.UpdateEffect(Vector3.zero, PullRadius, CoreRadius, 1.0f, null);
                return;
            }

            if (Time.time >= _castEndTime)
            {
                Finish();
                return;
            }

            float normalizedTime = Mathf.Clamp01((Time.time - _castStartTime) / Duration);
            BlueStats stats = ApplyBlueField();

            _effect.UpdateEffect(_corePosition, PullRadius, CoreRadius, normalizedTime, _effectTargetPositions);

            if (Time.time >= _nextStatusLogTime)
            {
                _nextStatusLogTime = Time.time + 0.5f;
                MelonLogger.Msg(
                    "[" + VersionTag + "] Active. " +
                    "RbTargets=" + stats.RigidbodyTargetCount +
                    ", RbPulled=" + stats.RigidbodyPulledCount +
                    ", KinematicPulled=" + stats.KinematicPulledCount +
                    ", NpcPulled=" + stats.NpcPulledCount +
                    ", NpcKnocked=" + stats.NpcKnockedCount +
                    ", ChildRbImpulse=" + stats.NpcChildRigidbodyImpulseCount +
                    ", CoreDamped=" + stats.CoreDampedCount +
                    ", Remaining=" + Mathf.Max(0f, _castEndTime - Time.time).ToString("0.00")
                );
            }
        }

        private void Finish()
        {
            if (!_active)
            {
                return;
            }

            _active = false;
            _effect.Hide();
            ClearFrameCaches();
            RestoreKnockedNpcControls();

            MelonLogger.Msg("[" + VersionTag + "] Finished / 蒼 終了");
        }

        private void ClearFrameCaches()
        {
            _processedRigidbodies.Clear();
            _processedTransforms.Clear();
            _effectTargetPositions.Clear();
        }

        private BlueStats ApplyBlueField()
        {
            BlueStats stats = new BlueStats();
            ClearFrameCaches();

            Collider[] hits;

            try
            {
                hits = Physics.OverlapSphere(_corePosition, PullRadius);
            }
            catch (Exception ex)
            {
                LogThrottled("[" + VersionTag + "] OverlapSphere failed: " + ex.GetType().Name + ": " + ex.Message);
                return stats;
            }

            if (hits == null || hits.Length == 0)
            {
                return stats;
            }

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit == null)
                {
                    continue;
                }

                Rigidbody rb = null;

                try
                {
                    rb = hit.attachedRigidbody;
                }
                catch
                {
                    rb = null;
                }

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
                            ApplyRigidbodyPull(rb, ref stats);
                            continue;
                        }

                        if (IsValidKinematicTarget(rb, hit))
                        {
                            ApplyKinematicPull(rb, hit, ref stats);
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogThrottled("[" + VersionTag + "] Rigidbody pull failed: " + SafeName(rb) + " / " + ex.GetType().Name + ": " + ex.Message);
                    }
                }

                try
                {
                    Transform npcRoot = FindNpcPullRoot(hit.transform);
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

                        ApplyNpcTransformPull(npcRoot, hit, ref stats);
                    }
                }
                catch (Exception ex)
                {
                    LogThrottled("[" + VersionTag + "] NPC pull failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            return stats;
        }

        private void ApplyRigidbodyPull(Rigidbody rb, ref BlueStats stats)
        {
            Vector3 rbPos = rb.worldCenterOfMass;
            Vector3 toCore = _corePosition - rbPos;
            float distance = toCore.magnitude;

            if (distance > PullRadius)
            {
                return;
            }

            stats.RigidbodyTargetCount++;
            AddEffectTarget(rbPos);

            if (distance <= 0.001f)
            {
                rb.velocity *= CoreDamping;
                rb.angularVelocity *= CoreDamping;
                stats.CoreDampedCount++;
                return;
            }

            Vector3 dir = toCore / distance;
            float intensity = ComputeIntensity(distance, PullRadius);
            float force = MaxPullForce * (0.20f + 0.80f * intensity * intensity);

            rb.AddForce(dir * force, ForceMode.Acceleration);

            // 蒼っぽく中心へ吸い込まれながら回転する見た目を追加。
            Vector3 swirl = Vector3.Cross(Vector3.up, dir);
            if (swirl.sqrMagnitude > 0.001f)
            {
                rb.AddForce(swirl.normalized * force * 0.18f, ForceMode.Acceleration);
            }

            stats.RigidbodyPulledCount++;

            if (distance <= CoreRadius)
            {
                rb.velocity *= CoreDamping;
                rb.angularVelocity *= CoreDamping;
                stats.CoreDampedCount++;
            }
        }

        private void ApplyKinematicPull(Rigidbody rb, Collider hit, ref BlueStats stats)
        {
            Transform t = rb.transform;
            if (t == null || IsExcludedTransform(t))
            {
                return;
            }

            Vector3 sample = SafeColliderCenter(hit, t.position);
            Vector3 toCore = _corePosition - sample;
            float distance = toCore.magnitude;

            if (distance > PullRadius)
            {
                return;
            }

            float intensity = ComputeIntensity(distance, PullRadius);
            float step = KinematicPullSpeed * (0.25f + 0.75f * intensity) * Time.deltaTime;
            Vector3 next = Vector3.MoveTowards(t.position, _corePosition, step);

            t.position = next;
            stats.KinematicPulledCount++;
            AddEffectTarget(sample);
        }

        private void ApplyNpcTransformPull(Transform npcRoot, Collider hit, ref BlueStats stats)
        {
            if (npcRoot == null || IsExcludedTransform(npcRoot))
            {
                return;
            }

            Vector3 sample = SafeColliderCenter(hit, npcRoot.position);
            Vector3 toCore = _corePosition - sample;
            float distance = toCore.magnitude;

            if (distance > MaxNpcSampleDistance)
            {
                return;
            }

            int id = SafeInstanceId(npcRoot);
            if (id != 0 && !_knockedNpcs.ContainsKey(id))
            {
                NpcKnockState state = TryApplyNpcKnockdown(npcRoot, sample, ref stats);
                _knockedNpcs[id] = state;
            }

            float intensity = ComputeIntensity(distance, MaxNpcSampleDistance);
            float step = NpcPullSpeed * (0.22f + 0.78f * intensity) * Time.deltaTime;

            Vector3 npcTarget = _corePosition + Vector3.down * 0.35f;
            npcRoot.position = Vector3.MoveTowards(npcRoot.position, npcTarget, step);
            TiltNpcRootTowardCore(npcRoot, intensity);

            stats.NpcPulledCount++;
            AddEffectTarget(sample);
        }

        private NpcKnockState TryApplyNpcKnockdown(Transform npcRoot, Vector3 sample, ref BlueStats stats)
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
                // Animatorを触れなくても続行。
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
                // CharacterControllerを触れなくても続行。
            }

            try
            {
                Rigidbody[] childBodies = npcRoot.GetComponentsInChildren<Rigidbody>();
                if (childBodies != null)
                {
                    for (int i = 0; i < childBodies.Length; i++)
                    {
                        Rigidbody body = childBodies[i];
                        if (body == null || IsExcludedTransform(body.transform))
                        {
                            continue;
                        }

                        Vector3 bodyPos = body.worldCenterOfMass;
                        Vector3 dir = SafeNormalize(_corePosition - bodyPos, SafeNormalize(_corePosition - sample, Vector3.up));
                        Vector3 impulse = dir * NpcKnockImpulse + Vector3.up * 1.5f;

                        try { body.isKinematic = false; } catch { }
                        try { body.useGravity = true; } catch { }
                        try { body.detectCollisions = true; } catch { }
                        try { body.WakeUp(); } catch { }
                        try { body.AddForce(impulse, ForceMode.VelocityChange); } catch { }
                        try { body.AddTorque(UnityEngine.Random.onUnitSphere * NpcKnockTorque, ForceMode.VelocityChange); } catch { }

                        stats.NpcChildRigidbodyImpulseCount++;
                    }
                }
            }
            catch
            {
                // 子Rigidbodyが無いNPCでも、Root傾き補正で倒れ感を出す。
            }

            MelonLogger.Msg(
                "[" + VersionTag + "] NPC knockdown attempt: " + SafeTransformName(npcRoot) +
                ", DisabledAnimators=" + state.DisabledAnimators.Count +
                ", DisabledControllers=" + state.DisabledControllers.Count +
                ", ChildRbImpulse=" + stats.NpcChildRigidbodyImpulseCount
            );

            return state;
        }

        private void TiltNpcRootTowardCore(Transform npcRoot, float intensity)
        {
            if (npcRoot == null)
            {
                return;
            }

            try
            {
                Vector3 flatToCore = _corePosition - npcRoot.position;
                flatToCore.y = 0f;

                if (flatToCore.sqrMagnitude < 0.001f)
                {
                    return;
                }

                Quaternion faceCore = Quaternion.LookRotation(flatToCore.normalized, Vector3.up);
                float tiltAngle = Mathf.Lerp(30.0f, 78.0f, Mathf.Clamp01(intensity));
                Quaternion knockedRotation = faceCore * Quaternion.Euler(tiltAngle, 0f, 18.0f * Mathf.Sin(Time.time * 8.0f));
                npcRoot.rotation = Quaternion.Slerp(npcRoot.rotation, knockedRotation, Time.deltaTime * NpcTiltSpeed);
            }
            catch
            {
                // 回転できないTransformなら無視。
            }
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
                if (state == null)
                {
                    continue;
                }

                for (int i = 0; i < state.DisabledAnimators.Count; i++)
                {
                    try
                    {
                        if (state.DisabledAnimators[i] != null)
                        {
                            state.DisabledAnimators[i].enabled = true;
                        }
                    }
                    catch { }
                }

                for (int i = 0; i < state.DisabledControllers.Count; i++)
                {
                    try
                    {
                        if (state.DisabledControllers[i] != null)
                        {
                            state.DisabledControllers[i].enabled = true;
                        }
                    }
                    catch { }
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

            if (TryGetWorldRightHandPose(out Vector3 handWorldPosition, out Quaternion rightWorldRot, out Transform camTransform, out string poseDebug))
            {
                origin = handWorldPosition;

                // 重要：OpenXR/Oculus TouchのdeviceRotation * Vector3.forward は、実機だと上/横にズレることがある。
                // ここでは「右手の位置」だけ採用し、方向はHMD/Cameraの前方向にして動画映えを安定させる。
                if (camTransform != null)
                {
                    forward = camTransform.forward;
                }
                else
                {
                    forward = PickControllerAxisClosestToCamera(rightWorldRot, Vector3.forward);
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
                    origin = camFallback.position;
                    forward = camFallback.forward;
                    source = "CameraTransformFallback";
                    debug = "PoseDebug=CameraTransformFallback";
                    return true;
                }
            }
            catch
            {
                // fallback続行。
            }

            try
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    origin = cam.transform.position;
                    forward = cam.transform.forward;
                    source = "CameraMainFallback";
                    debug = "PoseDebug=CameraMainFallback";
                    return true;
                }
            }
            catch
            {
                // 取れなければ失敗。
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

                debug =
                    "PoseDebug=RightLocal" + FormatVector(rightLocalPos) +
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

                return LooksLikeNpcOrCharacter(rb.transform) || FindNpcPullRoot(rb.transform) != null;
            }
            catch
            {
                return false;
            }
        }

        private Transform FindNpcPullRoot(Transform start)
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

                if (lower.Contains("gojoblue") ||
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
            try
            {
                if (hit != null)
                {
                    return hit.bounds.center;
                }
            }
            catch { }

            return fallback;
        }

        private Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
        {
            if (value.sqrMagnitude < 0.001f)
            {
                return fallback;
            }

            return value.normalized;
        }

        private int SafeInstanceId(UnityEngine.Object obj)
        {
            try
            {
                if (obj == null)
                {
                    return 0;
                }

                return obj.GetInstanceID();
            }
            catch
            {
                return 0;
            }
        }

        private void LogThrottled(string message)
        {
            if (Time.time >= _nextStatusLogTime)
            {
                _nextStatusLogTime = Time.time + 1.0f;
                MelonLogger.Msg(message);
            }
        }

        private static string SafeName(Rigidbody rb)
        {
            try
            {
                if (rb == null || rb.gameObject == null)
                {
                    return "null";
                }

                return rb.gameObject.name;
            }
            catch
            {
                return "unknown";
            }
        }

        private static string SafeTransformName(Transform t)
        {
            try
            {
                if (t == null)
                {
                    return "null";
                }

                return t.name;
            }
            catch
            {
                return "unknown";
            }
        }

        private static string FormatVector(Vector3 v)
        {
            return "(" + v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ", " + v.z.ToString("0.00") + ")";
        }

        private sealed class NpcKnockState
        {
            public readonly List<Animator> DisabledAnimators = new List<Animator>();
            public readonly List<CharacterController> DisabledControllers = new List<CharacterController>();
        }

        private struct BlueStats
        {
            public int RigidbodyTargetCount;
            public int RigidbodyPulledCount;
            public int KinematicPulledCount;
            public int NpcPulledCount;
            public int NpcKnockedCount;
            public int NpcChildRigidbodyImpulseCount;
            public int CoreDampedCount;
        }
    }
}
