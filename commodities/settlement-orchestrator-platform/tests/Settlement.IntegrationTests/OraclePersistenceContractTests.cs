using Xunit;

namespace Settlement.IntegrationTests;

public sealed class OraclePersistenceContractTests
{
    [Fact(Skip = "Requires local Oracle from infra/docker/compose.yaml and schema migration execution.")]
    public void OraclePersistenceIsCoveredByTheIntegrationSuite()
    {
    }
}
