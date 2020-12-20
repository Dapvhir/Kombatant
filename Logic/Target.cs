﻿//#define DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xaml;
using Buddy.Coroutines;
using Clio.Utilities;
using ff14bot;
using ff14bot.AClasses;
using ff14bot.Enums;
using ff14bot.Managers;
using ff14bot.Objects;
using Kombatant.Enums;
using Kombatant.Extensions;
using Kombatant.Forms;
using Kombatant.Helpers;
using Kombatant.Interfaces;
using BotBase = Kombatant.Settings.BotBase;

namespace Kombatant.Logic
{
	/// <summary>
	/// Logic for automatically targeting enemies.
	/// </summary>
	/// <inheritdoc cref="M:Komabatant.Interfaces.LogicExecutor"/>
	internal class Target : LogicExecutor
	{
		#region Singleton

		private static Target _target;
		internal static Target Instance => _target ?? (_target = new Target());

		#endregion

		/// <summary>
		/// Main task executor for the targeting logic.
		/// </summary>
		/// <returns>Returns <c>true</c> if any action was executed, otherwise <c>false</c>.</returns>
		internal new async Task<bool> ExecuteLogic()
		{
			// Do not execute this logic if the botbase is paused.
			if (Settings.BotBase.Instance.IsPaused || IsTraveling())
				return await Task.FromResult(false);

			// Auto face target
			if (ShouldExecuteAutoFaceTarget())
			{
				GameSettingsManager.FaceTargetOnAction = Settings.BotBase.Instance.AutoFaceTarget;
				return await Task.FromResult(true);
			}

			// Try to determine current target. Possibly a target chosen by the prior mechanisms.
			var currentTarget = Core.Me.CurrentTarget as BattleCharacter;
			var potentialTarget = null as BattleCharacter;

#if DEBUG
			using (new PerformanceLogger("AutoDeseletTarget"))
#endif
			{
				if (BotBase.Instance.AutoTarget && BotBase.Instance.AutoDeSelectTarget && currentTarget != null && !IsValidTarget(currentTarget) && !currentTarget.IsStrikingDummy())
				{
					Core.Me.ClearTarget();
					await Coroutine.Yield();
				}
			}

			// Automatically select a target if we do not have one. Uses one of the many colourful selection modes!
			if (ShouldExecuteAutoTarget())
			{
#if DEBUG
				using (new PerformanceLogger("ChoosePotentialTarget"))
#endif
				{
					switch (BotBase.Instance.AutoTargetingMode)
					{
						//case TargetingMode.None:
						//	break;

						case TargetingMode.BestAoE:
							potentialTarget = TargetBestAoeEnemy();
							break;

						case TargetingMode.OnlyWhitelisted:
							potentialTarget = TargetOnlyWhitelistedEnemy();
							break;

						case TargetingMode.Nearest:
							potentialTarget = TargetNearestEnemy();
							break;

						case TargetingMode.LowestHealth:
							potentialTarget = TargetLowestHpEnemy();
							break;

						case TargetingMode.LowestHealthPercent:
							potentialTarget = TargetLowestHpPercentEnemy();
							break;

						case TargetingMode.HighestHealth:
							potentialTarget = TargetHighestHpEnemy();
							break;

						case TargetingMode.HighestHealthPercent:
							potentialTarget = TargetHighestHpPercentEnemy();
							break;

						case TargetingMode.AssistTank:
							potentialTarget = TargetAssistTank();
							break;

						case TargetingMode.AssistLeader:
							potentialTarget = TargetAssistLeader();
							break;

						case TargetingMode.AssistFixedCharacter:
							potentialTarget = TargetAssistFixedCharacter();
							break;

						case TargetingMode.AssistHighestLvl:
							potentialTarget = TargetAssistHighestLvlCharacter();
							break;

						case TargetingMode.MostTargeted:
							potentialTarget = TargetMostTargetedEnemy();
							break;
					}

#if DEBUG
					using (new PerformanceLogger("CheckAndTargetPotentialTarget"))
#endif
					{

						// Player target differs from chosen target or is a FATE mob?
						if (potentialTarget != null && potentialTarget.CheckAliveAndValid() && currentTarget != potentialTarget)
						{
							LogHelper.Instance.Log(Localization.Localization.Msg_NewTargetSelected,
								potentialTarget.Name,
								$@"0x{potentialTarget.ObjectId:X8}");

							potentialTarget.Target();
							//if (Settings.BotBase.Instance.MarkTarget)
							//{
							//	ChatManager.SendChat(@"/marking bind1 <t>");
							//}
							return await Task.FromResult(true);
						}
					}
				}
			}

			// No target? Then the following checks would return false anyway...
			if (currentTarget == null)
				return await Task.FromResult(false);

			// Target not in line of sight?
			if (!currentTarget.InLineOfSight())
				return await Task.FromResult(true);

			// Target is invincible?
			if (currentTarget.IsInvincible())
				return await Task.FromResult(true);

			//// Make sure we don't pull stuff as a non-tank when in a party and smart pull is enabled.
			//if (!IsAllowedToFight(currentTarget))
			//    return await Task.FromResult(true);

			return await Task.FromResult(false);
		}

