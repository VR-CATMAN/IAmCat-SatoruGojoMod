using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.XR;

namespace IAmCatGojoMod
{
    /// <summary>
    /// 虚式「茈」 / Hollow Purple
    ///
    /// PurpleAbility_v2_LeftMergeCharge:
    /// - 右トリガーで0.7秒チャージ
    /// - 青球と赤球を合体させて大きい紫球を発射
    /// - 紫球は周囲のRigidbodyを吸い込みながら進む
    /// - 紫球に触れた軽い物理オブジェクト/うんこ/小物はSetActive(false)で消滅表現
    /// - NPCは消さず、Animator一時OFF + 子Rigidbody impulse + Root押し出しで彼方へ飛ばす
    /// - 高速すり抜け対策にSphereCastAll + OverlapSphereで通過ルートを削る
    /// </summary>
    public sealed class PurpleAbility
    {
        private const string VersionTag = "PurpleAbility_v2_LeftMergeCharge";

        private enum PurpleState
        {
            Idle,
            Charging,
            Projectile,
            Impact
        }

        private readonly PlayerRigFinder _playerRigFinder;
        private readonly VrInput _input;
        private readonly PurpleEffect _effect = new PurpleEffect();

        private PurpleState _state = PurpleState.Idle;
        private bool _initialized;

        private Vector3 _castOrigin;
        private Vector3 _castForward;
        private Vector3 _chargeCenter;
        private Vector3 _projectilePosition;
        private Vector3 _previousProjectilePosition;
        private Vector3 _impactPosition;

        private float _chargeStartTime;
        private float _projectileStartTime;
        private float _projectileEndTime;
        private float _impactStartTime;
        private float _impactEndTime;
        private float _travelDistance;
        private float _nextStatusLogTime;

        private const float ChargeTime = 1.20f;
        private const float SpawnForwardOffset = 0.72f;
        private const float SpawnLeftOffset = 0.42f;
        private const float SpawnUpOffset = 0.08f;
        private const float ProjectileRadius = 1.05f;
        private const float ProjectileSpeed = 13.5f;
        private const float ProjectileLifeSeconds = 2.15f;
        private const float MaxTravelDistance = 18.0f;

        private const float PullRadius = 4.4f;
        private const float PullAcceleration = 58.0f;
        private const float PullForwardAcceleration = 10.0f;

        private const float EraseRadius = 1.12f;
        private const float PathEraseRadius = 1.05f;
        private const float MaxEraseMass = 80.0f;
        private const float MaxAffectedMass = 1500.0f;

        private const float NpcImpulse = 22.0f;
        private const float NpcRootPush = 1.25f;
        private const float HeavyImpulse = 34.0f;
        private const float ImpactRadius = 6.2f;
        private const float ImpactDuration = 1.25f;
        private const float ImpactImpulse = 60.0f;
        private const float KinematicPush = 0.85f;

        private const int MaxEffectTargets = 80;
        private const int MaxEraseBurstsPerFrame = 10;
        private const bool RestoreNpcControlsOnFinish = true;

        private readonly List<Vector3> _effectTargetPositions = new List<Vector3>(MaxEffectTargets);
        private readonly List<Vector3> _eraseBurstPositions = new List<Vector3>(MaxEffectTargets);
        private readonly HashSet<int> _processedEraseObjects = new HashSet<int>();
        private readonly HashSet<int> _processedNpcRoots = new HashSet<int>();
        private readonly Dictionary<int, NpcKnockState> _knockedNpcs = new Dictionary<int, NpcKnockState>();

        public bool IsActive => _state != PurpleState.Idle;

        public PurpleAbility(PlayerRigFinder playerRigFinder, VrInput input)
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
            _chargeCenter = GetChargeCenter(_castOrigin, _castForward);
            _chargeStartTime = Time.time;
            _nextStatusLogTime = 0f;
            _travelDistance = 0f;
            _state = PurpleState.Charging;

            _effect.ShowCharge(_chargeCenter, _castForward, 0f, ProjectileRadius);

