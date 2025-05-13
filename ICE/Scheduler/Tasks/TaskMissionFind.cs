using ECommons.Automation;
using ECommons.ExcelServices.TerritoryEnumeration;
using ECommons.Logging;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using ICE.Ui;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Scheduler.Tasks
{
    internal enum MissionTab
    {
        Critical,
        Provisional,
        Basic
    }

    internal static class TaskMissionFind
    {
        private static uint MissionId = 0;
        private static int MissionScore = 0;
        private static MissionTab MissionTab = MissionTab.Critical;
        private static MissionTab CurrentTab = MissionTab.Critical;
        private static uint currentClassJob => GetClassJobId();
        
        private static bool isGatherer => currentClassJob >= 16 && currentClassJob <= 18;
        private static bool hasCritical => C.EnabledMission
                                            .Where(e => MissionInfoDict[e.Id].JobId == currentClassJob)
                                            .Where(e => !UnsupportedMissions.Ids.Contains(e.Id))
                                            .Select(enabled => enabled.Id)
                                            .Intersect(C.CriticalMissions.Select(critical => critical.Id))
                                            .Any();
        private static bool hasWeather => C.EnabledMission
                                            .Where(e => MissionInfoDict[e.Id].JobId == currentClassJob)
                                            .Where(e => !UnsupportedMissions.Ids.Contains(e.Id))
                                            .Select(enabled => enabled.Id)
                                            .Intersect(C.WeatherMissions.Select(weather => weather.Id))
                                            .Any();
        private static bool hasTimed => C.EnabledMission
                                            .Where(e => MissionInfoDict[e.Id].JobId == currentClassJob)
                                            .Where(e => !UnsupportedMissions.Ids.Contains(e.Id))
                                            .Select(enabled => enabled.Id)
                                            .Intersect(C.TimedMissions.Select(timed => timed.Id))
                                            .Any();
        private static bool hasSequence => C.EnabledMission
                                            .Where(e => MissionInfoDict[e.Id].JobId == currentClassJob)
                                            .Where(e => !UnsupportedMissions.Ids.Contains(e.Id))
                                            .Select(enabled => enabled.Id)
                                            .Intersect(C.SequenceMissions.Select(sequence => sequence.Id))
                                            .Any();
        private static bool hasStandard => C.EnabledMission
                                            .Where(e => MissionInfoDict[e.Id].JobId == currentClassJob)
                                            .Where(e => !UnsupportedMissions.Ids.Contains(e.Id))
                                            .Select(enabled => enabled.Id)
                                            .Intersect(C.StandardMissions.Select(standard => standard.Id))
                                            .Any();

        public static void EnqueueResumeCheck()
        {
            if (CurrentLunarMission != 0)
            {
                if (!ModeChangeCheck(isGatherer))
                {
                    SchedulerMain.State = IceState.CheckScoreAndTurnIn;
                }
            }
            else
            {
                SchedulerMain.State = IceState.GrabMission;
            }
        }

        public static void Enqueue()
        {

            if (SchedulerMain.StopOnceHitCredits)
            {
                if (TryGetAddonMaster<AddonMaster.WKSHud>("WKSHud", out var hud) && hud.IsAddonReady)
                {
                    if (hud.LunarCredit >= 10000)
                    {
                        PluginLog.Debug($"[SchedulerMain] Stopping the plugin as you have {hud.LunarCredit} credits");
                        SchedulerMain.StopBeforeGrab = false;
                        SchedulerMain.StopOnceHitCredits = false;
                        SchedulerMain.State = IceState.Idle;
                        return;
                    }
                }
            }

            if (SchedulerMain.StopBeforeGrab)
            {
                SchedulerMain.StopBeforeGrab = false;
                SchedulerMain.State = IceState.Idle;
                return;
            }

            if (SchedulerMain.State != IceState.GrabMission)
                return;
            
            SchedulerMain.State = IceState.GrabbingMission;

            P.TaskManager.Enqueue(() => UpdateValues(), "Updating Task Mission Values");
            P.TaskManager.Enqueue(() => OpenMissionFinder(), "Opening the Mission finder");
            // if (hasCritical) {
            P.TaskManager.Enqueue(() => CriticalButton(), "Selecting Critical Mission");
            P.TaskManager.EnqueueDelay(200);
            P.TaskManager.Enqueue(() => FindMission(), "Checking to see if critical mission available");
            // }
            //if (hasWeather || hasTimed || hasSequence) // Skip Checks if enabled mission doesn't have weather, timed or sequence?
            //{
            P.TaskManager.Enqueue(() => WeatherButton(), "Selecting Weather");
            P.TaskManager.EnqueueDelay(200);
            P.TaskManager.Enqueue(() => FindMission(), "Checking to see if weather mission avaialable");
            //}
            P.TaskManager.Enqueue(() => BasicMissionButton(), "Selecting Basic Missions");
            P.TaskManager.EnqueueDelay(200);
            P.TaskManager.Enqueue(() => FindMission(), "Finding Basic Mission");
            P.TaskManager.Enqueue(() => FindResetMission(), "Checking for abandon mission");
            P.TaskManager.Enqueue(() => OpenMissionTab(), "Returning to target mission tab");
            P.TaskManager.EnqueueDelay(200);
            P.TaskManager.Enqueue(() => TargetMission(), "Returning to target mission tab");
            P.TaskManager.Enqueue(() => GrabMission(), "Grabbing the mission");
            P.TaskManager.EnqueueDelay(250);
            P.TaskManager.Enqueue(() => AbandonMission(), "Checking to see if need to leave mission");
            P.TaskManager.Enqueue(() =>
            {
                if (SchedulerMain.Abandon)
                {
                    P.TaskManager.Enqueue(() => CurrentLunarMission == 0);
                    P.TaskManager.EnqueueDelay(250);
                    SchedulerMain.Abandon = false;
                    SchedulerMain.State = IceState.GrabMission;
                }
            }, "Checking if you are abandoning mission");
            P.TaskManager.Enqueue(async () =>
            {
                if (CurrentLunarMission != 0)
                {
                    if (TryGetAddonMaster<WKSHud>("WKSHud", out var hud) && hud.IsAddonReady && !IsAddonActive("WKSMissionInfomation"))
                    {
                        var gatherer = isGatherer;
                        if (EzThrottler.Throttle("Opening Steller Missions"))
                        {
                            PluginLog.Debug("Opening Mission Menu");
                            hud.Mission();

                            while (!IsAddonActive("WKSMissionInfomation"))
                            {
                                PluginLog.Debug("Waiting for WKSMissionInfomation to be active");
                                await Task.Delay(500);
                            }
                            if (!ModeChangeCheck(gatherer)) {
                                SchedulerMain.State = IceState.StartCraft;
                            }
                        }
                    }
                }
            });
        }

        private static int ScoreMission(WKSMission.StellarMissions mission)
        {
            if (MissionInfoDict[mission.MissionId].JobId != GetClassJobId())
            {
                return 0;
            }
            if (UnsupportedMissions.Ids.Contains(mission.MissionId))
            {
                return 0;
            }
            if (C.DisabledMissions.Any(e => e.Id == mission.MissionId))
            {
                return 0;
            }
            int score = 0;
            int type = 1;
            foreach (var target in SchedulerMain.TargetResearch)
            {
                if (target)
                {
                    score += MissionInfoDict[mission.MissionId].ExperienceRewards.Where(reward => reward.Type==type).Sum(reward => reward.Amount);
                }
                type++;
            }

            if (SchedulerMain.TargetLunarCredits)
                score += (int)MissionInfoDict[mission.MissionId].LunarCredit;
            if (SchedulerMain.TargetCosmoCredits)
                score += (int)MissionInfoDict[mission.MissionId].CosmoCredit;

            if (C.EnabledMission.Any(e => e.Id == mission.MissionId))
            {
                if (C.CriticalMissions.Any(e => e.Id == mission.MissionId))
                    score += 6000;
                else if (C.WeatherMissions.Any(e => e.Id == mission.MissionId))
                    score += 5000;
                else if (C.TimedMissions.Any(e => e.Id == mission.MissionId))
                    score += 4000;
                else if (C.SequenceMissions.Any(e => e.Id == mission.MissionId))
                    score += 3000;
                else if (C.StandardMissions.Any(e => e.Id == mission.MissionId))
                    score += 2000;
                else
                    score += 1000;
            }
            return score;
        }

        internal static bool? UpdateValues()
        {
            SchedulerMain.Abandon = false;
            SchedulerMain.MissionName = string.Empty;
            MissionId = 0;
            MissionScore = 0;

            return true;
        }

        internal unsafe static bool? CriticalButton()
        {
            if (TryGetAddonMaster<WKSMission>("WKSMission", out var x) && x.IsAddonReady)
            {
                x.CriticalMissions();
                CurrentTab = MissionTab.Critical;
                return true;
            }
            return false;
        }

        internal unsafe static bool? WeatherButton()
        {
            if (TryGetAddonMaster<WKSMission>("WKSMission", out var x) && x.IsAddonReady)
            {
                x.ProvisionalMissions();
                CurrentTab = MissionTab.Provisional;
                return true;
            }
            return false;
        }

        internal unsafe static bool? BasicMissionButton()
        {
            if (TryGetAddonMaster<WKSMission>("WKSMission", out var x) && x.IsAddonReady)
            {
                x.BasicMissions();
                CurrentTab = MissionTab.Basic;
                return true;
            }
            return false;
        }

        internal unsafe static bool? OpenMissionTab()
        {
            if (LogThrottle)
                PluginLog.Debug($"[OpenMissionTab] Returnbing to tab: {CurrentTab}");

            switch (MissionTab)
            {
                case MissionTab.Critical:
                    return CriticalButton();
                case MissionTab.Provisional:
                    return WeatherButton();
                case MissionTab.Basic:
                    return BasicMissionButton();
                default:
                    PluginLog.Debug($"[OpenMissionTab] CurrentTab is not set to a valid value: {CurrentTab}");
                    SchedulerMain.State = IceState.Idle;
                    return false;
            }
        }


        internal unsafe static bool? OpenMissionFinder()
        {
            if (TryGetAddonMaster<WKSMission>("WKSMission", out var mission) && mission.IsAddonReady)
            {
                return true;
            }

            if (TryGetAddonMaster<WKSHud>("WKSHud", out var hud) && hud.IsAddonReady)
            {
                if (EzThrottler.Throttle("Opening Mission Hud", 1000))
                {
                    hud.Mission();
                }
            }

            return false;
        }

        internal unsafe static void FindMission()
        {
            if (TryGetAddonMaster<WKSMission>("WKSMission", out var x) && x.IsAddonReady)
            {
                foreach (var m in x.StellerMissions)
                {
                    var score = ScoreMission(m);
                    if (score <= MissionScore)
                        continue;
                    MissionScore = score;

                    PluginLog.Debug($"Mission Name: {m.Name} | MissionId: {m.MissionId} has been found. Setting value for sending");
                    SelectMission(m);
                }
            }

            if (MissionId == 0)
            {
                PluginLog.Debug($"No mission was found on tab {CurrentTab}, continuing on");
            }
        }


        private static bool TargetMission()
        {
            if (!EzThrottler.Throttle($"Selecting {CurrentTab} Mission"))
                return false;
            if (TryGetAddonMaster<WKSMission>("WKSMission", out var x) && x.IsAddonReady)
            {
                foreach (var m in x.StellerMissions)
                {
                    if (m.MissionId == MissionId)
                    {
                        m.Select();
                        SchedulerMain.MissionName = m.Name;
                        return true;
                    }
                }
            }
            return false;
        }

        private static void SelectMission(WKSMission.StellarMissions m)
        {
            MissionId = m.MissionId;
            MissionTab = CurrentTab;
        }


        internal unsafe static bool? FindResetMission()
        {
            PluginLog.Debug($"[Reset Mission Finder] Mission Name: {SchedulerMain.MissionName} | MissionId {MissionId}");
            if (MissionId != 0)
            {
                PluginLog.Debug("You already have a mission found, skipping finding a reset mission");
                return true;
            }

            if (TryGetAddonMaster<WKSMission>("WKSMission", out var x) && x.IsAddonReady)
            {
                PluginLog.Debug("found mission was false");
                var currentClassJob = GetClassJobId();
                var ranks = C.EnabledMission
                    .Where(e => MissionInfoDict[e.Id].JobId == currentClassJob)
                    .Select(e => MissionInfoDict[e.Id].Rank)
                    .ToList();
                if (ranks.Count == 0 && !SchedulerMain.TargetResearch.Any(e => e) && !SchedulerMain.TargetLunarCredits && !SchedulerMain.TargetCosmoCredits)
                {
                    PluginLog.Debug("No missions selected in UI, would abandon every mission");
                    SchedulerMain.State = IceState.Idle;
                    SchedulerMain.StopBeforeGrab = false;
                    SchedulerMain.StopOnceHitCredits = false;
                    return false;
                }
                var rankToReset = ranks.Max();

                foreach (var m in x.StellerMissions)
                {
                    var missionEntry = MissionInfoDict.FirstOrDefault(e => e.Key == m.MissionId);

                    if (missionEntry.Value == null || missionEntry.Value.JobId != currentClassJob)
                        continue;

                    PluginLog.Debug($"Mission: {m.Name} | Mission rank: {missionEntry.Value.Rank} | Rank to reset: {rankToReset}");
                    if (missionEntry.Value.Rank == rankToReset || (missionEntry.Value.Rank >= 4 && rankToReset >= 4))
                    {
                        if (EzThrottler.Throttle("Selecting Abandon Mission"))
                        {
                            PluginLog.Debug($"Setting SchedulerMain.MissionName = {m.Name}");
                            SelectMission(m);
                            SchedulerMain.Abandon = true;

                            PluginLog.Debug($"Mission Name: {SchedulerMain.MissionName}");

                            return true;
                        }
                    }
                }
            }

            return false;
        }

        internal unsafe static bool? GrabMission()
        {
            PluginLog.Debug($"[Grabbing Mission] Mission Name: {SchedulerMain.MissionName} | MissionId {MissionId} | SchedulerMain.MissionScore {MissionScore}");
            if (TryGetAddonMaster<SelectYesno>("SelectYesno", out var select) && select.IsAddonReady)
            {
                if (EzThrottler.Throttle("Selecting Yes", 250))
                {
                    select.Yes();
                }
            }
            else if (TryGetAddonMaster<WKSMission>("WKSMission", out var x) && x.IsAddonReady)
            {
                if (!MissionInfoDict.ContainsKey(MissionId))
                {
                    PluginLog.Debug($"No values were found for mission id {MissionId}... which is odd. Stopping the process");
                    P.TaskManager.Abort();
                }

                if (EzThrottler.Throttle("Firing off to initiate quest"))
                {
                    Callback.Fire(x.Base, true, 13, MissionId);
                }
            }
            else if (!IsAddonActive("WKSMission"))
            {
                return true;
            }

            return false;
        }

        internal unsafe static bool? AbandonMission()
        {
            if (SchedulerMain.Abandon == false)
            {
                return true;
            }
            else
            {
                if (TryGetAddonMaster<SelectYesno>("SelectYesno", out var select) && select.IsAddonReady)
                {
                    if (EzThrottler.Throttle("Confirming Abandon"))
                    {
                        select.Yes();
                        return true;
                    }
                }
                if (TryGetAddonMaster<WKSMissionInfomation>("WKSMissionInfomation", out var addon) && addon.IsAddonReady)
                {
                    if (EzThrottler.Throttle("Abandoning the mission"))
                        addon.Abandon();
                }
                else if (TryGetAddonMaster<WKSHud>("WKSHud", out var SpaceHud) && SpaceHud.IsAddonReady)
                {
                    if (EzThrottler.Throttle("Opening the mission hud"))
                        SpaceHud.Mission();
                }
            }

            return false;
        }

        private static bool ModeChangeCheck(bool gatherer)
        {
            if (C.OnlyGrabMission || MissionInfoDict[CurrentLunarMission].JobId2 != 0) // Manual Mode for Only Grab Mission / Dual Class Mission
            {
                SchedulerMain.State = IceState.ManualMode;
                return true;
            }
            else if (gatherer)
            {
                //Change to GathererMode Later
                SchedulerMain.State = IceState.ManualMode;

                return true;
            }

            return false;
        }
    }
}