		/// <summary>
		/// Determines whether the combat routine is allowed to pull/do combat with the current target.
		/// </summary>
		/// <param name="target"></param>
		/// <returns></returns>
		private bool IsAllowedToFight(BattleCharacter target)
		{
			// Only limit when we have Smart Pull enabled, are not a tank ourselves and there is a tank nearby!
			if (Settings.BotBase.Instance.EnableSmartPull /*&& !Core.Me.IsTank() && PartyManager.VisibleMembers.Any(member => member.IsTank())*/)
				return /*Core.Me.IsInMyParty() && */target.IsEnemy() && (GameObjectManager.Attackers.Contains(target) || target.HasBeenTaggedByPartyMember());

			return true;
		}

		private static bool IsValidTarget(GameObject o)
		{
			if (!(o is BattleCharacter t)) return false;
			if (!t.IsValid || !t.IsVisible || !t.IsAlive || !t.IsTargetable || !t.CanAttack || t.IsStrikingDummy() || t.IsMe) return false;
			if (t.CombatDistance() > BotBase.Instance.TargetScanMaxDistance) return false;
			//if (t.CombatDistance() > (BotBase.Instance.TargetScanMaxDistance == 0 ? RoutineManager.Current.PullRange : BotBase.Instance.TargetScanMaxDistance)) return false;
			if (t.IsInvincible()) return false;
			if (BotBase.Instance.EnableLosCheck && !t.InLineOfSight()) return false;

			return true;
		}
		/// <summary>
		/// Helper method to determine whether a given GameObject is a valid target
		/// for the target strategy "Whitelist only".
		/// </summary>
		/// <param name="obj"></param>
		/// <returns></returns>
		private bool IsValidWhitelistTarget(GameObject obj)
		{
			return Settings.BotBase.Instance.TargetWhitelist.Any(whiteListEntry => whiteListEntry.NpcId == obj.NpcId) ||
				   GameObjectManager.Attackers.Contains(obj);
		}

		/// <summary>
		/// Is the player swimming, flying, moving or in any other way occupied?
		/// </summary>
		/// <returns></returns>
		private bool IsTraveling()
		{
			return MovementManager.IsSwimming || MovementManager.IsFlying || /*MovementManager.IsMoving*/ Core.Me.IsMounted || Core.Me.HasAura(1420) || Core.Me.HasAura(1520) ||
				   MovementManager.IsOccupied;
		}

		/// <summary>
		/// Determines whether auto facing a given target should be executed.
		/// </summary>
		/// <returns></returns>
		private bool ShouldExecuteAutoFaceTarget()
		{
			return GameSettingsManager.FaceTargetOnAction != Settings.BotBase.Instance.AutoFaceTarget
				   && WaitHelper.Instance.IsDoneWaiting(@"Target.AutoFace");
		}

