using System;
using MelonLoader;
using UnityEngine.XR;

namespace IAmCatGojoMod
{
    /// <summary>
    /// XR入力を安全に扱うためのラッパー。
    ///
    /// VrInput_GripTeleport_v1:
    /// - 既存の A/B/Trigger 入力は維持
    /// - 右/左 Grip 入力を追加
    /// - グリップは gripButton が取れればそれを優先し、取れない環境では grip axis 0.75以上で押下扱い
    /// - Head は必須にしない
    /// - Ready条件は RightHand が取れることだけ
    /// - 毎フレームのデバイス探索はしない
    /// </summary>
    public sealed class VrInput
    {
        private const string VersionTag = "VrInput_GripTeleport_v1";

        private InputDevice _rightHand;
        private InputDevice _leftHand;

        private bool _isReady;

        private int _frameCounter;
        private int _scanAttemptCount;

        // デバイス探索の間隔。
        // 毎フレーム探索で落ちる可能性を下げるため、待機中だけ120フレームに1回試す。
        private const int ScanIntervalFrames = 120;

        private ButtonState _rightPrimary;    // Quest右手 A
        private ButtonState _rightSecondary;  // Quest右手 B
        private ButtonState _rightTrigger;
        private ButtonState _leftTrigger;
        private ButtonState _rightGrip;
        private ButtonState _leftGrip;

        public string LastStatus { get; private set; } = "Not initialized.";

        public bool IsReady => _isReady;

        public bool RightPrimaryButtonDown => _rightPrimary.Down;
        public bool RightPrimaryButtonHeld => _rightPrimary.Held;
        public bool RightPrimaryButtonUp => _rightPrimary.Up;

        public bool RightSecondaryButtonDown => _rightSecondary.Down;
        public bool RightSecondaryButtonHeld => _rightSecondary.Held;
        public bool RightSecondaryButtonUp => _rightSecondary.Up;

        public bool RightTriggerDown => _rightTrigger.Down;
        public bool RightTriggerHeld => _rightTrigger.Held;
        public bool RightTriggerUp => _rightTrigger.Up;

        public bool LeftTriggerDown => _leftTrigger.Down;
        public bool LeftTriggerHeld => _leftTrigger.Held;
        public bool LeftTriggerUp => _leftTrigger.Up;

        public bool RightGripDown => _rightGrip.Down;
        public bool RightGripHeld => _rightGrip.Held;
        public bool RightGripUp => _rightGrip.Up;

        public bool LeftGripDown => _leftGrip.Down;
        public bool LeftGripHeld => _leftGrip.Held;
        public bool LeftGripUp => _leftGrip.Up;

        public bool TryInitialize()
        {
            if (_isReady && IsRightHandValid())
            {
                return true;
            }

            _isReady = false;
            _frameCounter++;

            // 起動直後の1回目はすぐ試す。
            // 2回目以降は間引く。
            bool shouldScan = _scanAttemptCount == 0 || (_frameCounter % ScanIntervalFrames) == 0;

            if (!shouldScan)
            {
                LastStatus =
                    "Waiting for scan interval. " +
                    "Attempt=" + _scanAttemptCount +
                    ", RightHand=" + SafeDeviceName(_rightHand) +
                    ", LeftHand=" + SafeDeviceName(_leftHand);
                return false;
            }

            TryScanDevices();

            if (IsRightHandValid())
            {
                _isReady = true;
                LastStatus =
                    "Ready. " +
                    "RightHand=" + SafeDeviceName(_rightHand) +
                    ", LeftHand=" + SafeDeviceName(_leftHand);

                return true;
            }

            LastStatus =
                "Waiting for RightHand device. " +
                "Attempt=" + _scanAttemptCount +
                ", RightHand=" + SafeDeviceName(_rightHand) +
                ", LeftHand=" + SafeDeviceName(_leftHand);

            return false;
        }

        public bool Update()
        {
            if (!_isReady)
            {
                return false;
            }

            if (!IsRightHandValid())
            {
                _isReady = false;
                LastStatus = "RightHand became invalid.";
                ResetButtonStates();
                return false;
            }

            try
            {
                bool rightPrimaryHeld = ReadBoolFeature(_rightHand, CommonUsages.primaryButton);
                bool rightSecondaryHeld = ReadBoolFeature(_rightHand, CommonUsages.secondaryButton);
                bool rightTriggerHeld = ReadTrigger(_rightHand);
                bool rightGripHeld = ReadGrip(_rightHand);

                bool leftTriggerHeld = false;
                bool leftGripHeld = false;
                if (IsLeftHandValid())
                {
                    leftTriggerHeld = ReadTrigger(_leftHand);
                    leftGripHeld = ReadGrip(_leftHand);
                }
                else
                {
                    // 左手は必須ではないので、Ready後にたまに再取得を試す。
                    _frameCounter++;
                    if ((_frameCounter % ScanIntervalFrames) == 0)
                    {
                        TryScanLeftHandOnly();
                    }
                }

                _rightPrimary.Update(rightPrimaryHeld);
                _rightSecondary.Update(rightSecondaryHeld);
                _rightTrigger.Update(rightTriggerHeld);
                _leftTrigger.Update(leftTriggerHeld);
                _rightGrip.Update(rightGripHeld);
                _leftGrip.Update(leftGripHeld);

                return true;
            }
            catch (Exception ex)
            {
                _isReady = false;
                LastStatus = "Update exception: " + ex.GetType().Name + ": " + ex.Message;
                MelonLogger.Warning("[" + VersionTag + "] " + LastStatus);
                ResetButtonStates();
                return false;
            }
        }

