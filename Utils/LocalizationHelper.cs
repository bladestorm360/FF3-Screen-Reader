using MessageManager = Il2CppLast.Management.MessageManager;
using MasterManager = Il2CppLast.Data.Master.MasterManager;
using Job = Il2CppLast.Data.Master.Job;

namespace FFIII_ScreenReader.Utils
{
    /// <summary>
    /// Helper for retrieving localized text from the game's MessageManager.
    /// Consolidates the repeated MessageManager lookup pattern across multiple files.
    /// </summary>
    public static class LocalizationHelper
    {
        /// <summary>
        /// Gets localized text from MessageManager by message ID.
        /// </summary>
        /// <param name="mesId">The message ID (e.g., "ITEM_NAME_001")</param>
        /// <param name="stripMarkup">Whether to strip icon markup from the result</param>
        /// <returns>Localized text, or null if not found or empty</returns>
        public static string GetText(string mesId, bool stripMarkup = true)
        {
            if (string.IsNullOrEmpty(mesId))
                return null;

            var manager = MessageManager.Instance;
            if (manager == null)
                return null;

            string text = manager.GetMessage(mesId, false);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            return stripMarkup ? TextUtils.StripIconMarkup(text) : text;
        }

        /// <summary>
        /// Gets localized text with a fallback value if not found.
        /// </summary>
        /// <param name="mesId">The message ID</param>
        /// <param name="fallback">Value to return if localization fails</param>
        /// <param name="stripMarkup">Whether to strip icon markup from the result</param>
        /// <returns>Localized text, or fallback if not found</returns>
        public static string GetTextOrDefault(string mesId, string fallback, bool stripMarkup = true)
        {
            return GetText(mesId, stripMarkup) ?? fallback;
        }

        /// <summary>
        /// Gets job name from job ID using MasterManager.GetList pattern.
        /// </summary>
        /// <param name="jobId">The job ID</param>
        /// <returns>Localized job name, or null if not found</returns>
        public static string GetJobName(int jobId)
        {
            if (jobId <= 0)
                return null;

            var masterManager = MasterManager.Instance;
            if (masterManager == null)
                return null;

            var jobList = masterManager.GetList<Job>();
            if (jobList == null || !jobList.ContainsKey(jobId))
                return null;

            var job = jobList[jobId];
            if (job == null)
                return null;

            return GetText(job.MesIdName);
        }
    }
}
