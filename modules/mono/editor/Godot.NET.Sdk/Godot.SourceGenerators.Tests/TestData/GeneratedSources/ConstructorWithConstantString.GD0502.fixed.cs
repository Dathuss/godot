using Godot;

public class ConstantStringConstructorTest : Node
{
	public void Foo()
	{
		NodePath x = "hello";
		NodePath z = "hello";
		StringName tr = Tr("ACTION_ATTACK", "Action");
	}
}