		/// <summary>
		/// Determines whether we should select a new target, taking into account the current state and settings.
		/// The order in which these conditions are checked is important, so please be careful!
		/// </summary>
		/// <returns></returns>
		private bool ShouldExecuteAutoTarget()
		{
#if DEBUG
			using (new PerformanceLogger("ShouldExecuteAutoTarget"))
#endif
			{
				// Target search disabled
				if (!BotBase.Instance.AutoTarget/* || Settings.BotBase.Instance.AutoTargetingMode == TargetingMode.None*/)
					return false;

				if (!Core.Target.CheckAliveAndValid())
				{
					return true;
				}

				//auto deselect when target is unreachable
				//         if (Settings.BotBase.Instance.AutoDeSelectTarget && Core.Me.HasTarget && )
				//         {
				//	return true;
				//}

				//don't change target when target has specific aura
				if (Core.Target.IsBattleCharacter() &&
					(Core.Me.CurrentTarget.GetBattleCharacter().HasMyAura(1323) ||
					 Core.Me.CurrentTarget.GetBattleCharacter().HasMyAura(1986)))
				{
					return false;
				}

				// Too soon to select a new target?
				if (!WaitHelper.Instance.IsDoneWaiting(@"Target.AutoTarget", TimeSpan.FromMilliseconds(BotBase.Instance.TargetScanRate)))
					return false;

				// Does the player allow us to switch targets?
				if (BotBase.Instance.AutoTargetSwitch)
					return true;

				return !Core.Me.HasTarget;
			}
		}

		//private static bool IsValidTarget(GameObject t)
		//{
		//	if (!(t is BattleCharacter c)) return false;
		//	c

		//	return true;
		//}

		/// <summary>
		/// General caller methods for all post-target filters.
		/// </summary>
		/// <param name="group"></param>
		/// <returns></returns>
		private IEnumerable<BattleCharacter> ApplyPostFilters(IEnumerable<BattleCharacter> group)
		{
#if DEBUG
			using (new PerformanceLogger("TargetPostFilter"))
#endif
			{
				var result = group;

				//result = PostFilterDistance(result);
				result = PostFilterThreatList(result);
				result = PostFilterFate(result);

				if (WorldManager.InPvP)
				{
					if (BotBase.Instance.TargetWarMachinaFirst)
						result = result.OrderByDescending(i => i.HiddenGorgeType() == HiddenGorgeType.WarMachina); //优先选中机甲

					//if (BotBase.Instance.TargetPlayerHealthPctUnder != 0) //如果为0则不优先选中玩家或NPC
					//{
					//	result = result.OrderBy(TargetingWeights);

					//	float TargetingWeights(GameObject c)
					//	{
					//		float res = c.CurrentHealthPercent;
					//		switch (c.HiddenGorgeType())
					//		{
					//			case HiddenGorgeType.Player:
					//			case HiddenGorgeType.WarMachina:
					//				if (res < BotBase.Instance.TargetPlayerHealthPctUnder) res -= 100;
					//				break;
					//			case HiddenGorgeType.Mammet:
					//			case HiddenGorgeType.GobTank:
					//			case HiddenGorgeType.GobMercenary:
					//				break;

					//			case HiddenGorgeType.Tower:
					//			case HiddenGorgeType.Core:
					//			case HiddenGorgeType.Cannon:
					//			case HiddenGorgeType.CeruleumTank:
					//			case HiddenGorgeType.undefined:
					//				res = res + 1000;
					//				break;
					//		}

					//		return res;
					//	}
					if(BotBase.Instance.TargetPcOrNpcFirst != null)
					{
						if ((bool)BotBase.Instance.TargetPcOrNpcFirst) //如果优先选中玩家
						{
							result = result.OrderByDescending(i => i.Type == GameObjectType.Pc);
						}
						else //优先选中人偶、哥布林坦克、哥布林佣兵
						{
							result = result.OrderByDescending(i =>
								i.HiddenGorgeType() == HiddenGorgeType.Mammet ||
								i.HiddenGorgeType() == HiddenGorgeType.GobTank ||
								i.HiddenGorgeType() == HiddenGorgeType.GobMercenary);

							result = result.OrderByDescending(i =>
								i.Type == GameObjectType.Pc && i.CurrentHealthPercent <
								BotBase.Instance.TargetPlayerUnderThisHPPct);
						}
					}



					if (BotBase.Instance.TargetMountedEnemyFirst)
						result = result.OrderByDescending(i => i.IsMounted && !i.HasAura(1394)); //优先选中在坐骑上且没有移动速度降低debuff的敌人
					result = result.OrderByDescending(i => i.IsCasting && i.HasTarget && i.TargetGameObject.Type == GameObjectType.EventObject); //优先选中正在摸点/捡水的敌人
				}
				else
				{
					if (BotBase.Instance.TargetTankedOnly)
					{
						if (!PartyManager.IsInParty || Core.Me.IsTank() ||
							!PartyManager.VisibleMembers.Any(i => i.IsTank() && !i.IsMe))
						{
							return result;
						}

						//if (Core.Me.IsTank()/* && !Core.Me.Auras.Select(i=>i.Id).Intersect(new uint[]{79,91,743,1833}).Any()*/)
						//{
						//	return result;
						//}

						//if (!PartyManager.VisibleMembers.Any(i=>i.IsTank() && !i.IsMe))
						//{
						//	return result;
						//}

						switch (BotBase.Instance.AutoTargetingMode)
						{
							case TargetingMode.Nearest:
							case TargetingMode.BestAoE:
							case TargetingMode.OnlyWhitelisted:
							case TargetingMode.LowestHealth:
							case TargetingMode.LowestHealthPercent:
							case TargetingMode.HighestHealth:
							case TargetingMode.HighestHealthPercent:
							case TargetingMode.MostTargeted:
								result = result.Where(i => i.TargetGameObject is BattleCharacter bc && bc.IsTank());
								break;
						}
					}
				}

				return result;
			}
		}

