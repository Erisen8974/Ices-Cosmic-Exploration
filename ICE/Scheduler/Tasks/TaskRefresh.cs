using ECommons.Automation;
using ECommons.Logging;
using ECommons.Throttlers;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Scheduler.Tasks
{
    internal class TaskRefresh
    {
        public static void Enqueue()
        {
            P.TaskManager.Enqueue(() => CloseMissionWindow(), "Closing Mission Window");
            if (SchedulerMain.TargetResearchState != SchedulerMain.ResearchTargetState.None)
            {
                P.TaskManager.Enqueue(() => CloseResearchWindow(), "Closing Research Window");
                P.TaskManager.EnqueueDelay(500);
                P.TaskManager.Enqueue(() => OpenResearchWindow(), "Opening Research Window");
            }
            P.TaskManager.Enqueue(() => OpenMissionWindow(), "Opening Mission Window");
        }

        internal unsafe static bool? CloseMissionWindow()
        {
            if (!IsAddonActive("WKSMission"))
                return true;

            if (TryGetAddonMaster<WKSMission>("WKSMission", out var m) && m.IsAddonReady)
            {
                if (EzThrottler.Throttle("Closing Mission Window"))
                    Callback.Fire(m.Base, true, 1);
            }

            return false;
        }

        internal unsafe static bool? OpenMissionWindow()
        {
            if (IsAddonActive("WKSMission"))
                return true;

            if (TryGetAddonMaster<WKSHud>("WKSHud", out var SpaceHud) && SpaceHud.IsAddonReady)
            {
                if (EzThrottler.Throttle("Opening the mission hud"))
                    SpaceHud.Mission();
            }

            return false;
        }

        internal unsafe static bool? CloseResearchWindow()
        {
            if (!IsAddonActive("WKSToolCustomize"))
                return true;

            if (TryGetAddonMaster<WKSHud>("WKSHud", out var SpaceHud) && SpaceHud.IsAddonReady)
            {
                if (EzThrottler.Throttle("Closing the Research hud"))
                    SpaceHud.Research();
            }

            return false;
        }

        internal unsafe static bool? OpenResearchWindow()
        {
            if (!EzThrottler.Throttle("Opening the Research hud"))
                return false;

            if (TryGetAddonMaster<WKSToolCustomize>("WKSToolCustomize", out var ResearchWindow) && ResearchWindow.IsAddonReady)
            {
                SchedulerMain.CurrentResearch = ResearchWindow.CurrentResearch;
                SchedulerMain.LevelResearch = ResearchWindow.TargetResearch;
                SchedulerMain.MaxResearch = ResearchWindow.MaxResearch;
                return true;
            }

            if (TryGetAddonMaster<WKSHud>("WKSHud", out var SpaceHud) && SpaceHud.IsAddonReady)
            {
                    SpaceHud.Research();
            }

            return false;
        }
    }
}