        private void TryScanDevices()
        {
            _scanAttemptCount++;

            try
            {
                InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                InputDevice left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

                if (right.isValid)
                {
                    _rightHand = right;
                }

                if (left.isValid)
                {
                    _leftHand = left;
                }

                LastStatus =
                    "Scan done. " +
                    "Attempt=" + _scanAttemptCount +
                    ", RightHand=" + SafeDeviceName(_rightHand) +
                    ", LeftHand=" + SafeDeviceName(_leftHand);

                MelonLogger.Msg("[" + VersionTag + "] " + LastStatus);
            }
            catch (Exception ex)
            {
                LastStatus = "Scan exception: " + ex.GetType().Name + ": " + ex.Message;
                MelonLogger.Warning("[" + VersionTag + "] " + LastStatus);
            }
        }

        private void TryScanLeftHandOnly()
        {
            try
            {
                InputDevice left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

                if (left.isValid)
                {
                    _leftHand = left;
                    MelonLogger.Msg("[" + VersionTag + "] LeftHand found: " + SafeDeviceName(_leftHand));
                }
            }
            catch (Exception ex)
            {
                LastStatus = "LeftHand scan exception: " + ex.GetType().Name + ": " + ex.Message;
                MelonLogger.Warning("[" + VersionTag + "] " + LastStatus);
            }
        }

        private bool IsRightHandValid()
        {
            try
            {
                return _rightHand.isValid;
            }
            catch
            {
                return false;
            }
        }

        private bool IsLeftHandValid()
        {
            try
            {
                return _leftHand.isValid;
            }
            catch
            {
                return false;
            }
        }

        private static bool ReadBoolFeature(InputDevice device, InputFeatureUsage<bool> usage)
        {
            try
            {
                bool value;

                if (device.isValid && device.TryGetFeatureValue(usage, out value))
                {
                    return value;
                }
            }
            catch
            {
                // 読めなければ押されていない扱い。
            }

            return false;
        }

        private static bool ReadTrigger(InputDevice device)
        {
            try
            {
                if (!device.isValid)
                {
                    return false;
                }

                bool button;
                if (device.TryGetFeatureValue(CommonUsages.triggerButton, out button) && button)
                {
                    return true;
                }

                float triggerValue;
                if (device.TryGetFeatureValue(CommonUsages.trigger, out triggerValue))
                {
                    return triggerValue >= 0.75f;
                }
            }
            catch
            {
                // 読めなければ押されていない扱い。
            }

            return false;
        }

        private static bool ReadGrip(InputDevice device)
        {
            try
            {
                if (!device.isValid)
                {
                    return false;
                }

                bool button;
                if (device.TryGetFeatureValue(CommonUsages.gripButton, out button) && button)
                {
                    return true;
                }

                float gripValue;
                if (device.TryGetFeatureValue(CommonUsages.grip, out gripValue))
                {
                    return gripValue >= 0.75f;
                }
            }
            catch
            {
                // 読めなければ押されていない扱い。
            }

            return false;
        }

        private static string SafeDeviceName(InputDevice device)
        {
            try
            {
                if (!device.isValid)
                {
                    return "Invalid";
                }

                string name = device.name;
                return string.IsNullOrEmpty(name) ? "Unnamed" : name;
            }
            catch
            {
                return "Unknown";
            }
        }

        private void ResetButtonStates()
        {
            _rightPrimary.Reset();
            _rightSecondary.Reset();
            _rightTrigger.Reset();
            _leftTrigger.Reset();
            _rightGrip.Reset();
            _leftGrip.Reset();
        }

        private struct ButtonState
        {
            private bool _previous;
            private bool _current;

            public bool Down => _current && !_previous;
            public bool Held => _current;
            public bool Up => !_current && _previous;

            public void Update(bool current)
            {
                _previous = _current;
                _current = current;
            }

            public void Reset()
            {
                _previous = false;
                _current = false;
            }
        }
    }
}