            MelonLogger.Msg("[" + VersionTag + "] Cast / 虚式 茈 チャージ開始. " +
                            "Pose=" + poseSource +
                            ", Origin=" + FormatVector(_castOrigin) +
                            ", Forward=" + FormatVector(_castForward) +
                            ", ChargeCenter=" + FormatVector(_chargeCenter) +
                            ", ChargeTime=" + ChargeTime +
                            ", LeftOffset=" + SpawnLeftOffset +
                            ", " + poseDebug);
        }

        public void Cancel()
        {
            if (_state == PurpleState.Idle)
            {
                return;
            }

            CancelInternal(true);
            MelonLogger.Msg("[" + VersionTag + "] Cancel / 虚式 茈 解除");
        }

        public void Update()
        {
            switch (_state)
            {
                case PurpleState.Charging:
                    UpdateCharging();
                    break;

                case PurpleState.Projectile:
                    UpdateProjectile();
                    break;

                case PurpleState.Impact:
                    UpdateImpact();
                    break;
            }
        }

        private void UpdateCharging()
        {
            float progress = Mathf.Clamp01((Time.time - _chargeStartTime) / ChargeTime);

            // チャージ中はできるだけ右手/視線に追従させる。
            // 取得できない瞬間があっても、最後に取れた姿勢で続行する。
            if (TryGetCastPose(out Vector3 origin, out Vector3 forward, out _, out _))
            {
                _castOrigin = origin;
                _castForward = SafeNormalize(forward, _castForward);
                _chargeCenter = GetChargeCenter(_castOrigin, _castForward);
            }

            _effect.UpdateCharge(_chargeCenter, _castForward, progress, ProjectileRadius);

            if (Time.time >= _chargeStartTime + ChargeTime)
            {
                FireProjectile();
                return;
            }

            if (Time.time >= _nextStatusLogTime)
            {
                _nextStatusLogTime = Time.time + 0.35f;
                MelonLogger.Msg("[" + VersionTag + "] Charging. Progress=" + progress.ToString("0.00") +
                                ", Center=" + FormatVector(_chargeCenter));
            }
        }


        private Vector3 GetChargeCenter(Vector3 origin, Vector3 forward)
        {
            Vector3 f = SafeNormalize(forward, _castForward);
            Vector3 left = Vector3.Cross(f, Vector3.up);
            if (left.sqrMagnitude <= 0.0001f)
            {
                left = Vector3.left;
            }
            else
            {
                left = left.normalized;
            }

            // v2: 画面上で青+赤→紫の合成が見えやすいように、右手前より少し左・少し上に出す。
            return origin + f * SpawnForwardOffset + left * SpawnLeftOffset + Vector3.up * SpawnUpOffset;
        }

        private void FireProjectile()
        {
            _projectilePosition = _chargeCenter;
            _previousProjectilePosition = _projectilePosition;
            _projectileStartTime = Time.time;
            _projectileEndTime = Time.time + ProjectileLifeSeconds;
            _travelDistance = 0f;
            _nextStatusLogTime = 0f;
            _state = PurpleState.Projectile;

            _effect.ShowProjectile(_projectilePosition, _castForward, ProjectileRadius);

            MelonLogger.Msg("[" + VersionTag + "] Fire / 虚式 茈 発射. " +
                            "Projectile=" + FormatVector(_projectilePosition) +
                            ", Forward=" + FormatVector(_castForward) +
                            ", Radius=" + ProjectileRadius +
                            ", PullRadius=" + PullRadius +
                            ", EraseRadius=" + EraseRadius +
                            ", Speed=" + ProjectileSpeed +
                            ", MaxDistance=" + MaxTravelDistance);
        }

