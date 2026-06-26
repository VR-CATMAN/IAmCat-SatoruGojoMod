using System;
using MelonLoader;
using UnityEngine;

namespace IAmCatGojoMod
{
    public enum GojoAbilityType
    {
        Infinity = 0,
        Blue = 1,
        Red = 2,
        Purple = 3,
        DomainExpansion = 4
    }

    /// <summary>
    /// 五条MODの能力管理。
    ///
    /// DomainIntegrated_v9_WheelMenu:
    /// - B短押し能力切り替えは維持
    /// - B長押しでVR内ホイールメニューを表示
    /// - B長押し中、右コントローラーの向き/手の位置で能力選択
    /// - Bを離した瞬間にホイール選択を確定
    /// - 右トリガーで現在能力発動
    /// - A長押しで、どの能力選択中でも領域展開
    /// - 左トリガーで現在発動中の能力をキャンセル。ホイール中ならホイールも閉じる
    /// </summary>
    public sealed class GojoAbilityManager
    {
        private const string VersionTag = "DomainIntegrated_v10_TeleportGrip";

        private enum ManagerState
        {
            WaitingForXR,
            Ready,
            Disabled
        }

        private ManagerState _state = ManagerState.WaitingForXR;
        private readonly VrInput _input = new VrInput();

        private PlayerRigFinder _playerRigFinder;
        private InfinityAbility _infinityAbility;
        private BlueAbility _blueAbility;
        private RedAbility _redAbility;
        private PurpleAbility _purpleAbility;
        private DomainExpansionAbility _domainAbility;
        private TeleportAbility _teleportAbility;
        private GojoAbilityWheelMenu _wheelMenu;

        private GojoAbilityType _currentAbility = GojoAbilityType.Infinity;

        private bool _initialized;
        private int _frameCount;

        private bool _bHoldActive;
        private float _bHoldStartTime;
        private bool _wheelMenuOpen;

        private const float WheelHoldSeconds = 0.45f;
        private const float DomainHoldSeconds = 1.00f;

        private bool _aHoldActive;
        private float _aHoldStartTime;
        private bool _domainTriggeredByHold;

        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _state = ManagerState.WaitingForXR;
            _currentAbility = GojoAbilityType.Infinity;

            _playerRigFinder = new PlayerRigFinder();

            _infinityAbility = new InfinityAbility(_playerRigFinder);
            _infinityAbility.Initialize();

            _blueAbility = new BlueAbility(_playerRigFinder, _input);
            _blueAbility.Initialize();

            _redAbility = new RedAbility(_playerRigFinder, _input);
            _redAbility.Initialize();

            _purpleAbility = new PurpleAbility(_playerRigFinder, _input);
            _purpleAbility.Initialize();

            _domainAbility = new DomainExpansionAbility(_playerRigFinder, _input);
            _domainAbility.Initialize();

            _teleportAbility = new TeleportAbility(_playerRigFinder);
            _teleportAbility.Initialize();

            _wheelMenu = new GojoAbilityWheelMenu();
            _wheelMenu.Initialize();

            MelonLogger.Msg("[GojoAbilityManager/" + VersionTag + "] Initialized.");
            MelonLogger.Msg("[GojoAbilityManager/" + VersionTag + "] State = WaitingForXR");
            MelonLogger.Msg("[GojoAbilityManager/" + VersionTag + "] Current ability: " + _currentAbility);
        }

        public void Update()
        {
            if (!_initialized || _state == ManagerState.Disabled)
            {
                return;
            }

            _frameCount++;

            switch (_state)
            {
                case ManagerState.WaitingForXR:
                    UpdateWaitingForXR();
                    break;

                case ManagerState.Ready:
                    UpdateReady();
                    break;
            }
        }

        private void UpdateWaitingForXR()
        {
            if (!_input.TryInitialize())
            {
                if (_frameCount % 300 == 0)
                {
                    MelonLogger.Msg("[GojoAbilityManager/" + VersionTag + "] Waiting for XR devices... " + _input.LastStatus);
                }

                return;
            }

            _state = ManagerState.Ready;

            MelonLogger.Msg("[GojoAbilityManager/" + VersionTag + "] State = Ready");
            MelonLogger.Msg("[GojoAbilityManager/" + VersionTag + "] XR input ready. " + _input.LastStatus);
            MelonLogger.Msg("[GojoAbilityManager/" + VersionTag + "] Controls: B short press = cycle ability, B hold = wheel menu, Right Trigger = activate, Right Grip = teleport, A hold = Domain Expansion, Left Trigger = cancel.");
        }

