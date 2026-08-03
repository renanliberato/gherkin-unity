using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace GherkinUnity
{
    /// <summary>
    /// Per-scenario state passed to a step definition when its first parameter is of this type.
    /// </summary>
    public sealed class BddScenarioContext
    {
        public BddScenarioContext(string featureName, string scenarioName)
        {
            FeatureName = featureName;
            ScenarioName = scenarioName;
        }

        public string FeatureName { get; }

        public string ScenarioName { get; }

        public Dictionary<string, object> Values { get; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Executes a parsed scenario against step-definition methods on a steps object: matches each
    /// step's keyword + regex pattern, converts captured groups to method arguments, runs
    /// Before/AfterScenario hooks, and fails with the failing step's file line for readability.
    /// </summary>
    public static class BddRunner
    {
        public static void Execute(BddScenario scenario, object steps, string featureName)
        {
            var context = new BddScenarioContext(featureName, scenario.Name);
            MethodInfo before = FindHook(steps.GetType(), typeof(BeforeScenarioAttribute));
            MethodInfo after = FindHook(steps.GetType(), typeof(AfterScenarioAttribute));

            try
            {
                before?.Invoke(steps, InvokeArgs(before, context));
            }
            catch (Exception e)
            {
                throw new AssertionException($"BeforeScenario hook failed: {e.Message}");
            }

            for (int i = 0; i < scenario.Steps.Count; i++)
            {
                BddStep step = scenario.Steps[i];
                MethodInfo definition = FindStepDefinition(steps.GetType(), step);
                if (definition == null)
                {
                    Assert.Fail($"No step definition matches step {i + 1}: \"{step}\" (line {step.LineNumber}). " +
                                "Add a [Given]/[When]/[Then] method with a matching regex in the steps class.");
                }

                object[] args;
                try
                {
                    args = BuildArguments(step, definition, context);
                }
                catch (Exception e)
                {
                    Assert.Fail($"Step {i + 1}: \"{step}\" (line {step.LineNumber}): cannot bind arguments to " +
                                $"{definition.Name}: {e.Message}");
                    throw;
                }

                try
                {
                    definition.Invoke(steps, args);
                }
                catch (TargetInvocationException e)
                {
                    throw new AssertionException(
                        $"Step {i + 1}: \"{step}\" (line {step.LineNumber}) failed: {e.InnerException?.Message ?? e.Message}");
                }
                catch (Exception e)
                {
                    throw new AssertionException(
                        $"Step {i + 1}: \"{step}\" (line {step.LineNumber}) failed: {e.Message}");
                }
            }

            try
            {
                after?.Invoke(steps, InvokeArgs(after, context));
            }
            catch (Exception e)
            {
                throw new AssertionException($"AfterScenario hook failed: {e.Message}");
            }
        }

        static MethodInfo FindHook(Type stepsType, Type attributeType)
        {
            foreach (MethodInfo method in stepsType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.GetCustomAttribute(attributeType) != null)
                {
                    return method;
                }
            }

            return null;
        }

        static MethodInfo FindStepDefinition(Type stepsType, BddStep step)
        {
            foreach (MethodInfo method in stepsType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                BddStepAttribute attribute = GetStepAttribute(method);
                if (attribute == null)
                {
                    continue;
                }

                if (attribute.Keyword != step.Keyword.ToString())
                {
                    continue;
                }

                if (Regex.IsMatch(step.Text, $"^{attribute.Pattern}$"))
                {
                    return method;
                }
            }

            return null;
        }

        static BddStepAttribute GetStepAttribute(MethodInfo method)
        {
            foreach (Attribute attribute in method.GetCustomAttributes(true))
            {
                if (attribute is BddStepAttribute stepAttribute)
                {
                    return stepAttribute;
                }
            }

            return null;
        }

        static object[] BuildArguments(BddStep step, MethodInfo definition, BddScenarioContext context)
        {
            BddStepAttribute attribute = GetStepAttribute(definition);
            Match match = Regex.Match(step.Text, $"^{attribute.Pattern}$");
            ParameterInfo[] parameters = definition.GetParameters();
            var args = new List<object>(parameters.Length);

            int parameterIndex = 0;
            if (parameters.Length > 0 && parameters[0].ParameterType == typeof(BddScenarioContext))
            {
                args.Add(context);
                parameterIndex++;
            }

            for (int i = 1; i <= match.Groups.Count - 1; i++)
            {
                if (parameterIndex >= parameters.Length)
                {
                    throw new InvalidOperationException(
                        $"pattern has {match.Groups.Count - 1} capture groups but method {definition.Name} " +
                        $"takes only {parameters.Length - (args.Count > 0 ? 1 : 0)} parameters.");
                }

                args.Add(ConvertValue(match.Groups[i].Value, parameters[parameterIndex].ParameterType));
                parameterIndex++;
            }

            if (step.DocString != null)
            {
                if (parameterIndex >= parameters.Length || parameters[parameterIndex].ParameterType != typeof(string))
                {
                    throw new InvalidOperationException(
                        $"step has a docstring but method {definition.Name} has no trailing string parameter.");
                }

                args.Add(step.DocString);
                parameterIndex++;
            }

            if (parameterIndex != parameters.Length)
            {
                throw new InvalidOperationException(
                    $"step \"{step}\" does not match method {definition.Name}: expected {parameters.Length} " +
                    $"parameters, got {parameterIndex}.");
            }

            return args.ToArray();
        }

        static object[] InvokeArgs(MethodInfo hook, BddScenarioContext context)
        {
            ParameterInfo[] parameters = hook.GetParameters();
            if (parameters.Length == 0)
            {
                return Array.Empty<object>();
            }

            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(BddScenarioContext))
            {
                return new object[] { context };
            }

            throw new InvalidOperationException($"hook {hook.Name} must take no parameters or a single BddScenarioContext.");
        }

        static object ConvertValue(string token, Type targetType)
        {
            try
            {
                if (targetType == typeof(string))
                {
                    return token;
                }

                if (targetType == typeof(int))
                {
                    return int.Parse(token, CultureInfo.InvariantCulture);
                }

                if (targetType == typeof(float))
                {
                    return float.Parse(token, CultureInfo.InvariantCulture);
                }

                if (targetType == typeof(double))
                {
                    return double.Parse(token, CultureInfo.InvariantCulture);
                }

                if (targetType == typeof(bool))
                {
                    return bool.Parse(token);
                }

                if (targetType == typeof(byte))
                {
                    return byte.Parse(token, CultureInfo.InvariantCulture);
                }

                return Convert.ChangeType(token, targetType, CultureInfo.InvariantCulture);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"cannot convert '{token}' to {targetType.Name}: {e.Message}");
            }
        }
    }
}
