using System;
using MelonLoader;
using UnityEngine;

namespace IAmCatGojoMod
{
    /// <summary>
    /// プレイヤー位置取得用。
    ///
    /// MVPでは Camera.main を基準にする。
    /// 無下限バリアの中心は頭そのものではなく、少し下げて猫の体中心っぽくする。
    /// </summary>
    public sealed class PlayerRigFinder
    {
        private Camera _mainCamera;
        private Transform _cameraTransform;

        private float _nextSearchTime;
        private float _nextLogTime;

        // 頭中心だとバリアが高すぎるので、少し下げる。
        private static readonly Vector3 BarrierCenterOffset = new Vector3(0f, -0.35f, 0f);

        public bool TryGetBarrierCenter(out Vector3 center)
        {
            center = Vector3.zero;

            if (!TryGetCameraTransform(out Transform cam))
            {
                return false;
            }

            center = cam.position + BarrierCenterOffset;
            return true;
        }

        public bool TryGetCameraTransform(out Transform cameraTransform)
        {
            cameraTransform = null;

            if (_cameraTransform != null)
            {
                cameraTransform = _cameraTransform;
                return true;
            }

            // 毎フレームCamera.main探索すると重いので間引く。
            if (Time.time < _nextSearchTime)
            {
                return false;
            }

            _nextSearchTime = Time.time + 0.5f;

            try
            {
                _mainCamera = Camera.main;

                if (_mainCamera != null)
                {
                    _cameraTransform = _mainCamera.transform;
                    cameraTransform = _cameraTransform;

                    MelonLogger.Msg("[PlayerRigFinder] Camera.main found: " + SafeName(_cameraTransform));
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogThrottled("[PlayerRigFinder] Camera.main search failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            LogThrottled("[PlayerRigFinder] Waiting for Camera.main...");
            return false;
        }

        private void LogThrottled(string message)
        {
            if (Time.time >= _nextLogTime)
            {
                _nextLogTime = Time.time + 2.0f;
                MelonLogger.Msg(message);
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
    }
}
