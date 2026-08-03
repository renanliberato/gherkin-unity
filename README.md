# Gherkin BDD for Unity

A minimal Gherkin (Cucumber-style) BDD test runner for Unity **Edit Mode** tests, with zero external dependencies. It parses real `.feature` files and executes scenarios through regex step definitions on top of the Unity Test Framework, so every scenario runs as its own named NUnit test case — visible in the Test Runner window, CI logs, and any protocol built on top of it (e.g. `TestRunnerApi`).

## Why not SpecFlow/Cucumber?

SpecFlow generates code-behind at MSBuild time and binds to a specific NUnit version, both of which fight Unity's asmdef/Bee compilation and its embedded NUnit fork. This package keeps the Gherkin syntax and the Given/When/Then workflow, but stays inside Unity's own test pipeline. Feature files remain portable: if you ever adopt SpecFlow, the `.feature` files import as-is.

## Features

- `Feature`, `Background`, `Scenario`, `Scenario Outline` + `Examples` tables
- `Given` / `When` / `Then` / `And` / `But` with keyword inheritance (`And`/`But` take the preceding keyword)
- Regex step definitions with typed argument conversion (`float`, `int`, `double`, `bool`, `byte`, `string`)
- Step docstrings (triple-quoted payloads)
- Each scenario (and each expanded outline row) runs as its own NUnit test, named after the scenario
- Failures report the exact failing step and its line number in the `.feature` file

## Installation

Add the package to your project's `Packages/manifest.json`:

```json
"com.renanliberato.gherkin-unity": "https://github.com/renanliberato/gherkin-unity.git#v0.1.0"
```

The package ships an Editor-only assembly (`GherkinUnity.Editor`) that references the NUnit test assemblies, so it is only usable from Edit Mode test assemblies. Reference it from your test `.asmdef`:

```json
{
    "name": "MyApp.EditorTests",
    "references": ["GherkinUnity.Editor"],
    "includePlatforms": ["Editor"],
    "optionalUnityReferences": ["TestAssemblies"]
}
```

## Usage

### 1. Write a feature file

`Assets/Tests/Editor/Features/Calculator.feature`:

```gherkin
Feature: Calculator

  Scenario: adding two numbers
    Given a calculator
      And I enter 2
      And I enter 3
    When I press add
    Then the result is 5

  Scenario Outline: adding and entering rows
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

### 2. Define the steps

Each step definition method carries a regex attribute. Captured groups are converted to the method's parameter types, in order. The steps object is instantiated fresh per scenario, so its fields are the scenario state. If a method's first parameter is a `BddScenarioContext`, it is injected automatically.

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

### 3. Bind the feature to a fixture

One fixture per feature file turns every scenario into an NUnit test case:

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

    static IEnumerable<TestCaseData> Scenarios() => FeatureCases(
        Path.Combine(Application.dataPath, "Tests/Editor/Features", "Calculator.feature"));

    [Test, TestCaseSource(nameof(Scenarios))]
    public void Scenario(BddScenario scenario) => RunScenario(scenario);
}
```

The `FeatureCases` helper returns one `TestCaseData` per scenario (outline rows included), named after the scenario.

## Limitations

- Edit Mode tests only (Editor platform + NUnit `TestAssemblies`).
- No tag-based execution filtering (tags are parsed and ignored).
- No Cucumber formatters or reports beyond NUnit's own output.

## License

MIT
