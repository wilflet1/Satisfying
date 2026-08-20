using System;

namespace Satisfying.Shared
{
    /// <summary>
    /// Marks a tuning field as live-editable. The in-game tuning panel builds itself by reflecting
    /// over these, so adding a knob to the game means adding one field here - no UI work.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class TuneAttribute : Attribute
    {
        public string Category;
        public float Min;
        public float Max;
        public string Label;
        public string Tip;

        public TuneAttribute(string category, float min, float max)
        {
            Category = category;
            Min = min;
            Max = max;
        }
    }

    /// <summary>Fields marked with this are never sent over the network (purely local view feel).</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Class, AllowMultiple = false)]
    public sealed class LocalOnlyAttribute : Attribute { }
}