		private IEnumerable<BattleCharacter> PostFilterThreatList(IEnumerable<BattleCharacter> group)
		{
			if (Settings.BotBase.Instance.EnableSmartPull && !WorldManager.InPvP)
				return group.Where(o => o.IsEnemy() && (GameObjectManager.Attackers.Contains(o) || o.HasBeenTaggedByPartyMember()));
			
			return group;
		}

		//private IEnumerable<BattleCharacter> PostFilterDistance(IEnumerable<BattleCharacter> group)
		//{
		//	if (Settings.BotBase.Instance.TargetScanMaxDistance == 0)
		//		return group.Where(o => o.IsInPullRange());

		//	return group.Where(o => o.CombatDistance() < Settings.BotBase.Instance.TargetScanMaxDistance);
		//}

		/// <summary>
		/// Applies a filter for FATE-only targets to a group of BattleCharacters.
		/// </summary>
		/// <param name="group"></param>
		/// <returns></returns>
		private IEnumerable<BattleCharacter> PostFilterFate(IEnumerable<BattleCharacter> group)
		{
			// FATE filter disabled? Return the group as-is.
			if (!Settings.BotBase.Instance.FateTargetFilter)
				return group;

			// Do we have aggro from something else? Prioritize those targets!
			if (GameObjectManager.Attackers.Any(o => o.TargetCharacter == Core.Me))
				return GameObjectManager.Attackers
					.Where(o => o.TargetCharacter == Core.Me)
					.OrderBy(o => o.Distance2DSqr());

			// No external attackers, prioritize FATE mobs.
			if (FateManager.WithinFate)
				return group
					.Where(o => o.IsFate)
					.OrderBy(o => o.Distance2DSqr());

			// Nothing special? Return group as-is.
			return group;
		}