        private void UpdateReady()
        {
            if (!_input.Update())
            {
                _state = ManagerState.WaitingForXR;
                CloseWheelMenu(false);
                _bHoldActive = false;
                _aHoldActive = false;
                _domainTriggeredByHold = false;

                CancelAllAbilities();

                MelonLogger.Warning("[GojoAbilityManager/" + VersionTag + "] XR input lost. Back to WaitingForXR. " + _input.LastStatus);
                return;
            }

            HandleBButton();
            HandleLeftTrigger();

            // ホイールを開いている間は誤爆防止で、右トリガー/A長押し/グリップ瞬間移動は見ない。
            if (!_wheelMenuOpen)
            {
                HandleTeleportGrip();
                HandleRightTrigger();
                HandleAButton();
            }

            _infinityAbility?.Update();
            _blueAbility?.Update();
            _redAbility?.Update();
            _purpleAbility?.Update();
            _domainAbility?.Update();
            _teleportAbility?.Update();
        }

        private void HandleBButton()
        {
            if (_input.RightSecondaryButtonDown)
            {
                _bHoldActive = true;
                _bHoldStartTime = Time.time;
                CloseWheelMenu(false);
            }

            if (_bHoldActive && _input.RightSecondaryButtonHeld)
            {
                float heldSeconds = Time.time - _bHoldStartTime;

                if (!_wheelMenuOpen && heldSeconds >= WheelHoldSeconds)
                {
                    OpenWheelMenu();
                }

                if (_wheelMenuOpen)
                {
                    _wheelMenu?.UpdateMenu(_currentAbility);
                }
            }

            if (_bHoldActive && _input.RightSecondaryButtonUp)
            {
                float heldSeconds = Time.time - _bHoldStartTime;

                if (_wheelMenuOpen || heldSeconds >= WheelHoldSeconds)
                {
                    GojoAbilityType selected = _wheelMenu != null ? _wheelMenu.SelectedAbility : _currentAbility;
                    bool directional = _wheelMenu != null && _wheelMenu.HasDirectionalSelection;

                    CloseWheelMenu(false);
                    SetCurrentAbility(selected, directional ? "WheelMenu" : "WheelMenuNoDirection");
                }
                else
                {
                    CycleAbility();
                }

                _bHoldActive = false;
            }
        }

        private void OpenWheelMenu()
        {
            _wheelMenuOpen = true;
            _wheelMenu?.Show(_currentAbility);
            MelonLogger.Msg("[GojoAbilityManager/" + VersionTag + "] Wheel menu open. Current ability: " + _currentAbility);
        }

        private void CloseWheelMenu(bool log)
        {
            if (_wheelMenuOpen || (_wheelMenu != null && _wheelMenu.IsVisible))
            {
                if (log)
                {
                    MelonLogger.Msg("[GojoAbilityManager/" + VersionTag + "] Wheel menu close.");
                }
            }

            _wheelMenuOpen = false;
            _wheelMenu?.Hide();
        }


        private void HandleTeleportGrip()
        {
            // 選択式能力とは別枠。右グリップを押した瞬間だけ、視線方向へ瞬間移動する。
            // 左グリップも使いたい場合は TeleportAbility.UseLeftGripToo を true にする。
            bool teleportPressed = _input.RightGripDown || (TeleportAbility.UseLeftGripToo && _input.LeftGripDown);

            if (!teleportPressed)
            {
                return;
            }

            MelonLogger.Msg("[GojoAbilityManager/" + VersionTag + "] Teleport grip pressed.");
            _teleportAbility?.TryBlink();
        }

