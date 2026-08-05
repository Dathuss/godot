using System.Threading.Tasks;
using Xunit;

namespace Godot.SourceGenerators.Tests;

public class NonConstStringOperatorAnalyzerTest
{
    [Fact]
    public async Task NonConstantStringTypeImplicitOperatorCodeFixTest()
    {
        await CSharpCodeFixVerifier<NonConstStringOperatorCodeFixProvider, NonConstStringOperatorAnalyzer>
            .Verify("NonConstantStringTypeImplicitOperator.GD0501.cs", "NonConstantStringTypeImplicitOperator.GD0501.fixed.cs");
    }
}
