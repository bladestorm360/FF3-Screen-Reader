using FFIII_ScreenReader.Field;

namespace FFIII_ScreenReader.Core.Filters
{
    /// <summary>
    /// Filters out ToLayer (layer transition) entities when enabled.
    /// These are staircase/ladder transitions between map layers (e.g., floor changes in dungeons).
    /// </summary>
    internal class ToLayerFilter : IEntityFilter
    {
        private bool isEnabled = false;

        public bool IsEnabled
        {
            get => isEnabled;
            set => isEnabled = value;
        }

        public string Name => "Layer Transition Filter";

        public FilterTiming Timing => FilterTiming.OnAdd;

        /// <summary>
        /// Returns true if the entity should be shown, false if it should be hidden.
        /// When enabled, hides EventEntity instances with EventTypeName == "ToLayer".
        /// </summary>
        public bool PassesFilter(NavigableEntity entity, FilterContext context)
        {
            if (entity is EventEntity eventEntity && eventEntity.EventTypeName == "ToLayer")
                return false;
            return true;
        }

        public void OnEnabled()
        {
        }

        public void OnDisabled()
        {
        }
    }
}