        private void HandleRightTrigger()
        {
            if (!_input.RightTriggerDown)
            {
                return;
            }

            MelonLogger.Msg("[GojoAbilityManager/" + VersionTag + "] Activate ability: " + _currentAbility);

            switch (_currentAbility)
            {
                case GojoAbilityType.Infinity:
                    _blueAbility?.Cancel();
                    _redAbility?.Cancel();
                    _purpleAbility?.Cancel();
                    _domainAbility?.Cancel();
                    _infinityAbility?.Toggle();
                    break;

                case GojoAbilityType.Blue:
                    _infinityAbility?.Disable();
                    _redAbility?.Cancel();
                    _purpleAbility?.Cancel();
                    _domainAbility?.Cancel();
                    _blueAbility?.Cast();
                    break;

                case GojoAbilityType.Red:
                    _infinityAbility?.Disable();
                    _blueAbility?.Cancel();
                    _purpleAbility?.Cancel();
                    _domainAbility?.Cancel();
                    _redAbility?.Cast();
                    break;

                case GojoAbilityType.Purple:
                    _infinityAbility?.Disable();
                    _blueAbility?.Cancel();
                    _redAbility?.Cancel();
                    _domainAbility?.Cancel();
                    _purpleAbility?.Cast();
                    break;

                case GojoAbilityType.DomainExpansion:
                    CastDomainExpansion();
                    break;
            }
        }

        private void HandleLeftTrigger()
        {
            if (!_input.LeftTriggerDown)
            {
                return;
            }

            MelonLogger.Msg("[GojoAbilityManager/" + VersionTag + "] Cancel / 解除");

            CloseWheelMenu(false);
            _bHoldActive = false;
            _aHoldActive = false;
            _domainTriggeredByHold = false;

            CancelAllAbilities();
        }

        private void HandleAButton()
        {
            if (_input.RightPrimaryButtonDown)
            {
                _aHoldActive = true;
                _aHoldStartTime = Time.time;
                _domainTriggeredByHold = false;
            }

            if (_aHoldActive && _input.RightPrimaryButtonHeld)
            {
                float heldSeconds = Time.time - _aHoldStartTime;

                if (!_domainTriggeredByHold && heldSeconds >= DomainHoldSeconds)
                {
                    _domainTriggeredByHold = true;
                    MelonLogger.Msg("[GojoAbilityManager/" + VersionTag + "] Domain Expansion triggered by A hold.");
                    CastDomainExpansion();
                }
            }

            if (_aHoldActive && _input.RightPrimaryButtonUp)
            {
                if (!_domainTriggeredByHold)
                {
                    MelonLogger.Msg("[GojoAbilityManager/" + VersionTag + "] A short press detected. No action assigned yet.");
                }

                _aHoldActive = false;
                _domainTriggeredByHold = false;
            }
        }

        private void CastDomainExpansion()
        {
            _infinityAbility?.Disable();
            _blueAbility?.Cancel();
            _redAbility?.Cancel();
            _purpleAbility?.Cancel();

            _domainAbility?.Cast();
        }

        private void CycleAbility()
        {
            int count = Enum.GetValues(typeof(GojoAbilityType)).Length;
            int next = ((int)_currentAbility + 1) % count;
            SetCurrentAbility((GojoAbilityType)next, "BShortPress");
        }

        private void SetCurrentAbility(GojoAbilityType next, string source)
        {
            GojoAbilityType previous = _currentAbility;

            if (previous == next)
            {
                MelonLogger.Msg("[GojoAbilityManager/" + VersionTag + "] Current ability unchanged: " + _currentAbility + " Source=" + source);
                return;
            }

            CancelPreviousAbilityOnSwitch(previous, next);
            _currentAbility = next;

            MelonLogger.Msg("[GojoAbilityManager/" + VersionTag + "] Current ability: " + _currentAbility + " Source=" + source + " Previous=" + previous);
        }

        private void CancelPreviousAbilityOnSwitch(GojoAbilityType previous, GojoAbilityType next)
        {
            if (previous == GojoAbilityType.Infinity && next != GojoAbilityType.Infinity)
            {
                _infinityAbility?.Disable();
            }

            if (previous == GojoAbilityType.Blue && next != GojoAbilityType.Blue)
            {
                _blueAbility?.Cancel();
            }

            if (previous == GojoAbilityType.Red && next != GojoAbilityType.Red)
            {
                _redAbility?.Cancel();
            }

            if (previous == GojoAbilityType.Purple && next != GojoAbilityType.Purple)
            {
                _purpleAbility?.Cancel();
            }

            if (previous == GojoAbilityType.DomainExpansion && next != GojoAbilityType.DomainExpansion)
            {
                _domainAbility?.Cancel();
            }
        }

        private void CancelAllAbilities()
        {
            _infinityAbility?.Disable();
            _blueAbility?.Cancel();
            _redAbility?.Cancel();
            _purpleAbility?.Cancel();
            _domainAbility?.Cancel();
            _teleportAbility?.Cancel();
        }
    }
}
