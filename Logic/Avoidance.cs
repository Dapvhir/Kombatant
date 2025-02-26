//!CompilerOption:Optimize:On
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using ff14bot.Behavior;
using ff14bot.Managers;
using Kombatant.Extensions;
using Kombatant.Helpers;
using Kombatant.Interfaces;
using BotBase = Kombatant.Settings.BotBase;

namespace Kombatant.Logic
{
    /// <summary>
    /// Logic for avoidance support.
    /// </summary>
    /// <inheritdoc cref="M:Kombatant.Interfaces.LogicExecutor"/>
    internal class Avoidance : LogicExecutor
    {
        #region Singleton

        private static Avoidance _avoidance;
        internal static Avoidance Instance => _avoidance ?? (_avoidance = new Avoidance());

        #endregion

        private bool _wasAvoiding;
        private bool _isFighting;
        private static PluginContainer sidestep = PluginManager.Plugins.FirstOrDefault(i => i.Plugin.Name == "SideStep");
        /// <summary>
        /// Main task executor for the Avoidance logic.
        /// </summary>
        /// <returns>Returns <c>true</c> if any action was executed, otherwise <c>false</c>.</returns>
        internal new async Task<bool> ExecuteLogic()
        {
            if (sidestep != null && BotBase.Instance.EnableAvoidance != sidestep.Enabled)
            {
                sidestep.Enabled = BotBase.Instance.EnableAvoidance;
            }

            if (ShouldExecuteAvoidance())
            {
                if (BotBase.Instance.IsPaused)
                {
                    return await Task.FromResult(false);
                }

                if(PulseFlagHelper.Instance.EnablePulseFlag(PulseFlags.Avoidance))
                    return await Task.FromResult(false);

                //if ( || AvoidanceManager.IsRunningOutOfAvoid && !AvoidanceManager.Avoids.Any(i => i.IsPointInAvoid(ff14bot.Core.Me.Location)))
                //{
                //    LogHelper.Instance.Log("Forcing Stop");
                //    MovementManager.MoveStop(); 
                //    return await Task.FromResult(true);

                //}

                if (AvoidanceManager.IsRunningOutOfAvoid)
                {
                    _wasAvoiding = true;
                    return await Task.FromResult(true);
                }

                // For non-autonomous operation, we really want the bot to not move more than it needs to.
                // Since we know whether or not we just came out of a avoidance loop, we can stop the movement
                // here to prevent unwanted chicken runs.
                if (!_wasAvoiding) return await Task.FromResult(false);
                _wasAvoiding = false;
                LogHelper.Instance.Log(Localization.Localization.Msg_AvoidanceFinished);
                MovementManager.MoveStop();
                return await Task.FromResult(true);
            }
            else
            {
                if (PulseFlagHelper.Instance.DisablePulseFlag(PulseFlags.Avoidance))
                    return await Task.FromResult(false);
            }

            return await Task.FromResult(false);
        }

        /// <summary>
        /// Determines whether or not avoidance informations should be honoured.
        /// </summary>
        /// <returns></returns>
        private bool ShouldExecuteAvoidance()
        {
            if (BotBase.Instance.IsPaused)
                return false;

            // User doesn't want RebornBuddy to avoid.
            if (!BotBase.Instance.EnableAvoidance)
                return false;

            // Pause avoidance because player is fighting a known boss?
            if (PauseAvoidanceBecauseBoss())
                return false;

            return true;
        }

        /// <summary>
        /// Determines whether or not the user wants to disable avoidance during bossfights.
        /// </summary>
        /// <returns></returns>
        private bool PauseAvoidanceBecauseBoss()
        {
            if (BotBase.Instance.PauseAvoidanceOnBosses &&
                GameObjectManager.Attackers.Any(attacker => attacker.IsBoss()))
            {
                // Fight started
                if (!_isFighting)
                {
                    var bossMonster = GameObjectManager.Attackers.FirstOrDefault(attacker => attacker.IsBoss());
                    var toastMsg = string.Format(Localization.Localization.Msg_AvoidanceDisabledOnBossStart, bossMonster?.Name);
                    LogHelper.Instance.Log(Localization.Localization.Msg_Log_AvoidanceDisabledOnBossStart);
                    OverlayHelper.Instance.AddToast(toastMsg, Colors.Coral, Colors.Chocolate, new TimeSpan(0, 0, 0, 5));
                }

                _isFighting = true;
                return true;
            }

            // Fight ended
            if (_isFighting)
            {
                LogHelper.Instance.Log(Localization.Localization.Msg_Log_AvoidanceDisabledOnBossEnd);
                OverlayHelper.Instance.AddToast(Localization.Localization.Msg_AvoidanceDisabledOnBossEnd, Colors.LimeGreen, Colors.DarkGreen, new TimeSpan(0, 0, 0, 5));
                _isFighting = false;
            }

            return false;
        }
    }
}