        private void UpdateProjectile()
        {
            if (_state != PurpleState.Projectile)
            {
                return;
            }

            float dt = Mathf.Max(Time.deltaTime, 0.001f);
            float move = ProjectileSpeed * dt;

            _previousProjectilePosition = _projectilePosition;
            _projectilePosition += _castForward * move;
            _travelDistance += move;

            PurpleStats stats = new PurpleStats();
            ClearFrameCaches();

            PullNearbyObjects(_projectilePosition, ref stats);
            EraseOrAffectAlongPath(_previousProjectilePosition, _projectilePosition, ref stats);

            float normalizedLife = Mathf.Clamp01((Time.time - _projectileStartTime) / ProjectileLifeSeconds);
            _effect.UpdateProjectile(_projectilePosition, _castForward, normalizedLife, _effectTargetPositions, _eraseBurstPositions);

            if (Time.time >= _projectileEndTime || _travelDistance >= MaxTravelDistance)
            {
                Detonate(_projectilePosition, "LifeOrDistanceEnd", ref stats);
                return;
            }

            if (Time.time >= _nextStatusLogTime)
            {
                _nextStatusLogTime = Time.time + 0.35f;
                MelonLogger.Msg("[" + VersionTag + "] ProjectileActive. " +
                                "Pos=" + FormatVector(_projectilePosition) +
                                ", Pulled=" + stats.PulledCount +
                                ", Erased=" + stats.ErasedCount +
                                ", NpcPushed=" + stats.NpcPushedCount +
                                ", Distance=" + _travelDistance.ToString("0.00") +
                                ", Remaining=" + Mathf.Max(0f, _projectileEndTime - Time.time).ToString("0.00"));
            }
        }

        private void PullNearbyObjects(Vector3 center, ref PurpleStats stats)
        {
            Collider[] hits;
            try
            {
                hits = Physics.OverlapSphere(center, PullRadius);
            }
            catch
            {
                return;
            }

            if (hits == null)
            {
                return;
            }

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit == null || hit.transform == null || IsExcludedTransform(hit.transform))
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

                if (rb == null || IsExcludedTransform(rb.transform) || rb.mass > MaxAffectedMass)
                {
                    continue;
                }

                Vector3 bodyPos = rb.worldCenterOfMass;
                Vector3 toCore = center - bodyPos;
                float distance = toCore.magnitude;
                if (distance < 0.05f)
                {
                    continue;
                }

                float intensity = Mathf.Clamp01(1.0f - distance / PullRadius);
                float force = PullAcceleration * (0.25f + intensity * intensity * 0.95f);
                Vector3 dir = toCore / distance;

