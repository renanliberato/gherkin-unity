---
name: gherkin-unity
description: Integrate the Gherkin BDD for Unity package (com.renanliberato.gherkin-unity, github.com/renanliberato/gherkin-unity) into a Unity project, write .feature files, regex step definitions, and feature fixtures, then validate them through the project's Unity test runner. Use when asked to add BDD, Gherkin, or Cucumber-style tests to a Unity project, create feature files, steps, or fixtures with this package, or fix failing Gherkin tests.
---

# Gherkin BDD for Unity

Parse real `.feature` files and run each scenario as its own test case in Unity Edit Mode tests. The package (`com.renanliberato.gherkin-unity`) is a small UPM package installed via git URL. It is **NUnit-free by design**: the engine (`GherkinParser`, `BddRunner`, step attributes, `GherkinFeatureFixture<TSteps>`) references no test framework — the consuming project's test assembly owns the NUnit surface (attributes, display names, reporting). The package assembly is Editor-only.

## 1. One-time integration

1. Add the git dependency to `Packages/manifest.json` (pin a released tag):
   ```json
   "com.renanliberato.gherkin-unity": "https://github.com/renanliberato/gherkin-unity.git#v0.4.0"
   ```
   After changing the URL, delete any stale entry for the package in `Packages/packages-lock.json` and the matching `Library/PackageCache/com.renanliberato.gherkin-unity@*` folder if the fetch appears stuck.
2. Add the assembly reference to the project's Edit Mode test `.asmdef` (the one that already has `"optionalUnityReferences": ["TestAssemblies"]`):
   ```json
   "references": ["GherkinUnity.Editor"]
   ```
3. Never modify the package's own asmdef to add `"optionalUnityReferences": ["TestAssemblies"]` — Unity silently skips asmdefs that do this inside git-fetched (immutable) packages. The package must stay NUnit-free; the consumer provides NUnit.

## 2. Create a feature file

Place `.feature` files under the test assembly, e.g. `Assets/<...>/Tests/Editor/Bdd/Features/<Name>.feature`. Supported Gherkin:

- `Feature: <name>` — required header; free-form description lines after it are allowed (keyword-looking words like "When ..." in description text are fine — steps are only parsed inside scenarios).
- `Background:` — steps prepended to every following scenario; must appear before any scenario.
- `Scenario: <name>` — plain scenario.
- `Scenario Outline: <name>` + `Examples:` table — each row expands into one scenario; `<column>` placeholders are substituted into step text and the scenario name. A `Scenario Outline` without an `Examples` table is a parse error.
- Steps: `Given` / `When` / `Then` / `And` / `But` (or `*`). `And`/`But` inherit the preceding Given/When/Then keyword.
- Comments (`#`), tags (`@tag`, parsed and ignored), and step docstrings (triple-quoted `"""` blocks attached to the preceding step) are supported.

Example (`Calculator.feature`):

```gherkin
Feature: Calculator

  Scenario: adding two numbers
    Given a calculator
      And I enter 2
      And I enter 3
    When I press add
    Then the result is 5

  Scenario Outline: adding rows
    Given a calculator
      And I enter <a>
      And I enter <b>
    When I press add
    Then the result is <expected>

    Examples:
      | a | b | expected |
      | 1 | 1 | 2        |
      | 2 | 3 | 5        |
```

## 3. Create step definitions

A steps class with one method per step pattern. Rules:

- Annotate with `[Given("regex")]`, `[When("regex")]`, or `[Then("regex")]` (also `[And]`/`[But]`, rarely needed — keyword matching is on the *normalized* keyword, so write `[Given]` for steps that read as `And` after a `Given`).
- The pattern is anchored (`^pattern$`); capture groups map left-to-right onto method parameters (after an optional leading `BddScenarioContext`). Supported conversions: `string`, `int`, `float`, `double`, `bool`, `byte` (invariant culture).
- A fresh steps instance is created per scenario — instance fields are the scenario state.
- Optional `[BeforeScenario]` / `[AfterScenario]` hooks (no args or a single `BddScenarioContext`).
- A step with a docstring passes it to a trailing `string` parameter if declared.
- Failures: any exception thrown in a step fails the scenario; the runner wraps failures as `BddStepException` with the failing step and its feature-file line. `NUnit.Framework.Assert.*` in step bodies works normally (consumer owns NUnit).

```csharp
using GherkinUnity;
using NUnit.Framework;

public class CalculatorSteps
{
    readonly Calculator _calculator = new Calculator();

    [Given(@"a calculator")]
    public void GivenCalculator() { }

    [Given(@"I enter (-?\d+)")]
    public void GivenEnter(int value) => _calculator.Enter(value);

    [When(@"I press add")]
    public void WhenPressAdd() => _calculator.Add();

    [Then(@"the result is (-?\d+)")]
    public void ThenResultIs(int expected) => Assert.AreEqual(expected, _calculator.Result);
}
```

## 4. Create the fixture

One NUnit fixture per feature file. The concrete fixture declares the `[Test]`/`[TestCaseSource]` method; `FeatureCases(fullPath)` returns one `object[]` per scenario (outline rows included), each wrapping a `BddScenario`:

```csharp
using System.Collections.Generic;
using System.IO;
using GherkinUnity;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class CalculatorFeatureFixture : GherkinFeatureFixture<CalculatorSteps>
{
    protected override string FeatureName => "Calculator";

    static IEnumerable<TestCaseData> Scenarios()
    {
        foreach (object[] args in FeatureCases(
            Path.Combine(Application.dataPath, "Tests/Editor/Bdd/Features", "Calculator.feature")))
        {
            yield return new TestCaseData(args).SetName(((BddScenario)args[0]).Name);
        }
    }

    [Test, TestCaseSource(nameof(Scenarios))]
    public void Scenario(BddScenario scenario)
    {
        RunScenario(scenario);
    }
}
```

## 5. Validate

Run the tests through the consuming project's Unity test workflow (edit mode):

```bash
./unity-test run CalculatorFeatureFixture   # or filter by full fixture name
./unity-test results                        # total / passed / failed
./unity-test events                         # per-test testFinished proof
```

Expect exactly one test per scenario plus one per Examples row, named after the scenarios. Common failure modes and their messages:

- `BddParsingException : <file>:<line>: ...` — malformed `.feature` (check the line).
- `No step definition matches step N: "<step>" (line L)` — pattern text or keyword mismatch; remember `And`/`But` bind to the preceding keyword's attribute.
- `cannot bind arguments ...` — capture group count ≠ method parameters.
- `Step N: "<step>" (line L) failed: <message>` — assertion inside the step.

## 6. Version bumps (maintainers)

Tagged releases are referenced by hash in consumers' manifests. To release: bump `version` in `package.json`, commit, `git tag vX.Y.Z` + push tag, then update every consumer manifest to `#vX.Y.Z` (and clear the stale lock entry + cache folder). Do not move existing tags.
