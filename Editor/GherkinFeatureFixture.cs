using System.Collections.Generic;

namespace GherkinUnity
{
    /// <summary>
    /// NUnit bridge for Gherkin features: each concrete fixture binds one .feature file to a steps
    /// class, and every scenario (including each expanded outline row) becomes its own test case,
    /// named after the scenario. The bridge deliberately avoids referencing NUnit itself — the
    /// package assembly stays free of test-framework dependencies, so consuming test assemblies
    /// own the NUnit surface (attributes, TestCaseData naming, TestRunnerApi reporting).
    /// </summary>
    public abstract class GherkinFeatureFixture<TSteps>
        where TSteps : new()
    {
        protected abstract string FeatureName { get; }

        /// <summary>Returns one argument array per scenario in the feature file at
        /// <paramref name="fullPath"/>; each element is the scenario's <see cref="BddScenario"/>.
        /// Consuming fixtures map these to their test framework's case objects
        /// (e.g. NUnit <c>TestCaseData</c>) to control display names.</summary>
        protected static IEnumerable<object[]> FeatureCases(string fullPath)
        {
            BddFeature feature = GherkinParser.Load(fullPath);
            foreach (BddScenario scenario in feature.Scenarios)
            {
                yield return new object[] { scenario };
            }
        }

        protected void RunScenario(BddScenario scenario)
        {
            BddRunner.Execute(scenario, new TSteps(), FeatureName);
        }
    }
}
