using System;
using MelonLoader;

[assembly: MelonInfo(typeof(IAmCatGojoMod.GojoModMain), "I Am Cat Gojo Mod", "1.0.0", "VR-CATMAN")]
[assembly: MelonGame("New Folder Games", "I Am Cat")]

namespace IAmCatGojoMod
{
    /// <summary>
    /// MelonLoader に認識させるための MOD 入口クラス。
    ///
    /// TeleportGrip_v1:
    /// - 既存の五条能力を維持
    /// - 右グリップ押下で視線方向へ瞬間移動
    /// </summary>
    public sealed class GojoModMain : MelonMod
    {
        private GojoAbilityManager _abilityManager;

        public override void OnInitializeMelon()
        {
            try
            {
                MelonLogger.Msg("[GojoModMain/TeleportGrip_v1] I Am Cat Gojo Mod initializing...");

                _abilityManager = new GojoAbilityManager();
                _abilityManager.Initialize();

                MelonLogger.Msg("[GojoModMain/TeleportGrip_v1] I Am Cat Gojo Mod initialized.");
                MelonLogger.Msg("[GojoModMain/TeleportGrip_v1] Controls: B short press = cycle ability, B long press = wheel menu, Right Trigger = activate, Right Grip = teleport, A long press = Domain Expansion, Left Trigger = cancel.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[GojoModMain/TeleportGrip_v1] Initialize failed.");
                MelonLogger.Error(ex.ToString());
            }
        }

        public override void OnUpdate()
        {
            try
            {
                _abilityManager?.Update();
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[GojoModMain/TeleportGrip_v1] Update failed.");
                MelonLogger.Error(ex.ToString());
            }
        }
    }
}
