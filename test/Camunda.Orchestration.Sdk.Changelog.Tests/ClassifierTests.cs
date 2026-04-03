using Xunit;

namespace Camunda.Orchestration.Sdk.Changelog.Tests;

public class ClassifierTests
{
    [Theory]
    [InlineData("JobActivationRequest", TypeRole.Request)]
    [InlineData("ProcessInstanceFilter", TypeRole.Request)]
    [InlineData("DecisionDefinitionSearchQuery", TypeRole.Request)]
    [InlineData("CreateProcessInput", TypeRole.Request)]
    [InlineData("MigrationInstruction", TypeRole.Request)]
    [InlineData("JobActivationResult", TypeRole.Response)]
    [InlineData("ProcessInstanceResponse", TypeRole.Response)]
    [InlineData("UserTaskSearchResult", TypeRole.Response)]
    [InlineData("ProblemDetail", TypeRole.Unknown)]
    [InlineData("SomeRandomType", TypeRole.Unknown)]
    public void ClassifiesBySuffix(string typeName, TypeRole expected)
    {
        Assert.Equal(expected, Classifier.ClassifyBySuffix(typeName));
    }

    [Fact]
    public void BuildRoleMapFromMethods()
    {
        var source = """
            namespace Test;
            public partial class CamundaClient
            {
                /// <remarks>Operation: searchJobs</remarks>
                public async Task<SearchJobsResponse> SearchJobsAsync(SearchJobsRequest body, CancellationToken ct = default) => null!;
            }
            public sealed class SearchJobsRequest { }
            public sealed class SearchJobsResponse { }
            """;

        var surface = Parser.ParseSource(source);
        var roles = Classifier.BuildRoleMap(surface);

        Assert.Equal(TypeRole.Request, roles["SearchJobsRequest"]);
        Assert.Equal(TypeRole.Response, roles["SearchJobsResponse"]);
    }

    [Theory]
    [InlineData("List<Foo>", "Foo")]
    [InlineData("Task<Bar>", "Bar")]
    [InlineData("string?", "string")]
    [InlineData("List<Foo>?", "Foo")]
    [InlineData("ProcessInstanceKey", "ProcessInstanceKey")]
    public void ExtractsTypeName(string typeExpr, string expected)
    {
        Assert.Equal(expected, Classifier.ExtractTypeName(typeExpr));
    }
}
