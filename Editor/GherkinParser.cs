using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace GherkinUnity
{
    public enum BddStepKeyword
    {
        Given,
        When,
        Then,
        And,
        But,
    }

    public sealed class BddStep
    {
        public BddStep(int lineNumber, BddStepKeyword keyword, string text)
        {
            LineNumber = lineNumber;
            Keyword = keyword;
            Text = text;
        }

        public int LineNumber { get; }

        public BddStepKeyword Keyword { get; }

        public string Text { get; }

        public string DocString { get; set; }

        public override string ToString() => $"{Keyword} {Text}";
    }

    public sealed class BddScenario
    {
        public BddScenario(string name, IReadOnlyList<BddStep> steps)
        {
            Name = name;
            Steps = steps;
        }

        public string Name { get; }

        public IReadOnlyList<BddStep> Steps { get; }

        public override string ToString() => Name;
    }

    public sealed class BddFeature
    {
        public BddFeature(string name, string description, IReadOnlyList<BddScenario> scenarios)
        {
            Name = name;
            Description = description;
            Scenarios = scenarios;
        }

        public string Name { get; }

        public string Description { get; }

        public IReadOnlyList<BddScenario> Scenarios { get; }
    }

    public sealed class BddParsingException : Exception
    {
        public BddParsingException(string path, int lineNumber, string message)
            : base($"{path}:{lineNumber}: {message}")
        {
        }
    }

    /// <summary>
    /// Minimal Gherkin parser: Feature, Background, Scenario, Scenario Outline + Examples tables,
    /// And/But keyword inheritance, comments and step docstrings. Background steps are prepended to
    /// every scenario; each outline row is expanded into a concrete scenario with
    /// &lt;placeholders&gt; substituted in step text.
    /// </summary>
    public static class GherkinParser
    {
        public static BddFeature Parse(string featureText, string sourcePath = "<feature>")
        {
            string[] lines = featureText.Replace("\r\n", "\n").Split('\n');

            string featureName = null;
            var description = new List<string>();
            var backgroundSteps = new List<BddStep>();
            List<BddStep> currentSteps = null;
            BddStepKeyword lastKeyword = BddStepKeyword.Given;
            BddScenario outline = null;
            bool inExamples = false;
            string examplesHeader = null;
            string[] examplesHeaders = null;
            var scenarios = new List<BddScenario>();

            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                int lineNumber = i + 1;
                string trimmed = raw.Trim();

                if (trimmed.Length == 0 || trimmed.StartsWith("#") || trimmed.StartsWith("@"))
                {
                    continue;
                }

                if (TryReadHeader(trimmed, "Feature:", out string featureTitle))
                {
                    featureName = featureTitle.Trim();
                    continue;
                }

                if (featureName == null)
                {
                    throw new BddParsingException(sourcePath, lineNumber, "Expected 'Feature:' header before any scenario.");
                }

                if (TryReadHeader(trimmed, "Background:", out _))
                {
                    if (scenarios.Count > 0 || outline != null)
                    {
                        throw new BddParsingException(sourcePath, lineNumber, "Background must appear before any scenario.");
                    }

                    currentSteps = backgroundSteps;
                    lastKeyword = BddStepKeyword.Given;
                    continue;
                }

                if (TryReadHeader(trimmed, "Scenario Outline:", out string outlineName) ||
                    TryReadHeader(trimmed, "Scenario Template:", out outlineName))
                {
                    outline = new BddScenario(outlineName.Trim(), new List<BddStep>());
                    currentSteps = (List<BddStep>)outline.Steps;
                    lastKeyword = BddStepKeyword.Given;
                    inExamples = false;
                    examplesHeader = null;
                    continue;
                }

                if (TryReadHeader(trimmed, "Scenario:", out string scenarioName))
                {
                    if (outline != null && !inExamples)
                    {
                        throw new BddParsingException(sourcePath, lineNumber, $"Scenario Outline '{outline.Name}' has no Examples table.");
                    }

                    outline = null;
                    inExamples = false;
                    examplesHeader = null;
                    currentSteps = new List<BddStep>();
                    scenarios.Add(new BddScenario(scenarioName.Trim(), currentSteps));
                    lastKeyword = BddStepKeyword.Given;
                    continue;
                }

                if (TryReadHeader(trimmed, "Examples:", out string examplesName) ||
                    TryReadHeader(trimmed, "Scenarios:", out examplesName))
                {
                    if (outline == null)
                    {
                        throw new BddParsingException(sourcePath, lineNumber, "Examples table without a matching Scenario Outline.");
                    }

                    inExamples = true;
                    examplesHeader = examplesName.Trim();
                    examplesHeaders = null;
                    continue;
                }

                if (trimmed.StartsWith("|"))
                {
                    if (outline == null || !inExamples)
                    {
                        throw new BddParsingException(sourcePath, lineNumber, "Table row outside of an Examples block.");
                    }

                    string[] cells = SplitTableRow(trimmed);
                    if (examplesHeaders == null)
                    {
                        examplesHeaders = cells;
                        continue;
                    }

                    if (examplesHeaders.Length != cells.Length)
                    {
                        throw new BddParsingException(sourcePath, lineNumber, "Examples row does not match the header column count.");
                    }

                    var rowValues = new Dictionary<string, string>();
                    for (int c = 0; c < cells.Length; c++)
                    {
                        rowValues[examplesHeaders[c]] = cells[c];
                    }

                    var instanceSteps = new List<BddStep>();
                    foreach (BddStep templateStep in outline.Steps)
                    {
                        instanceSteps.Add(new BddStep(
                            templateStep.LineNumber,
                            templateStep.Keyword,
                            Substitute(templateStep.Text, rowValues)));
                    }

                    string instanceName = Substitute(outline.Name, rowValues);
                    scenarios.Add(new BddScenario(instanceName, instanceSteps));
                    continue;
                }

                if (currentSteps != null && TryReadStep(trimmed, out BddStepKeyword keyword, out string stepText))
                {
                    if (outline != null && inExamples)
                    {
                        throw new BddParsingException(sourcePath, lineNumber, "Steps cannot appear after an Examples table.");
                    }

                    if (keyword == BddStepKeyword.And || keyword == BddStepKeyword.But)
                    {
                        keyword = lastKeyword;
                    }

                    lastKeyword = keyword;

                    var step = new BddStep(lineNumber, keyword, stepText);

                    string nextRaw = i + 1 < lines.Length ? lines[i + 1] : null;
                    if (nextRaw != null && nextRaw.Trim().StartsWith("\"\"\""))
                    {
                        i++;
                        var docString = new StringBuilder();
                        bool closed = false;
                        while (i + 1 < lines.Length)
                        {
                            i++;
                            string docLine = lines[i];
                            if (docLine.Trim().StartsWith("\"\"\""))
                            {
                                closed = true;
                                break;
                            }

                            docString.Append(docLine).Append('\n');
                        }

                        if (!closed)
                        {
                            throw new BddParsingException(sourcePath, lineNumber, "Unterminated docstring.");
                        }

                        step.DocString = docString.ToString();
                    }

                    currentSteps.Add(step);
                    continue;
                }

                if (currentSteps == null)
                {
                    description.Add(trimmed);
                    continue;
                }

                throw new BddParsingException(sourcePath, lineNumber, $"Unrecognized line: '{trimmed}'");
            }

            if (featureName == null)
            {
                throw new BddParsingException(sourcePath, 1, "No 'Feature:' header found.");
            }

            if (outline != null && !inExamples)
            {
                throw new BddParsingException(sourcePath, 1, $"Scenario Outline '{outline.Name}' has no Examples table.");
            }

            var finalScenarios = new List<BddScenario>(scenarios.Count);
            foreach (BddScenario scenario in scenarios)
            {
                var allSteps = new List<BddStep>(backgroundSteps.Count + scenario.Steps.Count);
                allSteps.AddRange(backgroundSteps);
                allSteps.AddRange(scenario.Steps);
                finalScenarios.Add(new BddScenario(scenario.Name, allSteps));
            }

            return new BddFeature(featureName, string.Join("\n", description), finalScenarios);
        }

        static bool TryReadHeader(string trimmed, string header, out string value)
        {
            if (trimmed.StartsWith(header, StringComparison.Ordinal))
            {
                value = trimmed.Substring(header.Length).Trim();
                return true;
            }

            value = null;
            return false;
        }

        static bool TryReadStep(string trimmed, out BddStepKeyword keyword, out string text)
        {
            foreach (string candidate in new[] { "Given", "When", "Then", "And", "But", "*" })
            {
                if (trimmed == candidate || trimmed.StartsWith(candidate + " ", StringComparison.Ordinal))
                {
                    keyword = candidate == "*"
                        ? BddStepKeyword.Given
                        : (BddStepKeyword)Enum.Parse(typeof(BddStepKeyword), candidate);
                    text = trimmed.Substring(candidate.Length).Trim();
                    return true;
                }
            }

            keyword = default;
            text = null;
            return false;
        }

        static string[] SplitTableRow(string trimmed)
        {
            string body = trimmed.TrimStart('|').TrimEnd('|');
            string[] cells = body.Split('|');
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = cells[i].Trim();
            }

            return cells;
        }

        static string Substitute(string text, Dictionary<string, string> values)
        {
            var builder = new StringBuilder(text);
            foreach (KeyValuePair<string, string> pair in values)
            {
                builder.Replace($"<{pair.Key}>", pair.Value);
            }

            return builder.ToString();
        }

        /// <summary>Loads and parses a feature file from <paramref name="fullPath"/>.</summary>
        public static BddFeature Load(string fullPath)
        {
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Gherkin feature file not found: {fullPath}");
            }

            return Parse(File.ReadAllText(fullPath, Encoding.UTF8), Path.GetFileName(fullPath));
        }

        public static float ParseFloat(string token) => float.Parse(token, CultureInfo.InvariantCulture);
    }
}
