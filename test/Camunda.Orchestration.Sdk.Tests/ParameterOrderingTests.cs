using System.Reflection;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Regression test: generated methods must not have optional parameters
/// before required ones (CS1737). This validates the entire CamundaClient
/// surface, not a single method — catching any future spec ordering issues.
/// </summary>
public class ParameterOrderingTests
{
    [Fact]
    public void AllPublicMethods_HaveRequiredParamsBeforeOptional()
    {
        var methods = typeof(CamundaClient)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName); // exclude property getters/setters

        var violations = new List<string>();

        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            var seenOptional = false;

            foreach (var param in parameters)
            {
                if (param.HasDefaultValue || param.IsOptional)
                {
                    seenOptional = true;
                }
                else if (seenOptional)
                {
                    violations.Add(
                        $"{method.Name}: required parameter '{param.Name}' appears after optional parameter");
                    break;
                }
            }
        }

        Assert.Empty(violations);
    }
}
