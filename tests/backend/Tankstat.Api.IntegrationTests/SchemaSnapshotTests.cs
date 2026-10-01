using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// schema.graphql (at the repository root) is the contract the frontend's generated types are built from.
/// This fails when the API's schema changed without exporting it: run `mise run codegen`.
/// </summary>
[Collection(ApiCollection.Name)]
public class SchemaSnapshotTests(ApiFixture api)
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tankstat.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Tankstat.slnx not found above the test output directory.");
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();

    [Fact]
    public async Task ExportedSchema_IsUpToDate()
    {
        var executor = await api.Factory.Services.GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync();
        var committed = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "schema.graphql"));

        Assert.True(
            Normalize(executor.Schema.ToString()) == Normalize(committed),
            "schema.graphql is out of date with the API. Run `mise run codegen` and commit schema.graphql and src/frontend/gql/generated.ts.");
    }
}
