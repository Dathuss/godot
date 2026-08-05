using System.Threading.Tasks;
using Xunit;

namespace Godot.SourceGenerators.Tests;

public class ConstStringConstructorAnalyzerTest
{
    [Fact]
    public async Task ConstructorWithConstantStringCodeFixTest()
    {
        await CSharpCodeFixVerifier<ConstStringConstructorCodeFixProvider, ConstStringConstructorAnalyzer>
            .Verify("ConstructorWithConstantString.GD0502.cs", "ConstructorWithConstantString.GD0502.fixed.cs");
    }
}
