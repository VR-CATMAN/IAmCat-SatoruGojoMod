using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace IAmCatGojoMod
{
    /// <summary>
    /// 瞬間移動 / Teleport
    ///
    /// TeleportAbility_v3_ThreeDimensionalGaze:
    /// - 能力選択には入れない常時スキル
    /// - 右グリップ押下で、HMD視線方向へ短距離テレポート
    /// - 軽い上下ブレは水平移動、明確に上/下を見た場合はY方向も移動
    /// - SphereCastで壁/家具を検出し、衝突手前で停止
    /// - 到着候補がCollider内なら少しずつ手前へ戻して安全地点を探す
    /// - 床がない候補は避ける
    ///
    /// 注意:
    /// - v3では上下移動対応。
    /// - ただし軽い首の上下ブレで勝手に浮かないよう、VerticalDeadZone未満は水平化する。
    /// </summary>
    public sealed class TeleportAbility
    {
        private const string VersionTag = "TeleportAbility_v3_ThreeDimensionalGaze";

        private readonly PlayerRigFinder _playerRigFinder;
        private readonly TeleportBlinkEffect _effect = new TeleportBlinkEffect();

        private bool _initialized;
        private float _lastTeleportTime = -999f;
        private float _nextLogTime;

        // 動画映えと安全性のバランス。長すぎると壁裏判定や床判定が荒れやすい。
        private const float TeleportDistance = 3.8f;
        private const float VerticalDeadZone = 0.18f;
        private const float MaxVerticalRatio = 0.86f;
        private const float CooldownSeconds = 0.45f;

        // 猫の体のざっくり判定。大きすぎると家具だらけの部屋で失敗しやすい。
        private const float BodyRadius = 0.26f;
        private static readonly Vector3 BodyCenterOffsetFromCamera = new Vector3(0f, -0.35f, 0f);

        // 壁や家具にぶつかった場合、この分だけ手前に止める。
        private const float WallStopBuffer = 0.42f;
        private const float MinTeleportDistance = 0.65f;
        private const float BackoffStep = 0.22f;
        private const int MaxBackoffAttempts = 20;

        // 足場チェック。床が取れない場所へ飛ばない。
        private const float GroundProbeUp = 0.65f;
        private const float GroundProbeDown = 2.20f;
        private const bool RequireGround = true;

        // trueにすると左グリップでも瞬間移動できる。掴み操作との誤爆を避けるためv1ではfalse。
        public const bool UseLeftGripToo = false;

        public TeleportAbility(PlayerRigFinder playerRigFinder)
        {
            _playerRigFinder = playerRigFinder;
        }

        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _effect.Initialize();
            MelonLogger.Msg("[" + VersionTag + "] Initialized. Distance=" + TeleportDistance.ToString("0.00") +
                            ", Cooldown=" + CooldownSeconds.ToString("0.00") +
                            ", BodyRadius=" + BodyRadius.ToString("0.00") +
                            ", VerticalDeadZone=" + VerticalDeadZone.ToString("0.00") +
                            ", RequireGround=" + RequireGround);
        }

        public void TryBlink()
        {
            if (!_initialized)
            {
                Initialize();
            }

            float now = Time.time;
            if (now - _lastTeleportTime < CooldownSeconds)
            {
                return;
            }

            if (_playerRigFinder == null || !_playerRigFinder.TryGetCameraTransform(out Transform cameraTransform))
            {
                LogThrottled("[" + VersionTag + "] Teleport failed. Camera not found.");
                return;
            }

            if (!TryGetGazeTeleportDirection(cameraTransform, out Vector3 direction, out bool verticalMode))
            {
                LogThrottled("[" + VersionTag + "] Teleport failed. View direction invalid.");
                return;
            }

            Transform moveRoot = ResolveMoveRoot(cameraTransform);
            if (moveRoot == null)
            {
                LogThrottled("[" + VersionTag + "] Teleport failed. MoveRoot not found.");
                return;
            }

            Vector3 startCameraPosition = cameraTransform.position;
            Vector3 startRootPosition = moveRoot.position;
            Vector3 startBodyCenter = startCameraPosition + BodyCenterOffsetFromCamera;

            if (!TryFindSafeDelta(moveRoot, startBodyCenter, direction, out Vector3 safeDelta, out string reason))
            {
                LogThrottled("[" + VersionTag + "] Teleport blocked. Reason=" + reason +
                             ", Root=" + SafeName(moveRoot) +
                             ", Cam=" + FormatVector(startCameraPosition) +
                             ", Dir=" + FormatVector(direction));
                return;
            }

            Vector3 targetRootPosition = startRootPosition + safeDelta;
            Vector3 targetCameraPosition = startCameraPosition + safeDelta;

            ApplyRootMove(moveRoot, targetRootPosition);
            _effect.Show(startCameraPosition, targetCameraPosition, 0.28f);

            _lastTeleportTime = now;

            MelonLogger.Msg("[" + VersionTag + "] Blink / 瞬間移動. " +
                            "Root=" + SafeName(moveRoot) +
                            ", Distance=" + safeDelta.magnitude.ToString("0.00") +
                            ", From=" + FormatVector(startCameraPosition) +
                            ", To=" + FormatVector(targetCameraPosition) +
                            ", Direction=" + FormatVector(direction) +
                            ", VerticalMode=" + verticalMode);
        }

        public void Cancel()
        {
            _effect.Hide();
        }

        public void Update()
        {
            _effect.UpdateEffect();
        }

        private bool TryGetGazeTeleportDirection(Transform cameraTransform, out Vector3 direction, out bool verticalMode)
        {
            direction = Vector3.zero;
            verticalMode = false;

            if (cameraTransform == null)
            {
                return false;
            }

            Vector3 forward = cameraTransform.forward;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            forward = forward.normalized;

            // 普通に前を見ているときの小さい上下ブレでは浮いたり沈んだりしない。
            // 明確に上/下を見たときだけ、五条っぽく3D方向へ瞬間移動する。
            if (Mathf.Abs(forward.y) < VerticalDeadZone)
            {
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f)
                {
                    return false;
                }

                direction = forward.normalized;
                verticalMode = false;
                return true;
            }

            // 真上/真下すぎると水平成分がほぼなくなり、酔いやすい垂直ワープになるので少しだけ制限する。
            forward.y = Mathf.Clamp(forward.y, -MaxVerticalRatio, MaxVerticalRatio);
            if (forward.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            direction = forward.normalized;
            verticalMode = true;
            return true;
        }

        private bool TryFindSafeDelta(Transform moveRoot, Vector3 startBodyCenter, Vector3 direction, out Vector3 safeDelta, out string reason)
        {
            safeDelta = Vector3.zero;
            reason = "Unknown";

            float desiredDistance = TeleportDistance;
            float maxTravelDistance = desiredDistance;

            HashSet<int> startOverlapColliderIds = CollectStartOverlapColliderIds(moveRoot, startBodyCenter);

            bool hitWall = TryGetNearestSolidHit(moveRoot, startBodyCenter, direction, desiredDistance, out RaycastHit nearestHit);
            if (hitWall)
            {
                maxTravelDistance = Mathf.Max(0f, nearestHit.distance - WallStopBuffer);
            }

            if (maxTravelDistance < MinTeleportDistance)
            {
                reason = hitWall
                    ? "ObstacleTooClose hit=" + SafeName(nearestHit.collider)
                    : "TravelTooShort";
                return false;
            }

            float candidateDistance = maxTravelDistance;
            for (int attempt = 0; attempt < MaxBackoffAttempts; attempt++)
            {
                if (candidateDistance < MinTeleportDistance)
                {
                    break;
                }

                Vector3 candidateBodyCenter = startBodyCenter + direction * candidateDistance;

                if (!IsBodyClearAt(moveRoot, candidateBodyCenter, startOverlapColliderIds, out string clearReason))
                {
                    reason = "CandidateBlocked attempt=" + attempt + " " + clearReason;
                    candidateDistance -= BackoffStep;
                    continue;
                }

                if (RequireGround && !HasGroundBelow(moveRoot, candidateBodyCenter, out string groundReason))
                {
                    reason = "NoGround attempt=" + attempt + " " + groundReason;
                    candidateDistance -= BackoffStep;
                    continue;
                }

                safeDelta = direction * candidateDistance;
                reason = hitWall ? "StoppedBeforeWall" : "Clear";
                return true;
            }

            reason = "NoSafeCandidateAfterBackoff last=" + reason;
            return false;
        }

        private bool TryGetNearestSolidHit(Transform moveRoot, Vector3 origin, Vector3 direction, float distance, out RaycastHit nearestHit)
        {
            nearestHit = new RaycastHit();
            float nearestDistance = float.MaxValue;
            bool found = false;

            try
            {
                RaycastHit[] hits = Physics.SphereCastAll(origin, BodyRadius, direction, distance);
                if (hits == null)
                {
                    return false;
                }

                for (int i = 0; i < hits.Length; i++)
                {
                    RaycastHit hit = hits[i];
                    Collider col = hit.collider;

                    if (!IsValidBlockingCollider(moveRoot, col))
                    {
                        continue;
                    }

                    // 開始地点にほぼ重なっているものは、自分/床/接触中オブジェクトの可能性が高いので無視。
                    if (hit.distance < 0.08f)
                    {
                        continue;
                    }

                    if (hit.distance < nearestDistance)
                    {
                        nearestDistance = hit.distance;
                        nearestHit = hit;
                        found = true;
                    }
                }
            }
            catch (Exception ex)
            {
                reasonlessLog("[" + VersionTag + "] SphereCastAll failed: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }

            return found;
        }

        private bool IsBodyClearAt(Transform moveRoot, Vector3 bodyCenter, HashSet<int> startOverlapColliderIds, out string reason)
        {
            reason = "Clear";

            try
            {
                Collider[] colliders = Physics.OverlapSphere(bodyCenter, BodyRadius);
                if (colliders == null)
                {
                    return true;
                }

                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider col = colliders[i];
                    if (!IsValidBlockingCollider(moveRoot, col))
                    {
                        continue;
                    }

                    // I Am Catの部屋Collider/SecretRoom系は、プレイヤー開始位置でも既にOverlapしていることがある。
                    // v1ではこれを「到着地点が壁に埋まっている」と誤判定して、常にキャンセルしていた。
                    // 既に開始地点で重なっているColliderはルームシェル扱いとして、到着Overlap判定だけ無視する。
                    // 壁抜け対策自体は、移動経路のSphereCastで継続する。
                    if (IsStartOverlapCollider(col, startOverlapColliderIds))
                    {
                        continue;
                    }

                    // Sphereの下側に触れている床は、到着地点の足場なのでブロック扱いしない。
                    // これを入れないと、猫のカメラ位置が低いシーンで床Overlapにより毎回失敗しやすい。
                    if (LooksLikeFloorBelow(col, bodyCenter))
                    {
                        continue;
                    }

                    reason = "Overlap=" + SafeName(col);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = "OverlapSphereException=" + ex.GetType().Name + ":" + ex.Message;
                return true;
            }
        }


        private HashSet<int> CollectStartOverlapColliderIds(Transform moveRoot, Vector3 startBodyCenter)
        {
            HashSet<int> ids = new HashSet<int>();

            try
            {
                Collider[] colliders = Physics.OverlapSphere(startBodyCenter, BodyRadius * 1.05f);
                if (colliders == null)
                {
                    return ids;
                }

                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider col = colliders[i];
                    if (!IsValidBlockingCollider(moveRoot, col))
                    {
                        continue;
                    }

                    try
                    {
                        ids.Add(col.GetInstanceID());
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                reasonlessLog("[" + VersionTag + "] StartOverlap collect failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            return ids;
        }

        private bool IsStartOverlapCollider(Collider col, HashSet<int> startOverlapColliderIds)
        {
            if (col == null || startOverlapColliderIds == null || startOverlapColliderIds.Count <= 0)
            {
                return false;
            }

            try
            {
                return startOverlapColliderIds.Contains(col.GetInstanceID());
            }
            catch
            {
                return false;
            }
        }

        private bool HasGroundBelow(Transform moveRoot, Vector3 bodyCenter, out string reason)
        {
            reason = "GroundOk";

            Vector3 origin = bodyCenter + Vector3.up * GroundProbeUp;
            float distance = GroundProbeUp + GroundProbeDown;

            try
            {
                RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, distance);
                if (hits == null || hits.Length == 0)
                {
                    reason = "RaycastNoHit";
                    return false;
                }

                float nearestDistance = float.MaxValue;
                Collider nearestCollider = null;

                for (int i = 0; i < hits.Length; i++)
                {
                    RaycastHit hit = hits[i];
                    Collider col = hit.collider;
                    if (!IsValidBlockingCollider(moveRoot, col))
                    {
                        continue;
                    }

                    if (hit.distance < nearestDistance)
                    {
                        nearestDistance = hit.distance;
                        nearestCollider = col;
                    }
                }

                if (nearestCollider == null)
                {
                    reason = "OnlySelfOrTriggerHit";
                    return false;
                }

                reason = "Ground=" + SafeName(nearestCollider) + "/d=" + nearestDistance.ToString("0.00");
                return true;
            }
            catch (Exception ex)
            {
                // 床チェックで落ちるよりは、テレポート自体は通す。
                reason = "GroundCheckException=" + ex.GetType().Name + ":" + ex.Message;
                return true;
            }
        }


        private bool LooksLikeFloorBelow(Collider col, Vector3 bodyCenter)
        {
            if (col == null)
            {
                return false;
            }

            try
            {
                Bounds b = col.bounds;
                // 床・低い段差はColliderの上面が体中心よりかなり下にある。
                // 壁や家具は上面が高いのでここでは除外されない。
                return b.max.y <= bodyCenter.y - BodyRadius * 0.35f;
            }
            catch
            {
                return false;
            }
        }

        private bool IsValidBlockingCollider(Transform moveRoot, Collider col)
        {
            if (col == null)
            {
                return false;
            }

            try
            {
                if (!col.enabled || col.isTrigger)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            Transform t = null;
            try
            {
                t = col.transform;
            }
            catch
            {
                t = null;
            }

            if (IsTransformSelfOrChildOf(t, moveRoot))
            {
                return false;
            }

            try
            {
                Rigidbody rb = col.attachedRigidbody;
                if (rb != null && IsTransformSelfOrChildOf(rb.transform, moveRoot))
                {
                    return false;
                }
            }
            catch { }

            // 自作エフェクトの万一の残骸は無視。
            try
            {
                string n = col.name;
                if (!string.IsNullOrEmpty(n) && n.IndexOf("Gojo", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }
            catch { }

            return true;
        }

        private Transform ResolveMoveRoot(Transform cameraTransform)
        {
            if (cameraTransform == null)
            {
                return null;
            }

            // まずは親階層の中から、Player/XR/Rig/VR/Origin/CameraRigっぽいものを探す。
            Transform best = null;
            Transform current = cameraTransform;

            for (int depth = 0; current != null && depth < 10; depth++)
            {
                if (LooksLikePlayerRig(current))
                {
                    best = current;
                }

                // CharacterControllerを持つ階層は、移動Rootの可能性が高い。
                try
                {
                    if (current.GetComponent<CharacterController>() != null)
                    {
                        best = current;
                    }
                }
                catch { }

                if (current.parent == null)
                {
                    break;
                }

                current = current.parent;
            }

            if (best != null)
            {
                return best;
            }

            // フォールバック: Camera直下ではなく、できるだけ親を動かす。
            if (cameraTransform.parent != null)
            {
                return cameraTransform.parent;
            }

            return cameraTransform;
        }

        private bool LooksLikePlayerRig(Transform t)
        {
            if (t == null)
            {
                return false;
            }

            string n = "";
            try
            {
                n = t.name;
            }
            catch
            {
                n = "";
            }

            if (string.IsNullOrEmpty(n))
            {
                return false;
            }

            n = n.ToLowerInvariant();

            return n.Contains("player") ||
                   n.Contains("rig") ||
                   n.Contains("xr") ||
                   n.Contains("origin") ||
                   n.Contains("vr") ||
                   n.Contains("camera rig") ||
                   n.Contains("xrorigin") ||
                   n.Contains("cat");
        }

        private void ApplyRootMove(Transform moveRoot, Vector3 targetRootPosition)
        {
            if (moveRoot == null)
            {
                return;
            }

            CharacterController[] disabledControllers = null;
            List<CharacterController> toRestore = new List<CharacterController>();

            try
            {
                disabledControllers = moveRoot.GetComponentsInChildren<CharacterController>();
                if (disabledControllers != null)
                {
                    for (int i = 0; i < disabledControllers.Length; i++)
                    {
                        CharacterController controller = disabledControllers[i];
                        if (controller != null && controller.enabled)
                        {
                            controller.enabled = false;
                            toRestore.Add(controller);
                        }
                    }
                }
            }
            catch { }

            try
            {
                Rigidbody rb = moveRoot.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    try
                    {
                        rb.velocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                    catch { }

                    try
                    {
                        rb.position = targetRootPosition;
                    }
                    catch
                    {
                        moveRoot.position = targetRootPosition;
                    }
                }
                else
                {
                    moveRoot.position = targetRootPosition;
                }
            }
            catch
            {
                try
                {
                    moveRoot.position = targetRootPosition;
                }
                catch { }
            }

            for (int i = 0; i < toRestore.Count; i++)
            {
                try
                {
                    if (toRestore[i] != null)
                    {
                        toRestore[i].enabled = true;
                    }
                }
                catch { }
            }
        }

        private bool IsTransformSelfOrChildOf(Transform target, Transform root)
        {
            if (target == null || root == null)
            {
                return false;
            }

            Transform current = target;
            for (int depth = 0; current != null && depth < 16; depth++)
            {
                if (current == root)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private void LogThrottled(string message)
        {
            if (Time.time >= _nextLogTime)
            {
                _nextLogTime = Time.time + 0.8f;
                MelonLogger.Msg(message);
            }
        }

        private void reasonlessLog(string message)
        {
            if (Time.time >= _nextLogTime)
            {
                _nextLogTime = Time.time + 1.0f;
                MelonLogger.Warning(message);
            }
        }

        private static string SafeName(UnityEngine.Object obj)
        {
            try
            {
                return obj == null ? "null" : obj.name;
            }
            catch
            {
                return "unknown";
            }
        }

        private static string SafeName(Transform t)
        {
            try
            {
                return t == null ? "null" : t.name;
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
    }
}
