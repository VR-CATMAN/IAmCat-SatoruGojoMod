using System;
using MelonLoader;
using UnityEngine;

namespace IAmCatGojoMod
{
    /// <summary>
    /// 無下限呪術 / Infinity
    ///
    /// InfinityAbility_v7_EffectIntegrated:
    /// - v2ベースのRigidbody全速度減衰
    /// - 投げ物・物理オブジェクト・倒れた敵に効く
    /// - 猫の周囲に薄い青い透明球
    /// - バリア境界に青白いリング
    /// - 止めた/減速した物体の周囲に小さい粒子
    ///
    /// NPCについて:
    /// - 通常歩行NPCはAnimator/AI制御が強く、無下限のRigidbody減衰では止まらないことがある
    /// - これは動画上「黒閃持ちは無下限を貫通する」扱いにしやすい
    /// </summary>
    public sealed class InfinityAbility
    {
        private const string VersionTag = "InfinityAbility_v7_EffectIntegrated";

        private readonly PlayerRigFinder _playerRigFinder;
        private readonly InfinityBarrierEffect _barrierEffect = new InfinityBarrierEffect();

        private bool _enabled;

        // Rigidbody用バリア。
        private const float OuterRadius = 2.0f;
        private const float StopRadius = 0.45f;
        private const float CoreRadius = 0.25f;

        private const float MaxDamping = 18.0f;
        private const float CorePushForce = 5.0f;
        private const float MaxTargetMass = 300.0f;

        // 粒子を出しすぎないため、実際に粒子化する対象数を制限。
        private const int MaxSparkBurstsPerFrame = 5;

        private float _nextStatusLogTime;

        public bool IsEnabled => _enabled;

        public InfinityAbility(PlayerRigFinder playerRigFinder)
        {
            _playerRigFinder = playerRigFinder;
        }

        public void Initialize()
        {
            MelonLogger.Msg("[" + VersionTag + "] Initialized.");
        }

        public void Toggle()
        {
            if (_enabled)
            {
                Disable();
            }
            else
            {
                Enable();
            }
        }

        public void Enable()
        {
            if (_enabled)
            {
                return;
            }

            _enabled = true;
            _nextStatusLogTime = 0f;

            if (_playerRigFinder != null && _playerRigFinder.TryGetBarrierCenter(out Vector3 center))
            {
                _barrierEffect.Show(center, OuterRadius);
            }

            MelonLogger.Msg("[" + VersionTag + "] ON / 無下限呪術 起動");
        }

        public void Disable()
        {
            if (!_enabled)
            {
                return;
            }

            _enabled = false;
            _barrierEffect.Hide();

            MelonLogger.Msg("[" + VersionTag + "] OFF / 無下限呪術 解除");
        }

        public void Update()
        {
            if (!_enabled)
            {
                _barrierEffect.Update(Vector3.zero, OuterRadius, 0);
                return;
            }

            if (_playerRigFinder == null || !_playerRigFinder.TryGetBarrierCenter(out Vector3 center))
            {
                LogStatus("[" + VersionTag + "] Waiting for player center...");
                return;
            }

            if (!_barrierEffect.IsVisible)
            {
                _barrierEffect.Show(center, OuterRadius);
            }

            InfinityStats stats = ApplyInfinityField(center);

            _barrierEffect.Update(center, OuterRadius, stats.DampedCount);

            if (Time.time >= _nextStatusLogTime)
            {
                _nextStatusLogTime = Time.time + 1.0f;
                MelonLogger.Msg(
                    "[" + VersionTag + "] Active. " +
                    "Targets=" + stats.TargetCount +
                    ", Damped=" + stats.DampedCount +
                    ", CorePushed=" + stats.CorePushedCount +
                    ", SparkBursts=" + stats.SparkBurstCount
                );
            }
        }

        private InfinityStats ApplyInfinityField(Vector3 center)
        {
            InfinityStats stats = new InfinityStats();

            Collider[] hits;

            try
            {
                hits = Physics.OverlapSphere(center, OuterRadius);
            }
            catch (Exception ex)
            {
                LogStatus("[" + VersionTag + "] OverlapSphere failed: " + ex.GetType().Name + ": " + ex.Message);
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

                if (!IsValidTarget(rb, hit))
                {
                    continue;
                }

                try
                {
                    ApplyDampingToRigidbody(rb, center, ref stats);
                }
                catch (Exception ex)
                {
                    LogStatus("[" + VersionTag + "] Apply failed: " + SafeName(rb) + " / " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            return stats;
        }

        private void ApplyDampingToRigidbody(Rigidbody rb, Vector3 center, ref InfinityStats stats)
        {
            Vector3 rbPos = rb.worldCenterOfMass;
            Vector3 fromCenter = rbPos - center;
            float distance = fromCenter.magnitude;

            if (distance > OuterRadius)
            {
                return;
            }

            stats.TargetCount++;

            float intensity = Mathf.InverseLerp(OuterRadius, StopRadius, distance);
            intensity = Mathf.Clamp01(intensity);
            intensity = intensity * intensity;

            float beforeSpeed = rb.velocity.magnitude;

            float damping = Mathf.Lerp(0.0f, MaxDamping, intensity);
            float factor = Mathf.Exp(-damping * Time.deltaTime);

            rb.velocity *= factor;
            rb.angularVelocity *= factor;

            float afterSpeed = rb.velocity.magnitude;

            stats.DampedCount++;

            // ある程度速度が落ちた対象だけ粒子を出す。
            // 静止物全部に出すと重くなるため、フレーム上限あり。
            if (stats.SparkBurstCount < MaxSparkBurstsPerFrame && beforeSpeed > 0.05f && afterSpeed < beforeSpeed * 0.92f)
            {
                _barrierEffect.BurstAt(rbPos);
                stats.SparkBurstCount++;
            }

            if (distance < CoreRadius && distance > 0.001f)
            {
                Vector3 pushDir = fromCenter.normalized;
                rb.AddForce(pushDir * CorePushForce, ForceMode.Acceleration);
                stats.CorePushedCount++;
            }
        }

        private bool IsValidTarget(Rigidbody rb, Collider hit)
        {
            if (rb == null)
            {
                return false;
            }

            try
            {
                if (!rb.gameObject.activeInHierarchy)
                {
                    return false;
                }

                // 通常歩行NPCなどのkinematicはvelocity変更が効きにくいので除外。
                // 投げ物/小物/ラグドール化した敵を主対象にする。
                if (rb.isKinematic)
                {
                    return false;
                }

                if (rb.mass > MaxTargetMass)
                {
                    return false;
                }

                Transform t = rb.transform;
                if (IsExcludedTransform(t))
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

        private bool IsExcludedTransform(Transform t)
        {
            if (t == null)
            {
                return true;
            }

            Transform current = t;

            for (int depth = 0; depth < 10 && current != null; depth++)
            {
                string n = current.name;
                if (string.IsNullOrEmpty(n))
                {
                    current = current.parent;
                    continue;
                }

                string lower = n.ToLowerInvariant();

                // プレイヤー/VRリグ/UI/システム系を除外。
                if (lower.Contains("player") ||
                    lower.Contains("xr") ||
                    lower.Contains("camera") ||
                    lower.Contains("main camera") ||
                    lower.Contains("hmd") ||
                    lower.Contains("controller") ||
                    lower.Contains("hand") ||
                    lower.Contains("rig") ||
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

        private void LogStatus(string message)
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

        private struct InfinityStats
        {
            public int TargetCount;
            public int DampedCount;
            public int CorePushedCount;
            public int SparkBurstCount;
        }
    }
}