                try
                {
                    if (rb.isKinematic)
                    {
                        rb.transform.position += dir * KinematicPush * Time.deltaTime * (0.35f + intensity);
                    }
                    else
                    {
                        rb.AddForce(dir * force + _castForward * PullForwardAcceleration, ForceMode.Acceleration);
                    }

                    AddEffectTarget(bodyPos);
                    stats.PulledCount++;
                }
                catch
                {
                }
            }
        }

        private void EraseOrAffectAlongPath(Vector3 from, Vector3 to, ref PurpleStats stats)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            Vector3 dir = distance > 0.001f ? delta / distance : _castForward;

            try
            {
                if (distance > 0.001f)
                {
                    RaycastHit[] hits = Physics.SphereCastAll(from, PathEraseRadius, dir, distance + PathEraseRadius * 0.5f);
                    if (hits != null)
                    {
                        for (int i = 0; i < hits.Length; i++)
                        {
                            Collider col = hits[i].collider;
                            if (col == null)
                            {
                                continue;
                            }

                            Vector3 point = hits[i].point.sqrMagnitude > 0.001f ? hits[i].point : col.transform.position;
                            TryEraseOrAffectCollider(col, point, ref stats);
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                Collider[] overlaps = Physics.OverlapSphere(to, EraseRadius);
                if (overlaps != null)
                {
                    for (int i = 0; i < overlaps.Length; i++)
                    {
                        Collider col = overlaps[i];
                        if (col == null)
                        {
                            continue;
                        }

                        TryEraseOrAffectCollider(col, col.transform.position, ref stats);
                    }
                }
            }
            catch
            {
            }
        }

        private void TryEraseOrAffectCollider(Collider hit, Vector3 hitPoint, ref PurpleStats stats)
        {
            if (hit == null || hit.transform == null || IsExcludedTransform(hit.transform))
            {
                return;
            }

            Transform npcRoot = FindNpcRoot(hit.transform);
            if (npcRoot != null)
            {
                ApplyNpcPurple(npcRoot, hit, _projectilePosition, ref stats);
                return;
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
                if (IsExcludedTransform(rb.transform))
                {
                    return;
                }

                if (rb.mass <= MaxEraseMass && CanEraseObject(rb.transform, hit.transform))
                {
                    Transform eraseTarget = ChooseEraseTarget(rb.transform, hit.transform);
                    EraseTransform(eraseTarget, hitPoint, ref stats);
                    return;
                }

                PushHeavyRigidbody(rb, _projectilePosition, ref stats);
                return;
            }

            // Rigidbodyなしの小物は誤爆で床/壁/家を消しやすいので、かなり限定する。
            if (LooksLikeDisposableName(hit.transform.name) && CanEraseObject(hit.transform, hit.transform))
            {
                EraseTransform(hit.transform, hitPoint, ref stats);
            }
        }

        private void EraseTransform(Transform target, Vector3 hitPoint, ref PurpleStats stats)
        {
            if (target == null || IsExcludedTransform(target) || !CanEraseObject(target, target))
            {
                return;
            }

            int id = SafeInstanceId(target.gameObject);
            if (id != 0 && _processedEraseObjects.Contains(id))
            {
                return;
            }

            if (id != 0)
            {
                _processedEraseObjects.Add(id);
            }

            Vector3 pos = target.position;
            AddEffectTarget(pos);
            AddEraseBurst(pos);

            try
            {
                target.gameObject.SetActive(false);
                stats.ErasedCount++;
                MelonLogger.Msg("[" + VersionTag + "] Erased / 削除: " + GetPath(target) + ", Pos=" + FormatVector(pos));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[" + VersionTag + "] Erase failed: " + target.name + " / " + ex.Message);
            }
        }

        private void PushHeavyRigidbody(Rigidbody rb, Vector3 center, ref PurpleStats stats)
        {
            if (rb == null || IsExcludedTransform(rb.transform))
            {
                return;
            }

            Vector3 pos = rb.worldCenterOfMass;
            Vector3 away = pos - center;
            if (away.sqrMagnitude < 0.001f)
            {
                away = _castForward;
            }
            away = SafeNormalize(away + _castForward * 0.65f + Vector3.up * 0.15f, _castForward);

            float massFactor = Mathf.Clamp(80.0f / Mathf.Max(1.0f, rb.mass), 0.25f, 1.25f);
            try
            {
                if (rb.isKinematic)
                {
                    rb.transform.position += away * KinematicPush * 0.35f;
                }
                else
                {
                    rb.AddForce(away * HeavyImpulse * massFactor + Vector3.up * 8.0f * massFactor, ForceMode.Impulse);
                    rb.AddTorque(UnityEngine.Random.onUnitSphere * HeavyImpulse * 0.5f * massFactor, ForceMode.Impulse);
                }

                AddEffectTarget(pos);
                stats.HeavyPushedCount++;
            }
            catch
            {
            }
        }

        private void ApplyNpcPurple(Transform npcRoot, Collider hit, Vector3 center, ref PurpleStats stats)
        {
            if (npcRoot == null || IsExcludedTransform(npcRoot))
            {
                return;
            }

            int id = SafeInstanceId(npcRoot.gameObject);
            if (id != 0 && _processedNpcRoots.Contains(id))
            {
                return;
            }

            if (id != 0)
            {
                _processedNpcRoots.Add(id);
            }

            Vector3 npcPos = npcRoot.position;
            Vector3 away = npcPos - center;
            if (away.sqrMagnitude < 0.001f)
            {
                away = _castForward;
            }
            away = SafeNormalize(away + _castForward * 1.15f + Vector3.up * 0.25f, _castForward);

            if (id != 0 && !_knockedNpcs.ContainsKey(id))
            {
                NpcKnockState state = TryApplyNpcKnockdown(npcRoot, away, ref stats);
                _knockedNpcs[id] = state;
            }

            try
            {
                npcRoot.position += away * NpcRootPush * Time.deltaTime * 5.0f + Vector3.up * 0.06f;
                TiltNpcRootAway(npcRoot, away);
            }
            catch
            {
            }

            AddEffectTarget(npcPos);
            stats.NpcPushedCount++;
        }

        private NpcKnockState TryApplyNpcKnockdown(Transform npcRoot, Vector3 away, ref PurpleStats stats)
        {
            NpcKnockState state = new NpcKnockState();
            if (npcRoot == null)
            {
                return state;
            }

            state.Root = npcRoot;
            stats.NpcKnockedCount++;

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
                        state.DisabledAnimatorCount++;
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
                        state.DisabledControllerCount++;
                    }
                }
            }
            catch
            {
            }

            try
            {
                Rigidbody[] bodies = npcRoot.GetComponentsInChildren<Rigidbody>(true);
                if (bodies != null)
                {
                    for (int i = 0; i < bodies.Length; i++)
                    {
                        Rigidbody body = bodies[i];
                        if (body == null || IsExcludedTransform(body.transform))
                        {
                            continue;
                        }

                        state.Rigidbodies.Add(new RigidbodyState
                        {
                            Rigidbody = body,
                            IsKinematic = body.isKinematic,
                            UseGravity = body.useGravity
                        });

                        try
                        {
                            body.isKinematic = false;
                            body.useGravity = true;
                            body.AddForce(away * NpcImpulse + Vector3.up * NpcImpulse * 0.35f, ForceMode.Impulse);
                            body.AddTorque(UnityEngine.Random.onUnitSphere * NpcImpulse * 0.8f, ForceMode.Impulse);
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

            MelonLogger.Msg("[" + VersionTag + "] NPC purple knockdown: " + npcRoot.name +
                            ", DisabledAnimators=" + state.DisabledAnimatorCount +
                            ", DisabledControllers=" + state.DisabledControllerCount +
                            ", ChildRbImpulse=" + stats.NpcChildRigidbodyImpulseCount);

            return state;
        }

        private void TiltNpcRootAway(Transform npcRoot, Vector3 away)
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

                Quaternion tilt = Quaternion.AngleAxis(18.0f * Time.deltaTime * 6.0f, axis.normalized);
                npcRoot.rotation = tilt * npcRoot.rotation;
            }
            catch
            {
            }
        }

        private void Detonate(Vector3 center, string reason, ref PurpleStats preImpactStats)
        {
            _state = PurpleState.Impact;
            _impactPosition = center;
            _impactStartTime = Time.time;
            _impactEndTime = Time.time + ImpactDuration;
            _nextStatusLogTime = 0f;

            PurpleStats impactStats = new PurpleStats();
            ApplyImpact(center, ref impactStats);

            _effect.ShowImpact(center, ImpactRadius, _effectTargetPositions);

            MelonLogger.Msg("[" + VersionTag + "] Impact / 虚式 茈 終端. Reason=" + reason +
                            ", Pos=" + FormatVector(center) +
                            ", PreErased=" + preImpactStats.ErasedCount +
                            ", ImpactErased=" + impactStats.ErasedCount +
                            ", ImpactNpcPushed=" + impactStats.NpcPushedCount +
                            ", ImpactHeavyPushed=" + impactStats.HeavyPushedCount);
        }

        private void ApplyImpact(Vector3 center, ref PurpleStats stats)
        {
            ClearFrameCaches();

            Collider[] hits;
            try
            {
                hits = Physics.OverlapSphere(center, ImpactRadius);
            }
            catch
            {
                return;
            }

            if (hits == null)
            {
                return;
            }

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit == null || hit.transform == null || IsExcludedTransform(hit.transform))
                {
                    continue;
                }

                Transform npcRoot = FindNpcRoot(hit.transform);
                if (npcRoot != null)
                {
                    ApplyNpcPurple(npcRoot, hit, center, ref stats);
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
                    if (rb.mass <= MaxEraseMass * 0.75f && CanEraseObject(rb.transform, hit.transform))
                    {
                        EraseTransform(ChooseEraseTarget(rb.transform, hit.transform), hit.transform.position, ref stats);
                    }
                    else
                    {
                        Vector3 pos = rb.worldCenterOfMass;
                        Vector3 away = pos - center;
                        if (away.sqrMagnitude < 0.001f)
                        {
                            away = _castForward;
                        }
                        away = SafeNormalize(away + Vector3.up * 0.2f, _castForward);
                        float intensity = Mathf.Clamp01(1.0f - away.magnitude / ImpactRadius);
                        float massFactor = Mathf.Clamp(80.0f / Mathf.Max(1.0f, rb.mass), 0.25f, 1.35f);

                        try
                        {
                            if (rb.isKinematic)
                            {
                                rb.transform.position += away * KinematicPush * (0.5f + intensity);
                            }
                            else
                            {
                                rb.AddForce(away * ImpactImpulse * massFactor + Vector3.up * 12.0f * massFactor, ForceMode.Impulse);
                                rb.AddTorque(UnityEngine.Random.onUnitSphere * ImpactImpulse * 0.45f * massFactor, ForceMode.Impulse);
                            }

                            AddEffectTarget(pos);
                            stats.HeavyPushedCount++;
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        private void UpdateImpact()
        {
            if (_state != PurpleState.Impact)
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

        private void FinishImpact()
        {
            _effect.Hide();
            RestoreKnockedNpcControls();
            _state = PurpleState.Idle;
            MelonLogger.Msg("[" + VersionTag + "] Finished / 虚式 茈 終了");
        }

        private void CancelInternal(bool hideEffect)
        {
            _state = PurpleState.Idle;
            if (hideEffect)
            {
                _effect.Hide();
            }
        }

        private void ClearFrameCaches()
        {
            _effectTargetPositions.Clear();
            _eraseBurstPositions.Clear();
            _processedNpcRoots.Clear();
            // _processedEraseObjects はProjectile中は維持して、同じ物体の多重SetActiveを防ぐ。
            // Idle/新規Cast時のみ明示的に消す。
            if (_state == PurpleState.Idle || _state == PurpleState.Charging)
            {
                _processedEraseObjects.Clear();
            }
        }

        private void AddEffectTarget(Vector3 position)
        {
            if (_effectTargetPositions.Count >= MaxEffectTargets)
            {
                return;
            }

            _effectTargetPositions.Add(position);
        }

        private void AddEraseBurst(Vector3 position)
        {
            if (_eraseBurstPositions.Count >= MaxEffectTargets)
            {
                return;
            }

            _eraseBurstPositions.Add(position);

            if (_eraseBurstPositions.Count <= MaxEraseBurstsPerFrame)
            {
                _effect.SpawnEraseBurst(position);
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

                for (int i = 0; i < state.Animators.Count; i++)
                {
                    AnimatorState s = state.Animators[i];
                    try
                    {
                        if (s.Animator != null)
                        {
                            s.Animator.enabled = s.Enabled;
                        }
                    }
                    catch
                    {
                    }
                }

                for (int i = 0; i < state.Controllers.Count; i++)
                {
                    ControllerState s = state.Controllers[i];
                    try
                    {
                        if (s.Controller != null)
                        {
                            s.Controller.enabled = s.Enabled;
                        }
                    }
                    catch
                    {
                    }
                }

                for (int i = 0; i < state.Rigidbodies.Count; i++)
                {
                    RigidbodyState s = state.Rigidbodies[i];
                    try
                    {
                        if (s.Rigidbody != null)
                        {
                            s.Rigidbody.isKinematic = s.IsKinematic;
                            s.Rigidbody.useGravity = s.UseGravity;
                        }
                    }
                    catch
                    {
                    }
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

            Transform cameraTransform = TryGetCameraTransform();

            bool hasRightPose = TryGetRightHandLocalPose(out Vector3 rightLocalPos, out Quaternion rightLocalRot);
            bool hasHeadPose = TryGetHeadLocalPose(out Vector3 headLocalPos, out Quaternion headLocalRot);

            if (hasRightPose && hasHeadPose && cameraTransform != null)
            {
                // XR local空間の right/head 差分を、Cameraのワールド姿勢に乗せる。
                Vector3 localDelta = rightLocalPos - headLocalPos;
                origin = cameraTransform.position + cameraTransform.rotation * localDelta;

                // Quest/OpenXRのController forwardは指先方向とズレやすいので、動画用MVPとしてCamera forwardを採用。
                forward = cameraTransform.forward;

                source = "RightHandPosition_CameraForwardAim";
                debug = "PoseDebug=RightLocal" + FormatVector(rightLocalPos) +
                        " HeadLocal" + FormatVector(headLocalPos) +
                        " CamWorld" + FormatVector(cameraTransform.position) +
                        " WorldHand" + FormatVector(origin) +
                        " Aim=CameraForwardFromHand";
                return true;
            }

            if (cameraTransform != null)
            {
                origin = cameraTransform.position + cameraTransform.right * 0.22f - cameraTransform.up * 0.05f + cameraTransform.forward * 0.35f;
                forward = cameraTransform.forward;
                source = "CameraFallback";
                debug = "PoseDebug=CamWorld" + FormatVector(cameraTransform.position);
                return true;
            }

            try
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    origin = cam.transform.position + cam.transform.right * 0.22f - cam.transform.up * 0.05f + cam.transform.forward * 0.35f;
                    forward = cam.transform.forward;
                    source = "CameraMainFallback";
                    debug = "PoseDebug=Camera.main";
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private Transform TryGetCameraTransform()
        {
            try
            {
                if (_playerRigFinder != null && _playerRigFinder.TryGetCameraTransform(out Transform cameraTransform) && cameraTransform != null)
                {
                    return cameraTransform;
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
                    return cam.transform;
                }
            }
            catch
            {
            }

            return null;
        }

        private bool TryGetRightHandLocalPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            try
            {
                InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                if (!right.isValid)
                {
                    return false;
                }

                bool hasPos = right.TryGetFeatureValue(CommonUsages.devicePosition, out position);
                bool hasRot = right.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);
                return hasPos && hasRot;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetHeadLocalPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            try
            {
                InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                if (!head.isValid)
                {
                    return false;
                }

                bool hasPos = head.TryGetFeatureValue(CommonUsages.devicePosition, out position);
                bool hasRot = head.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);
                return hasPos && hasRot;
            }
            catch
            {
                return false;
            }
        }

        private Transform ChooseEraseTarget(Transform rbTransform, Transform hitTransform)
        {
            if (rbTransform != null && !IsExcludedTransform(rbTransform) && CanEraseObject(rbTransform, hitTransform))
            {
                return rbTransform;
            }

            return hitTransform;
        }

        private bool CanEraseObject(Transform candidate, Transform hitTransform)
        {
            if (candidate == null || IsExcludedTransform(candidate))
            {
                return false;
            }

            if (FindNpcRoot(candidate) != null)
            {
                return false;
            }

            string path = GetPath(candidate).ToLowerInvariant();
            string name = candidate.name != null ? candidate.name.ToLowerInvariant() : string.Empty;

            // Player/XR/UI/manager系はパスに含まれるだけで除外。
            string[] pathDangerous =
            {
                "player", "cat", "xr", "camera", "hmd", "controller", "hand", "rig",
                "ui", "canvas", "eventsystem", "scenecontext", "projectcontext",
                "navmesh", "light", "reflection", "volume", "audio", "manager", "system"
            };

            for (int i = 0; i < pathDangerous.Length; i++)
            {
                if (path.Contains(pathDangerous[i]) || name.Contains(pathDangerous[i]))
                {
                    return false;
                }
            }

            // floor/wall/houseなどは「親パスに含まれるだけ」なら許可する。
            // I Am Catでは小物がHouse配下にいる可能性が高いので、名前そのものが危険Rootっぽい場合だけ除外。
            string[] nameDangerous =
            {
                "floor", "wall", "ceiling", "roof", "stair", "stairs", "house", "room", "map", "scene", "level", "terrain"
            };

            for (int i = 0; i < nameDangerous.Length; i++)
            {
                if (name.Contains(nameDangerous[i]))
                {
                    return false;
                }
            }

            // 親に多数のRenderer/Colliderを抱えるRootは消すとシーンが壊れやすい。
            try
            {
                Collider[] cols = candidate.GetComponentsInChildren<Collider>(true);
                Renderer[] renderers = candidate.GetComponentsInChildren<Renderer>(true);
                if ((cols != null && cols.Length > 24) || (renderers != null && renderers.Length > 24))
                {
                    if (!LooksLikeDisposableName(candidate.name))
                    {
                        return false;
                    }
                }
            }
            catch
            {
            }

            return true;
        }

        private bool LooksLikeDisposableName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            string n = name.ToLowerInvariant();
            return n.Contains("poop") ||
                   n.Contains("shit") ||
                   n.Contains("food") ||
                   n.Contains("stuff") ||
                   n.Contains("item") ||
                   n.Contains("toy") ||
                   n.Contains("bottle") ||
                   n.Contains("can") ||
                   n.Contains("plate") ||
                   n.Contains("cup") ||
                   n.Contains("box") ||
                   n.Contains("fragment") ||
                   n.Contains("piece") ||
                   n.Contains("break") ||
                   n.Contains("debris");
        }

        private Transform FindNpcRoot(Transform start)
        {
            if (start == null || IsExcludedTransform(start))
            {
                return null;
            }

            Transform current = start;
            for (int depth = 0; depth < 8 && current != null; depth++)
            {
                if (IsExcludedTransform(current))
                {
                    return null;
                }

                if (LooksLikeNpcName(current.name))
                {
                    return current;
                }

                current = current.parent;
            }

            return null;
        }

        private bool LooksLikeNpcName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            string n = name.ToLowerInvariant();
            return n.Contains("charactercontainer") ||
                   n.Contains("babka") ||
                   n.Contains("granny") ||
                   n.Contains("grandma") ||
                   n.Contains("mechanic") ||
                   n.Contains("npc") ||
                   n.Contains("pigeon") ||
                   n.Contains("dog") ||
                   n.Contains("enemy");
        }

        private bool IsExcludedTransform(Transform t)
        {
            if (t == null)
            {
                return true;
            }

            string path = GetPath(t).ToLowerInvariant();
            string n = t.name != null ? t.name.ToLowerInvariant() : string.Empty;

            string[] excluded =
            {
                "gojopurple", "gojored", "gojoblue", "gojoinfinity", "gojoeffect",
                "player", "xr", "camera", "main camera", "hmd", "controller", "hand", "rig",
                "ui", "canvas", "eventsystem", "scenecontext", "projectcontext"
            };

            for (int i = 0; i < excluded.Length; i++)
            {
                if (path.Contains(excluded[i]) || n.Contains(excluded[i]))
                {
                    return true;
                }
            }

            return false;
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
                while (current != null && guard < 16)
                {
                    path = current.name + "/" + path;
                    current = current.parent;
                    guard++;
                }

                return path;
            }
            catch
            {
                return t.name != null ? t.name : "unknown";
            }
        }

        private int SafeInstanceId(UnityEngine.Object obj)
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

        private Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
        {
            if (value.sqrMagnitude <= 0.0001f)
            {
                return fallback.sqrMagnitude <= 0.0001f ? Vector3.forward : fallback.normalized;
            }

            return value.normalized;
        }

        private string FormatVector(Vector3 v)
        {
            return "(" + v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ", " + v.z.ToString("0.00") + ")";
        }

        private sealed class PurpleStats
        {
            public int PulledCount;
            public int ErasedCount;
            public int HeavyPushedCount;
            public int NpcPushedCount;
            public int NpcKnockedCount;
            public int NpcChildRigidbodyImpulseCount;
        }

        private sealed class NpcKnockState
        {
            public Transform Root;
            public readonly List<AnimatorState> Animators = new List<AnimatorState>();
            public readonly List<ControllerState> Controllers = new List<ControllerState>();
            public readonly List<RigidbodyState> Rigidbodies = new List<RigidbodyState>();
            public int DisabledAnimatorCount;
            public int DisabledControllerCount;
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

        private struct RigidbodyState
        {
            public Rigidbody Rigidbody;
            public bool IsKinematic;
            public bool UseGravity;
        }
    }
}
