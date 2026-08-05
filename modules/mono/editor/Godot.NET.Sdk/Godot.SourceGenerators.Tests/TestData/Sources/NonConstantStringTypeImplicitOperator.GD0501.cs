public class NonConstantStringTest : Godot.Node
{
	public void Foo()
	{
		Godot.NodePath x = {|GD0501:GetString()|};
		Godot.StringName y = {|GD0501:Tr({|GD0501:GetString()|})|};
	}

	string GetString()
	{
		return "hi";
	}
}
