using Godot;

public class ConstantStringConstructorTest : Node
{
	public void Foo()
	{
		NodePath x = {|GD0502:new NodePath("hello")|};
		NodePath z = {|GD0502:new("hello")|};
		StringName tr = Tr({|GD0502:new StringName("ACTION_ATTACK")|}, {|GD0502:new StringName("Action")|});
	}
}
