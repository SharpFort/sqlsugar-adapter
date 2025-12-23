using Xunit;
using Casbin.Adapter.SqlSugar.UnitTest.Integration;

namespace Casbin.Adapter.SqlSugar.IntegrationTest.Integration
{
    /// <summary>
    /// Collection definition for integration tests.
    /// This ensures all test classes marked with [Collection("IntegrationTests")]
    /// share a single TransactionIntegrityTestFixture instance.
    ///
    /// DisableParallelization = true ensures tests run sequentially to prevent
    /// race conditions and schema conflicts.
    /// </summary>
    [CollectionDefinition("IntegrationTests", DisableParallelization = true)]
    public class IntegrationTestCollection : ICollectionFixture<TransactionIntegrityTestFixture>
    {
        // This class has no code, and is never instantiated.
        // Its purpose is simply to be the place to apply [CollectionDefinition]
        // and all the ICollectionFixture<> interfaces.
    }
}