		/// <summary>
		/// Target selection: Assist fixed character.
		/// </summary>
		/// <returns>Potential BattleCharacter object as the new target or null when no suitable target was found.</returns>
		private BattleCharacter TargetAssistFixedCharacter()
		{
			// No character name set.
			if (string.IsNullOrEmpty(Settings.BotBase.Instance.FixedCharacterName))
				return null;

			var character = GameObjectManager.GetObjectByObjectId(BotBase.Instance.FixedCharacterId).GetBattleCharacter() ??
							GameObjectManager.GetObjectsOfType<BattleCharacter>()
								.Where(c => c.Name == BotBase.Instance.FixedCharacterName && c.Type == BotBase.Instance.FixedCharacterType)
								.OrderBy(i => i.Distance2DSqr()).FirstOrDefault();

			// Character is not in the vicinity...
			if (character == null || !character.IsValid)
				return null;

			// Character doesn't have a target or it's not what we consider an enemy.
			if (!character.HasTarget ||
				!character.TargetGameObject.IsEnemy())
				return null;

			return character.TargetGameObject as BattleCharacter;
		}

		/// <summary>
		/// Target selection: Assist party leader.
		/// </summary>
		/// <returns>Potential BattleCharacter object as the new target or null when no suitable target was found.</returns>
		private BattleCharacter TargetAssistLeader()
		{
			// Not in party or party leader?
			if (!Core.Me.IsInMyParty() || Core.Me.IsInMyParty() && Core.Me.IsPartyLeader())
				return null;

			// Party leader is not in the vicinity...
			if (!PartyManager.PartyLeader.IsInObjectManager)
				return null;

			// Leader doesn't have a target or it's not what we consider an enemy.
			if (!PartyManager.PartyLeader.BattleCharacter.HasTarget ||
				!PartyManager.PartyLeader.BattleCharacter.TargetGameObject.IsEnemy())
				return null;

			return PartyManager.PartyLeader.BattleCharacter.TargetGameObject as BattleCharacter;
		}

		/// <summary>
		/// Target selection: Assist tank.
		/// </summary>
		/// <returns>Potential BattleCharacter object as the new target or null when no suitable target was found.</returns>
		private BattleCharacter TargetAssistTank()
		{
			// I am not in a party? I am the tank? Or there is no tank in my party?
			if (!Core.Me.IsInMyParty() || Core.Me.IsTank() || !PartyManager.VisibleMembers.Any(member => member.IsTank() && !member.IsMe))
				return null;

			// Find the closest tank in my party and target whatever they are targeting!
			var nearestTank = PartyManager.VisibleMembers
				.Where(member => member.IsTank())
				.OrderBy(member => member.BattleCharacter.Distance2DSqr())
				.FirstOrDefault();

			// No tanksywhirls?
			if (nearestTank == null)
				return null;

			// No targetsie?
			if (!nearestTank.BattleCharacter.HasTarget || !nearestTank.BattleCharacter.TargetGameObject.IsEnemy())
				return null;

			return nearestTank.BattleCharacter.TargetGameObject as BattleCharacter;
		}

		private BattleCharacter TargetAssistHighestLvlCharacter()
		{
			if (!Core.Me.IsInMyParty() ||
				!PartyManager.VisibleMembers.Any(member => member.SyncdLevel > Core.Me.ClassLevel))
				return null;

			var highestLevelChar = PartyManager.VisibleMembers
				.OrderByDescending(member => member.SyncdLevel)
				.ThenByDescending(member => member.Name)
				.FirstOrDefault();

			if (highestLevelChar == null || highestLevelChar.IsMe)
				return null;

			if (!highestLevelChar.BattleCharacter.HasTarget || !highestLevelChar.BattleCharacter.TargetGameObject.IsEnemy())
				return null;

			return highestLevelChar.BattleCharacter.TargetGameObject as BattleCharacter;
		}

		/// <summary>
		/// Target selection: Best AOE target.
		/// </summary>
		/// <returns>Potential BattleCharacter object as the new target or null when no suitable target was found.</returns>
		private BattleCharacter TargetBestAoeEnemy()
		{

			var potentialTargets = GameObjectManager.GetObjectsOfType<BattleCharacter>()
				.Where(IsValidTarget)
				.OrderByDescending(o => o.NearbyEnemyCount())
				.ThenBy(o => o.Distance2DSqr());

			var bestEnemy = ApplyPostFilters(potentialTargets).FirstOrDefault();

			return bestEnemy;
		}

