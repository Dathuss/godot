using Godot;

public class NonConstantStringTest : Godot.Node
{
	public void Foo()
	{
		Godot.NodePath x = new NodePath(GetString());
		Godot.StringName y = new StringName(Tr(new StringName(GetString())));
	}

	string GetString()
	{
		return "hi";
	}
}
