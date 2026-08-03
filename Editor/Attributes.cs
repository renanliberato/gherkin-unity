using System;

namespace GherkinUnity
{
    [AttributeUsage(AttributeTargets.Method)]
    public abstract class BddStepAttribute : Attribute
    {
        protected BddStepAttribute(string pattern)
        {
            Pattern = pattern;
        }

        public string Pattern { get; }

        public string Keyword { get; protected set; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class GivenAttribute : BddStepAttribute
    {
        public GivenAttribute(string pattern)
            : base(pattern)
        {
            Keyword = "Given";
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class WhenAttribute : BddStepAttribute
    {
        public WhenAttribute(string pattern)
            : base(pattern)
        {
            Keyword = "When";
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ThenAttribute : BddStepAttribute
    {
        public ThenAttribute(string pattern)
            : base(pattern)
        {
            Keyword = "Then";
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AndAttribute : BddStepAttribute
    {
        public AndAttribute(string pattern)
            : base(pattern)
        {
            Keyword = "And";
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ButAttribute : BddStepAttribute
    {
        public ButAttribute(string pattern)
            : base(pattern)
        {
            Keyword = "But";
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class BeforeScenarioAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AfterScenarioAttribute : Attribute
    {
    }
}