		/// <summary>
		/// Target selection: Nearest enemy.
		/// </summary>
		/// <returns>Potential BattleCharacter object as the new target or null when no suitable target was found.</returns>
		private BattleCharacter TargetNearestEnemy()
		{
			var potentialTargets = GameObjectManager.GetObjectsOfType<BattleCharacter>()
				.Where(IsValidTarget)
				.OrderBy(o => o.Distance2DSqr());

			var nearestEnemy = ApplyPostFilters(potentialTargets).FirstOrDefault();

			return nearestEnemy;
		}

		/// <summary>
		/// Target selection: Whitelisted enemies only.
		/// </summary>
		/// <returns>Potential BattleCharacter object as the new target or null when no suitable target was found.</returns>
		private BattleCharacter TargetOnlyWhitelistedEnemy()
		{
			var potentialTargets = GameObjectManager.GetObjectsOfType<BattleCharacter>()
				.Where(IsValidTarget)
				.Where(IsValidWhitelistTarget)
				.OrderBy(o => o.Distance2DSqr());

			var validEnemy = ApplyPostFilters(potentialTargets).FirstOrDefault();

			return validEnemy;
		}

		/// <summary>
		/// Target selection: Highest HP enemy.
		/// </summary>
		/// <returns>Potential BattleCharacter object as the new target or null when no suitable target was found.</returns>
		private BattleCharacter TargetHighestHpEnemy()
		{
			var potentialTargets = GameObjectManager.GetObjectsOfType<BattleCharacter>()
				.Where(IsValidTarget)
				.OrderByDescending(o => o.CurrentHealth)
				.ThenBy(o => o.Distance2DSqr());

			var strongestEnemy = ApplyPostFilters(potentialTargets).FirstOrDefault();

			return strongestEnemy;
		}

		/// <summary>
		/// Target selection: Highest HP percent enemy.
		/// </summary>
		/// <returns>Potential BattleCharacter object as the new target or null when no suitable target was found.</returns>
		private BattleCharacter TargetHighestHpPercentEnemy()
		{
			var potentialTargets = GameObjectManager.GetObjectsOfType<BattleCharacter>()
				.Where(IsValidTarget)
				.OrderByDescending(o => o.CurrentHealthPercent)
				.ThenBy(o => o.Distance2DSqr());

			var strongestEnemy = ApplyPostFilters(potentialTargets).FirstOrDefault();

			return strongestEnemy;
		}

		/// <summary>
		/// Target selection: Lowest HP enemy.
		/// </summary>
		/// <returns>Potential BattleCharacter object as the new target or null when no suitable target was found.</returns>
		private BattleCharacter TargetLowestHpEnemy()
		{
			var potentialTargets = GameObjectManager.GetObjectsOfType<BattleCharacter>()
				.Where(IsValidTarget)
				.OrderBy(o => o.CurrentHealth)
				.ThenBy(o => o.Distance2DSqr());

			var weakestEnemy = ApplyPostFilters(potentialTargets).FirstOrDefault();

			return weakestEnemy;
		}

		/// <summary>
		/// Target selection: Lowest HP percent enemy.
		/// </summary>
		/// <returns>Potential BattleCharacter object as the new target or null when no suitable target was found.</returns>
		private BattleCharacter TargetLowestHpPercentEnemy()
		{
			var potentialTargets = GameObjectManager.GetObjectsOfType<BattleCharacter>()
				.Where(IsValidTarget)
				.OrderBy(o => o.CurrentHealthPercent)
				.ThenBy(o => o.Distance2DSqr());

			var weakestEnemy = ApplyPostFilters(potentialTargets).FirstOrDefault();

			return weakestEnemy;
		}

		private BattleCharacter TargetMostTargetedEnemy()
		{
			var potentialTargets = GameObjectManager.GetObjectsOfType<BattleCharacter>()
				.Where(IsValidTarget)
				.OrderByDescending(o => o.BeingTargetedCount())
				.ThenBy(o => o.Distance2DSqr());

			var mostTargetedEnemy = ApplyPostFilters(potentialTargets).FirstOrDefault();

			return mostTargetedEnemy;
		}
	}
}