using System.Collections.Generic;
using NUnit.Framework;

namespace GherkinUnity
{
    /// <summary>
    /// NUnit bridge for Gherkin features: each concrete fixture binds one .feature file to a steps
    /// class, and every scenario (including each expanded outline row) becomes its own NUnit test
    /// case named after the scenario, so results are readable in the Test Runner window and any
    /// protocol built on top of it (e.g. TestRunnerApi-based CI reporting).
    /// </summary>
    public abstract class GherkinFeatureFixture<TSteps>
        where TSteps : new()
    {
        protected abstract string FeatureName { get; }

        /// <summary>Builds one NUnit test case per scenario in the feature file at
        /// <paramref name="fullPath"/>.</summary>
        protected static IEnumerable<TestCaseData> FeatureCases(string fullPath)
        {
            BddFeature feature = GherkinParser.Load(fullPath);
            foreach (BddScenario scenario in feature.Scenarios)
            {
                yield return new TestCaseData(scenario).SetName(scenario.Name);
            }
        }

        protected void RunScenario(BddScenario scenario)
        {
            BddRunner.Execute(scenario, new TSteps(), FeatureName);
        }
    }
}
